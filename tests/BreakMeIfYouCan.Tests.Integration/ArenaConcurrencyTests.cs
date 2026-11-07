using BreakMeIfYouCan.Api.Arena;
using BreakMeIfYouCan.Tests.Integration.Infrastructure;

namespace BreakMeIfYouCan.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class ArenaConcurrencyTests(PostgresFixture database)
{
    [Fact]
    public async Task OptimisticRun_FiftyConcurrentTransfers_AllSucceedWithBalancedLedger()
    {
        await using var factory = new ApiFactory(database);
        using var client = new ArenaClient(factory);

        var session = await client.GetSessionAsync();
        var accountA = session.Accounts.Single(a => a.Code == "A");
        var accountB = session.Accounts.Single(a => a.Code == "B");

        var result = await client.RunAsync(new ArenaRunRequest
        {
            Strategy = "optimistic",
            Requests = 50,
            Concurrency = 8,
            AmountCents = 1_000,
            Pattern = "a_to_b",
        });

        Assert.Equal(50, result.Requested);
        Assert.Equal(50, result.Accepted);
        Assert.Equal(0, result.Rejected);
        Assert.DoesNotContain(result.Receipts, r => r.Status == "error");

        // The whole point of the optimistic experiment: real xmin conflicts occurred and
        // were retried, and the final state still reflects every accepted transfer.
        Assert.True(result.Conflicts > 0, "Expected at least one optimistic concurrency conflict.");
        Assert.True(result.TotalRetries > 0, "Expected at least one retry.");

        Assert.True(result.Integrity.Ok);
        Assert.True(result.Integrity.Balanced);
        Assert.Equal(0, result.Integrity.NetCents);
        Assert.Equal(0, result.Integrity.NegativeAccounts);
        Assert.Equal(0, result.Integrity.MalformedTransfers);
        Assert.Equal(50, result.Integrity.CompletedTransfers);
        Assert.Equal(100, result.Integrity.LedgerEntries);

        Assert.Equal(200_000, result.FinalBalances.Sum(a => a.BalanceCents));
        Assert.Equal(50_000, result.FinalBalances.Single(a => a.Code == "A").BalanceCents);
        Assert.Equal(150_000, result.FinalBalances.Single(a => a.Code == "B").BalanceCents);
    }

    [Fact]
    public async Task PessimisticRun_FiftyConcurrentTransfers_SerialiseWithoutConflicts()
    {
        await using var factory = new ApiFactory(database);
        using var client = new ArenaClient(factory);

        var session = await client.GetSessionAsync();
        Assert.Equal(2, session.Accounts.Count);

        var result = await client.RunAsync(new ArenaRunRequest
        {
            Strategy = "pessimistic",
            Requests = 50,
            Concurrency = 8,
            AmountCents = 1_000,
            Pattern = "alternating",
        });

        Assert.Equal(50, result.Accepted);
        Assert.Equal(0, result.Rejected);

        // Pessimistic locking removes the collision entirely: writers queue on the row.
        Assert.Equal(0, result.Conflicts);
        Assert.Equal(0, result.TotalRetries);

        Assert.True(result.Integrity.Ok);
        Assert.Equal(0, result.Integrity.NetCents);
        Assert.Equal(200_000, result.FinalBalances.Sum(a => a.BalanceCents));
        Assert.Equal(100_000, result.FinalBalances.Single(a => a.Code == "A").BalanceCents);
        Assert.Equal(100_000, result.FinalBalances.Single(a => a.Code == "B").BalanceCents);
    }

    [Fact]
    public async Task FiftyParallelHttpTransferRequests_KeepEveryInvariant()
    {
        await using var factory = new ApiFactory(database);
        using var client = new ArenaClient(factory);

        var session = await client.GetSessionAsync();
        var accountA = session.Accounts.Single(a => a.Code == "A");
        var accountB = session.Accounts.Single(a => a.Code == "B");

        var requests = Enumerable.Range(0, 50)
            .Select(_ => client.TransferAsync(
                accountA.Id,
                accountB.Id,
                1_000,
                "optimistic",
                "http-" + Guid.NewGuid().ToString("N")))
            .ToArray();

        var responses = await Task.WhenAll(requests);

        var statuses = responses.Select(r => (int)r.StatusCode).ToArray();
        Assert.All(statuses, status => Assert.True(
            status is 200 or 201 or 409,
            "Unexpected status " + status + " from a concurrent transfer."));

        var accepted = statuses.Count(s => s is 200 or 201);
        var rejected = statuses.Count(s => s == 409);
        Assert.Equal(50, accepted + rejected);
        Assert.True(accepted > 0, "No transfer completed at all.");

        var ledger = await client.GetLedgerAsync();
        Assert.True(ledger.Integrity.Ok);
        Assert.Equal(0, ledger.Integrity.NetCents);
        Assert.Equal(0, ledger.Integrity.NegativeAccounts);
        Assert.Equal(0, ledger.Integrity.MalformedTransfers);
        Assert.Equal(accepted, ledger.Integrity.CompletedTransfers);

        var after = await client.GetSessionAsync();
        Assert.Equal(200_000, after.Accounts.Sum(a => a.BalanceCents));
        Assert.All(after.Accounts, account => Assert.True(account.BalanceCents >= 0));

        // Every completed transfer moved exactly the amount that the balances reflect.
        Assert.Equal(100_000 - (accepted * 1_000), after.Accounts.Single(a => a.Code == "A").BalanceCents);
        Assert.Equal(100_000 + (accepted * 1_000), after.Accounts.Single(a => a.Code == "B").BalanceCents);
    }

    [Fact]
    public async Task OptimisticStrategy_RecordsRetryCountOnTheTransfer()
    {
        await using var factory = new ApiFactory(database);
        using var client = new ArenaClient(factory);

        var session = await client.GetSessionAsync();
        var accountA = session.Accounts.Single(a => a.Code == "A");
        var accountB = session.Accounts.Single(a => a.Code == "B");

        var run = await client.RunAsync(new ArenaRunRequest
        {
            Strategy = "optimistic",
            Requests = 20,
            Concurrency = 8,
            AmountCents = 500,
            Pattern = "a_to_b",
        });

        Assert.True(run.TotalRetries > 0);

        var ledger = await client.GetLedgerAsync();
        Assert.Contains(ledger.Transfers, t => t.Attempts > 1 || t.Conflicts > 0);
        Assert.All(ledger.Transfers, t => Assert.InRange(t.Attempts, 1, 10));
        Assert.Equal(20, run.Accepted);
        Assert.True(ledger.Integrity.Ok);
    }
}