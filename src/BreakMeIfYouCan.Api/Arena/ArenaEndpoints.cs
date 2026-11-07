using BreakMeIfYouCan.Api.Data;
using BreakMeIfYouCan.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BreakMeIfYouCan.Api.Arena;

public static class ArenaEndpoints
{
    private const string IdempotencyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapArenaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/arena").WithTags("Arena");

        group.MapGet("/session", GetSession)
            .WithName("GetArenaSession")
            .RequireRateLimiting("arena");

        group.MapPost("/reset", Reset)
            .WithName("ResetArenaSandbox")
            .RequireRateLimiting("arena-write");

        group.MapPost("/transfers", Transfer)
            .WithName("CreateArenaTransfer")
            .RequireRateLimiting("arena-write");

        group.MapPost("/runs", Run)
            .WithName("RunArenaChallenge")
            .RequireRateLimiting("arena-run");

        group.MapGet("/ledger", GetLedger)
            .WithName("GetArenaLedger")
            .RequireRateLimiting("arena");

        return app;
    }

    private static async Task<IResult> GetSession(
        HttpContext context,
        VisitorCookie cookie,
        SandboxService sandboxes,
        IOptions<ArenaOptions> options,
        CancellationToken cancellationToken)
    {
        var visitor = cookie.Resolve(context);
        var session = await sandboxes.GetOrCreateAsync(visitor, cancellationToken);
        return Results.Ok(ToView(session, options.Value, cookie.Protect(visitor)));
    }

    private static async Task<IResult> Reset(
        HttpContext context,
        VisitorCookie cookie,
        SandboxService sandboxes,
        IOptions<ArenaOptions> options,
        CancellationToken cancellationToken)
    {
        if (!options.Value.EnableReset)
        {
            throw new ArenaException("Sandbox reset is disabled by configuration.", StatusCodes.Status403Forbidden);
        }

        var visitor = cookie.Resolve(context);
        var session = await sandboxes.ResetAsync(visitor, cancellationToken);
        return Results.Ok(ToView(session, options.Value, cookie.Protect(visitor)));
    }

    private static async Task<IResult> Transfer(
        TransferRequest request,
        HttpContext context,
        VisitorCookie cookie,
        SandboxService sandboxes,
        TransferExecutor executor,
        CancellationToken cancellationToken)
    {
        var visitor = cookie.Resolve(context);
        var session = await sandboxes.GetOrCreateAsync(visitor, cancellationToken);

        if (session.Accounts.All(a => a.Id != request.FromAccountId) ||
            session.Accounts.All(a => a.Id != request.ToAccountId))
        {
            throw new ArenaException("Both accounts must belong to your sandbox.", StatusCodes.Status403Forbidden);
        }

        if (!TransferStrategies.IsValid(request.Strategy))
        {
            throw new ArenaException("Strategy must be 'optimistic' or 'pessimistic'.", StatusCodes.Status422UnprocessableEntity);
        }

        var idempotencyKey = context.Request.Headers[IdempotencyHeader].ToString();
        if (idempotencyKey.Length > 120)
        {
            throw new ArenaException("The Idempotency-Key header must be at most 120 characters.", StatusCodes.Status422UnprocessableEntity);
        }

        var outcome = await executor.ExecuteAsync(new TransferCommand
        {
            SandboxId = session.Sandbox.Id,
            FromAccountId = request.FromAccountId,
            ToAccountId = request.ToAccountId,
            AmountCents = request.AmountCents,
            Strategy = TransferStrategies.Normalize(request.Strategy),
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
        }, cancellationToken);

        var response = new TransferResponse
        {
            TransferId = outcome.TransferId,
            Status = outcome.Status,
            Replayed = outcome.Replayed,
            Strategy = outcome.Strategy,
            Attempts = outcome.Attempts,
            Conflicts = outcome.Conflicts,
            DurationMs = outcome.DurationMs,
            FailureReason = outcome.FailureReason,
            Balances = outcome.Balances,
        };

        var statusCode = outcome.Replayed
            ? StatusCodes.Status200OK
            : outcome.Status == TransferStatus.Completed
                ? StatusCodes.Status201Created
                : StatusCodes.Status409Conflict;

        return Results.Json(response, statusCode: statusCode);
    }

    private static async Task<IResult> Run(
        ArenaRunRequest request,
        HttpContext context,
        VisitorCookie cookie,
        SandboxService sandboxes,
        ArenaRunService runs,
        CancellationToken cancellationToken)
    {
        var visitor = cookie.Resolve(context);
        var session = await sandboxes.GetOrCreateAsync(visitor, cancellationToken);
        var result = await runs.RunAsync(session.Sandbox.Id, session.Accounts, request, cancellationToken);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetLedger(
        HttpContext context,
        int? limit,
        VisitorCookie cookie,
        SandboxService sandboxes,
        ArenaDbContext db,
        LedgerIntegrityService integrity,
        CancellationToken cancellationToken)
    {
        var visitor = cookie.Resolve(context);
        var session = await sandboxes.GetOrCreateAsync(visitor, cancellationToken);
        var take = Math.Clamp(limit ?? 25, 1, 100);

        var transfers = await db.Transfers
            .AsNoTracking()
            .Where(t => t.SandboxId == session.Sandbox.Id)
            .OrderByDescending(t => t.CreatedAt)
            .Take(take)
            .Include(t => t.Entries)
            .ToListAsync(cancellationToken);

        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(a => a.SandboxId == session.Sandbox.Id)
            .ToListAsync(cancellationToken);

        var codes = accounts.ToDictionary(a => a.Id, a => a.Code);
        var report = await integrity.CheckAsync(db, session.Sandbox.Id, cancellationToken);

        return Results.Ok(new LedgerView
        {
            Transfers = transfers.Select(t => new LedgerTransferView
            {
                TransferId = t.Id,
                AmountCents = t.AmountCents,
                Status = t.Status,
                Strategy = t.Strategy,
                Attempts = t.Attempts,
                Conflicts = t.Conflicts,
                FromCode = codes.GetValueOrDefault(t.FromAccountId, "?"),
                ToCode = codes.GetValueOrDefault(t.ToAccountId, "?"),
                FailureReason = t.FailureReason,
                CreatedAt = t.CreatedAt,
                Entries = t.Entries
                    .OrderBy(e => e.Direction == LedgerDirections.Debit ? 0 : 1)
                    .Select(e => new LedgerEntryView
                    {
                        Direction = e.Direction,
                        AmountCents = e.AmountCents,
                        AccountCode = codes.GetValueOrDefault(e.AccountId, "?"),
                    })
                    .ToList(),
            }).ToList(),
            Integrity = report,
        });
    }

    private static SandboxView ToView(SandboxSession session, ArenaOptions options, string? sandboxToken = null)
    {
        var scenario = ArenaScenarioKeys.StarterScenario;
        return new SandboxView
        {
            SandboxId = session.Sandbox.Id,
            ScenarioKey = scenario.Key,
            ScenarioName = scenario.Name,
            ScenarioDescription = scenario.Description,
            InitialBalanceCents = session.Sandbox.InitialBalanceCents,
            Accounts = session.Accounts.Select(a => new AccountView
            {
                Id = a.Id,
                Code = a.Code,
                Label = a.Label,
                BalanceCents = a.BalanceCents,
            }).ToList(),
            Limits = new ArenaLimits
            {
                MaxRequestsPerRun = Math.Min(options.MaxRequestsPerRun, scenario.MaxRequests),
                MaxConcurrencyPerRun = Math.Min(options.MaxConcurrencyPerRun, scenario.MaxConcurrency),
                MaxAmountCents = scenario.MaxAmountCents,
                GlobalConcurrencyLimit = options.GlobalConcurrencyLimit,
                MaxRetryAttempts = options.MaxRetryAttempts,
            },
            CreatedAt = session.Sandbox.CreatedAt,
            SandboxToken = sandboxToken,
        };
    }
}
