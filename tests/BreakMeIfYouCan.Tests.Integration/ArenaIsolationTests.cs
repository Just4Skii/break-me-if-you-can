using System.Net;
using BreakMeIfYouCan.Api.Arena;
using BreakMeIfYouCan.Tests.Integration.Infrastructure;

namespace BreakMeIfYouCan.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class ArenaIsolationTests(PostgresFixture database)
{
    [Fact]
    public async Task TwoVisitors_ReceiveDifferentSandboxesAndAccounts()
    {
        await using var factory = new ApiFactory(database);
        using var alice = new ArenaClient(factory);
        using var bob = new ArenaClient(factory);

        var aliceSession = await alice.GetSessionAsync();
        var bobSession = await bob.GetSessionAsync();

        Assert.NotEqual(aliceSession.SandboxId, bobSession.SandboxId);
        Assert.Equal(2, aliceSession.Accounts.Count);
        Assert.Equal(2, bobSession.Accounts.Count);
        Assert.Empty(aliceSession.Accounts.Select(a => a.Id).Intersect(bobSession.Accounts.Select(a => a.Id)));
        Assert.All(aliceSession.Accounts, account => Assert.Equal(100_000, account.BalanceCents));
        Assert.All(bobSession.Accounts, account => Assert.Equal(100_000, account.BalanceCents));
    }

    [Fact]
    public async Task VisitorCannotTransferFromAnotherVisitorsAccount()
    {
        await using var factory = new ApiFactory(database);
        using var alice = new ArenaClient(factory);
        using var bob = new ArenaClient(factory);

        var aliceSession = await alice.GetSessionAsync();
        var bobSession = await bob.GetSessionAsync();

        var response = await bob.TransferAsync(
            aliceSession.Accounts.Single(a => a.Code == "A").Id,
            bobSession.Accounts.Single(a => a.Code == "A").Id,
            1_000);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var aliceAfter = await alice.GetSessionAsync();
        Assert.All(aliceAfter.Accounts, account => Assert.Equal(100_000, account.BalanceCents));
    }

    [Fact]
    public async Task Reset_StartsAFreshSandboxAndKeepsTheArchivedLedger()
    {
        await using var factory = new ApiFactory(database);
        using var client = new ArenaClient(factory);

        var first = await client.GetSessionAsync();
        var accountA = first.Accounts.Single(a => a.Code == "A");
        var accountB = first.Accounts.Single(a => a.Code == "B");

        var run = await client.RunAsync(new ArenaRunRequest
        {
            Strategy = "pessimistic",
            Requests = 5,
            Concurrency = 5,
            AmountCents = 1_000,
            Pattern = "a_to_b",
        });
        Assert.Equal(5, run.Accepted);

        var second = await client.ResetAsync();

        Assert.NotEqual(first.SandboxId, second.SandboxId);
        Assert.All(second.Accounts, account => Assert.Equal(100_000, account.BalanceCents));

        // The archived sandbox keeps its ledger: reset must not rewrite history.
        var archivedEntries = await DatabaseTestHelpers.ScalarLongAsync(
            database.ConnectionString,
            "SELECT COUNT(*) FROM arena.ledger_entries e JOIN arena.transfers t ON t.id = e.transfer_id WHERE t.sandbox_id = @sandbox",
            ("sandbox", first.SandboxId));
        Assert.Equal(10, archivedEntries);

        var archivedTransfers = await DatabaseTestHelpers.ScalarLongAsync(
            database.ConnectionString,
            "SELECT COUNT(*) FROM arena.transfers WHERE sandbox_id = @sandbox",
            ("sandbox", first.SandboxId));
        Assert.Equal(5, archivedTransfers);

        _ = accountA;
        _ = accountB;
    }
}
