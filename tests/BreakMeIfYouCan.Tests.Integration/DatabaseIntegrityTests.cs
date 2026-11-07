using BreakMeIfYouCan.Api.Arena;
using BreakMeIfYouCan.Api.Data;
using BreakMeIfYouCan.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BreakMeIfYouCan.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class DatabaseIntegrityTests(PostgresFixture database)
{
    [Fact]
    public async Task XminConcurrencyToken_DetectsAConcurrentUpdate()
    {
        await using var factory = new ApiFactory(database);
        using var client = new ArenaClient(factory);
        var session = await client.GetSessionAsync();
        var accountId = session.Accounts[0].Id;

        await using var first = DatabaseTestHelpers.CreateContext(database.ConnectionString);
        await using var second = DatabaseTestHelpers.CreateContext(database.ConnectionString);

        var loadedFirst = await first.Accounts.SingleAsync(a => a.Id == accountId);
        var loadedSecond = await second.Accounts.SingleAsync(a => a.Id == accountId);

        loadedFirst.BalanceCents += 1;
        await first.SaveChangesAsync();

        loadedSecond.BalanceCents += 1;
        var exception = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        Assert.NotNull(exception);
    }

    [Fact]
    public async Task CheckConstraint_PreventsNegativeBalanceEvenWithoutApplicationCode()
    {
        await using var factory = new ApiFactory(database);
        using var client = new ArenaClient(factory);
        var session = await client.GetSessionAsync();
        var accountId = session.Accounts[0].Id;

        var exception = await Assert.ThrowsAsync<PostgresException>(() => DatabaseTestHelpers.ExecuteAsync(
            database.ConnectionString,
            "UPDATE arena.accounts SET balance_cents = -1 WHERE id = @id",
            ("id", accountId)));

        Assert.Equal(PostgresErrorCodes.CheckViolation, exception.SqlState);
        Assert.Contains("ck_accounts_balance_non_negative", exception.ConstraintName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LedgerEntries_AreAppendOnly()
    {
        await using var factory = new ApiFactory(database);
        using var client = new ArenaClient(factory);
        var session = await client.GetSessionAsync();

        var transfer = await client.TransferAsync(
            session.Accounts.Single(a => a.Code == "A").Id,
            session.Accounts.Single(a => a.Code == "B").Id,
            1_000,
            "optimistic",
            "append-only-" + Guid.NewGuid().ToString("N"));
        transfer.EnsureSuccessStatusCode();

        await using var context = DatabaseTestHelpers.CreateContext(database.ConnectionString);
        var entryId = await context.LedgerEntries.Select(e => e.Id).FirstAsync();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => DatabaseTestHelpers.ExecuteAsync(
            database.ConnectionString,
            "UPDATE arena.ledger_entries SET amount_cents = 1 WHERE id = @id",
            ("id", entryId)));

        Assert.Equal(PostgresErrorCodes.RaiseException, exception.SqlState);
        Assert.Contains("append-only", exception.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UnbalancedTransfer_IsRejectedAtCommitTime()
    {
        await using var factory = new ApiFactory(database);
        using var client = new ArenaClient(factory);
        var session = await client.GetSessionAsync();
        var accountA = session.Accounts.Single(a => a.Code == "A");
        var accountB = session.Accounts.Single(a => a.Code == "B");

        await using var context = DatabaseTestHelpers.CreateContext(database.ConnectionString);
        var now = DateTimeOffset.UtcNow;
        var transfer = new Transfer
        {
            Id = Guid.NewGuid(),
            SandboxId = session.SandboxId,
            FromAccountId = accountA.Id,
            ToAccountId = accountB.Id,
            AmountCents = 1_000,
            Strategy = TransferStrategies.Optimistic,
            Status = TransferStatus.Completed,
            Attempts = 1,
            CreatedAt = now,
            CompletedAt = now,
        };

        context.Transfers.Add(transfer);
        context.LedgerEntries.Add(new LedgerEntry
        {
            Id = Guid.NewGuid(),
            TransferId = transfer.Id,
            AccountId = accountA.Id,
            Direction = LedgerDirections.Debit,
            AmountCents = 1_000,
            CreatedAt = now,
        });

        var exception = await Assert.ThrowsAnyAsync<Exception>(() => context.SaveChangesAsync());

        Assert.True(
            ContainsRaiseException(exception),
            "Expected the deferred balance trigger to reject the single-entry transfer, but got " +
            exception.GetType().Name + ": " + exception.Message);
    }

    private static bool ContainsRaiseException(Exception? exception)
    {
        while (exception is not null)
        {
            if (exception is PostgresException { SqlState: PostgresErrorCodes.RaiseException })
            {
                return true;
            }

            exception = exception.InnerException;
        }

        return false;
    }

    [Fact]
    public async Task DuplicateIdempotencyKey_IsRejectedByTheDatabase()
    {
        await using var factory = new ApiFactory(database);
        using var client = new ArenaClient(factory);
        var session = await client.GetSessionAsync();
        var accountA = session.Accounts.Single(a => a.Code == "A");
        var accountB = session.Accounts.Single(a => a.Code == "B");
        var key = "db-unique-" + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        await using var context = DatabaseTestHelpers.CreateContext(database.ConnectionString);

        context.Transfers.Add(new Transfer
        {
            Id = Guid.NewGuid(),
            SandboxId = session.SandboxId,
            FromAccountId = accountA.Id,
            ToAccountId = accountB.Id,
            AmountCents = 100,
            Strategy = TransferStrategies.Optimistic,
            Status = TransferStatus.Rejected,
            IdempotencyKey = key,
            Attempts = 1,
            CreatedAt = now,
            FailureReason = "test",
        });
        await context.SaveChangesAsync();

        context.Transfers.Add(new Transfer
        {
            Id = Guid.NewGuid(),
            SandboxId = session.SandboxId,
            FromAccountId = accountA.Id,
            ToAccountId = accountB.Id,
            AmountCents = 100,
            Strategy = TransferStrategies.Optimistic,
            Status = TransferStatus.Rejected,
            IdempotencyKey = key,
            Attempts = 1,
            CreatedAt = now,
            FailureReason = "test",
        });

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        Assert.True(exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation });
    }

    [Fact]
    public async Task DatabaseIsNotSharedBetweenSandboxes()
    {
        await using var factory = new ApiFactory(database);
        using var alice = new ArenaClient(factory);
        using var bob = new ArenaClient(factory);

        var aliceSession = await alice.GetSessionAsync();
        var bobSession = await bob.GetSessionAsync();

        await alice.RunAsync(new ArenaRunRequest
        {
            Strategy = "pessimistic",
            Requests = 5,
            Concurrency = 5,
            AmountCents = 1_000,
            Pattern = "a_to_b",
        });

        var bobAfter = await bob.GetSessionAsync();
        Assert.All(bobAfter.Accounts, account => Assert.Equal(100_000, account.BalanceCents));

        var aliceLedger = await alice.GetLedgerAsync();
        var bobLedger = await bob.GetLedgerAsync();
        Assert.Equal(5, aliceLedger.Integrity.CompletedTransfers);
        Assert.Equal(0, bobLedger.Integrity.CompletedTransfers);
        Assert.NotEqual(aliceSession.SandboxId, bobSession.SandboxId);
    }
}