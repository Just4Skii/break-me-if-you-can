using BreakMeIfYouCan.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BreakMeIfYouCan.Api.Arena;

public sealed record SandboxSession(Sandbox Sandbox, IReadOnlyList<Account> Accounts);

/// <summary>
/// Creates and loads the anonymous visitor sandbox. A reset archives the current sandbox
/// instead of deleting history: the ledger is append-only by design, so it cannot be
/// silently rewritten.
/// </summary>
public sealed class SandboxService(IServiceScopeFactory scopeFactory, ILogger<SandboxService> logger)
{
    public async Task<SandboxSession> GetOrCreateAsync(Guid visitorId, CancellationToken cancellationToken)
    {
        var existing = await LoadActiveAsync(visitorId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        try
        {
            return await CreateAsync(visitorId, cancellationToken);
        }
        catch (DbUpdateException exception) when (PostgresErrors.IsUniqueViolation(exception))
        {
            logger.LogInformation("Sandbox creation raced for visitor {VisitorId}; loading the winner.", visitorId);
            var raced = await LoadActiveAsync(visitorId, cancellationToken);
            return raced ?? throw new ArenaException("The sandbox could not be created.", StatusCodes.Status503ServiceUnavailable);
        }
    }

    public async Task<SandboxSession?> LoadActiveAsync(Guid visitorId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArenaDbContext>();

        var sandbox = await db.Sandboxes
            .AsNoTracking()
            .Include(s => s.Accounts)
            .SingleOrDefaultAsync(s => s.VisitorId == visitorId && s.ArchivedAt == null, cancellationToken);

        return sandbox is null ? null : ToSession(sandbox);
    }

    public async Task<SandboxSession> ResetAsync(Guid visitorId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArenaDbContext>();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var active = await db.Sandboxes
            .Where(s => s.VisitorId == visitorId && s.ArchivedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var sandbox in active)
        {
            sandbox.ArchivedAt = now;
        }

        var created = NewSandbox(visitorId, now);
        db.Sandboxes.Add(created);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Reset sandbox for visitor {VisitorId}; new sandbox {SandboxId}.", visitorId, created.Id);
        return ToSession(created);
    }

    private async Task<SandboxSession> CreateAsync(Guid visitorId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArenaDbContext>();

        var created = NewSandbox(visitorId, DateTimeOffset.UtcNow);
        db.Sandboxes.Add(created);
        await db.SaveChangesAsync(cancellationToken);
        return ToSession(created);
    }

    private static Sandbox NewSandbox(Guid visitorId, DateTimeOffset now)
    {
        var scenario = ArenaScenarioKeys.StarterScenario;
        var sandbox = new Sandbox
        {
            Id = Guid.NewGuid(),
            VisitorId = visitorId,
            ScenarioKey = scenario.Key,
            InitialBalanceCents = scenario.InitialBalanceCents,
            CreatedAt = now,
        };

        sandbox.Accounts.Add(new Account
        {
            Id = Guid.NewGuid(),
            SandboxId = sandbox.Id,
            Code = "A",
            Label = "Operating account",
            BalanceCents = scenario.InitialBalanceCents,
        });

        sandbox.Accounts.Add(new Account
        {
            Id = Guid.NewGuid(),
            SandboxId = sandbox.Id,
            Code = "B",
            Label = "Reserve account",
            BalanceCents = scenario.InitialBalanceCents,
        });

        return sandbox;
    }

    private static SandboxSession ToSession(Sandbox sandbox) => new(
        sandbox,
        sandbox.Accounts.OrderBy(a => a.Code, StringComparer.Ordinal).ToList());
}
