using System.Data;
using System.Diagnostics;
using BreakMeIfYouCan.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BreakMeIfYouCan.Api.Arena;

public sealed record TransferCommand
{
    public required Guid SandboxId { get; init; }

    public required Guid FromAccountId { get; init; }

    public required Guid ToAccountId { get; init; }

    public required long AmountCents { get; init; }

    public required string Strategy { get; init; }

    public string? IdempotencyKey { get; init; }

    public long MaxAmountCents { get; init; } = ArenaScenarioKeys.StarterScenario.MaxAmountCents;
}

public sealed record TransferOutcome
{
    public required Guid TransferId { get; init; }

    public required string Status { get; init; }

    public required bool Replayed { get; init; }

    public required string Strategy { get; init; }

    public required int Attempts { get; init; }

    public required int Conflicts { get; init; }

    public required double DurationMs { get; init; }

    public string? FailureReason { get; init; }

    public required IReadOnlyList<AccountView> Balances { get; init; }
}

/// <summary>
/// Executes one transfer under either concurrency strategy.
///
/// Optimistic: read the account rows, let PostgreSQL's xmin detect a concurrent commit,
/// then retry with bounded exponential backoff and jitter.
/// Pessimistic: lock the sandbox accounts with SELECT ... FOR UPDATE in a deterministic
/// order, so writers queue instead of colliding.
///
/// Both paths write a debit and a credit in the same transaction as the balance update,
/// and the database verifies the pair at commit time.
/// </summary>
public sealed class TransferExecutor(
    IServiceScopeFactory scopeFactory,
    ArenaConcurrencyGate gate,
    IOptions<ArenaOptions> options,
    ILogger<TransferExecutor> logger)
{
    private readonly ArenaOptions _options = options.Value;

    public async Task<TransferOutcome> ExecuteAsync(TransferCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var stopwatch = Stopwatch.StartNew();
        var strategy = TransferStrategies.Normalize(command.Strategy);

        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var existing = await FindReplayAsync(command, cancellationToken);
            if (existing is not null)
            {
                return await ToReplayOutcomeAsync(existing, command, stopwatch, cancellationToken);
            }
        }

        var maxAttempts = strategy == TransferStrategies.Optimistic
            ? Math.Max(1, _options.MaxRetryAttempts)
            : 1;

        var conflicts = 0;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await AttemptAsync(command, strategy, attempt, conflicts, stopwatch, cancellationToken);
            }
            catch (DbUpdateConcurrencyException) when (strategy == TransferStrategies.Optimistic)
            {
                conflicts++;

                if (attempt >= maxAttempts)
                {
                    logger.LogWarning(
                        "Optimistic transfer exhausted {Attempts} attempts for sandbox {SandboxId}.",
                        maxAttempts,
                        command.SandboxId);

                    return await PersistRejectedAsync(
                        command,
                        strategy,
                        attempt,
                        conflicts,
                        "concurrency_conflict",
                        stopwatch,
                        cancellationToken);
                }

                await Task.Delay(Backoff(attempt), cancellationToken);
            }
            catch (DbUpdateException exception) when (
                !string.IsNullOrWhiteSpace(command.IdempotencyKey) && PostgresErrors.IsUniqueViolation(exception))
            {
                var replay = await FindReplayAsync(command, cancellationToken);
                if (replay is not null)
                {
                    return await ToReplayOutcomeAsync(replay, command, stopwatch, cancellationToken);
                }

                throw;
            }
        }

        return await PersistRejectedAsync(
            command,
            strategy,
            maxAttempts,
            conflicts,
            "concurrency_conflict",
            stopwatch,
            cancellationToken);
    }

    private async Task<TransferOutcome> AttemptAsync(
        TransferCommand command,
        string strategy,
        int attempt,
        int conflicts,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        using var lease = await gate.AcquireTransactionAsync(cancellationToken);

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArenaDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);

        try
        {
            List<Account> accounts;

            if (strategy == TransferStrategies.Pessimistic)
            {
                // Lock every account in the sandbox in a deterministic order. Locking both
                // rows (not just the two involved) makes the order independent of the
                // transfer direction, which is what removes the deadlock window.
                accounts = await db.Accounts
                    .FromSqlInterpolated($@"SELECT a.*, a.xmin AS xmin FROM arena.accounts a WHERE a.sandbox_id = {command.SandboxId} ORDER BY a.id FOR UPDATE")
                    .ToListAsync(cancellationToken);
            }
            else
            {
                accounts = await db.Accounts
                    .Where(a => a.SandboxId == command.SandboxId)
                    .OrderBy(a => a.Id)
                    .ToListAsync(cancellationToken);
            }

            Validate(command, accounts);

            var from = accounts.Single(a => a.Id == command.FromAccountId);
            var to = accounts.Single(a => a.Id == command.ToAccountId);

            if (from.BalanceCents < command.AmountCents)
            {
                var rejected = NewTransfer(command, strategy, attempt, conflicts, TransferStatus.Rejected, "insufficient_funds");
                rejected.DurationMicroseconds = ElapsedMicroseconds(stopwatch);
                db.Transfers.Add(rejected);
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return Outcome(rejected, from, to, stopwatch, replayed: false);
            }

            from.BalanceCents -= command.AmountCents;
            to.BalanceCents += command.AmountCents;

            var transfer = NewTransfer(command, strategy, attempt, conflicts, TransferStatus.Completed, null);
            transfer.DurationMicroseconds = ElapsedMicroseconds(stopwatch);

            db.Transfers.Add(transfer);
            db.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(),
                TransferId = transfer.Id,
                AccountId = from.Id,
                Direction = LedgerDirections.Debit,
                AmountCents = command.AmountCents,
                CreatedAt = transfer.CreatedAt,
            });
            db.LedgerEntries.Add(new LedgerEntry
            {
                Id = Guid.NewGuid(),
                TransferId = transfer.Id,
                AccountId = to.Id,
                Direction = LedgerDirections.Credit,
                AmountCents = command.AmountCents,
                CreatedAt = transfer.CreatedAt,
            });

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            return Outcome(transfer, from, to, stopwatch, replayed: false);
        }
        catch
        {
            await RollbackQuietlyAsync(transaction);
            throw;
        }
    }

    private async Task<TransferOutcome> PersistRejectedAsync(
        TransferCommand command,
        string strategy,
        int attempts,
        int conflicts,
        string reason,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArenaDbContext>();

        var rejected = NewTransfer(command, strategy, attempts, conflicts, TransferStatus.Rejected, reason);
        rejected.DurationMicroseconds = ElapsedMicroseconds(stopwatch);
        db.Transfers.Add(rejected);
        await db.SaveChangesAsync(cancellationToken);

        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(a => a.SandboxId == command.SandboxId)
            .OrderBy(a => a.Code)
            .ToListAsync(cancellationToken);

        return Outcome(rejected, accounts, stopwatch, replayed: false);
    }

    private async Task<Transfer?> FindReplayAsync(TransferCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            return null;
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArenaDbContext>();
        return await db.Transfers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                t => t.SandboxId == command.SandboxId && t.IdempotencyKey == command.IdempotencyKey,
                cancellationToken);
    }

    private async Task<TransferOutcome> ToReplayOutcomeAsync(
        Transfer existing,
        TransferCommand command,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArenaDbContext>();
        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(a => a.SandboxId == command.SandboxId)
            .OrderBy(a => a.Code)
            .ToListAsync(cancellationToken);

        return new TransferOutcome
        {
            TransferId = existing.Id,
            Status = existing.Status,
            Replayed = true,
            Strategy = existing.Strategy,
            Attempts = existing.Attempts,
            Conflicts = existing.Conflicts,
            DurationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
            FailureReason = existing.FailureReason,
            Balances = accounts.Select(a => new AccountView
            {
                Id = a.Id,
                Code = a.Code,
                Label = a.Label,
                BalanceCents = a.BalanceCents,
            }).ToList(),
        };
    }

    private static TransferOutcome Outcome(Transfer transfer, Account from, Account to, Stopwatch stopwatch, bool replayed) =>
        Outcome(transfer, [from, to], stopwatch, replayed);

    private static TransferOutcome Outcome(Transfer transfer, IReadOnlyList<Account> accounts, Stopwatch stopwatch, bool replayed) =>
        new()
        {
            TransferId = transfer.Id,
            Status = transfer.Status,
            Replayed = replayed,
            Strategy = transfer.Strategy,
            Attempts = transfer.Attempts,
            Conflicts = transfer.Conflicts,
            DurationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
            FailureReason = transfer.FailureReason,
            Balances = accounts
                .OrderBy(a => a.Code, StringComparer.Ordinal)
                .Select(a => new AccountView
                {
                    Id = a.Id,
                    Code = a.Code,
                    Label = a.Label,
                    BalanceCents = a.BalanceCents,
                })
                .ToList(),
        };

    private static void Validate(TransferCommand command, IReadOnlyList<Account> accounts)
    {
        if (command.AmountCents <= 0)
        {
            throw new ArenaException("The transfer amount must be positive.", StatusCodes.Status422UnprocessableEntity);
        }

        if (command.AmountCents > command.MaxAmountCents)
        {
            throw new ArenaException(
                "The transfer amount exceeds the challenge limit of " + command.MaxAmountCents + " cents.",
                StatusCodes.Status422UnprocessableEntity);
        }

        if (command.FromAccountId == command.ToAccountId)
        {
            throw new ArenaException("An account cannot transfer to itself.", StatusCodes.Status422UnprocessableEntity);
        }

        if (accounts.Count < 2)
        {
            throw new ArenaException("The sandbox no longer has its two accounts.", StatusCodes.Status409Conflict);
        }

        if (accounts.All(a => a.Id != command.FromAccountId) || accounts.All(a => a.Id != command.ToAccountId))
        {
            throw new ArenaException("Both accounts must belong to your sandbox.", StatusCodes.Status403Forbidden);
        }
    }

    private static Transfer NewTransfer(
        TransferCommand command,
        string strategy,
        int attempts,
        int conflicts,
        string status,
        string? failureReason)
    {
        var now = DateTimeOffset.UtcNow;
        return new Transfer
        {
            Id = Guid.NewGuid(),
            SandboxId = command.SandboxId,
            FromAccountId = command.FromAccountId,
            ToAccountId = command.ToAccountId,
            AmountCents = command.AmountCents,
            Strategy = strategy,
            Status = status,
            IdempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey) ? null : command.IdempotencyKey,
            Attempts = attempts,
            Conflicts = conflicts,
            FailureReason = failureReason,
            CreatedAt = now,
            CompletedAt = status == TransferStatus.Completed ? now : null,
        };
    }

    private static int ElapsedMicroseconds(Stopwatch stopwatch) =>
        (int)Math.Min(int.MaxValue, stopwatch.Elapsed.TotalMicroseconds);

    private static async Task RollbackQuietlyAsync(Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
            // The connection is already gone; disposal will finish the cleanup.
        }
    }

    private TimeSpan Backoff(int attempt)
    {
        var exponential = Math.Min(
            _options.BackoffMaxMilliseconds,
            _options.BackoffBaseMilliseconds * Math.Pow(2, attempt - 1));
        var jitter = 0.5 + (Random.Shared.NextDouble() * 0.5);
        return TimeSpan.FromMilliseconds(exponential * jitter);
    }
}