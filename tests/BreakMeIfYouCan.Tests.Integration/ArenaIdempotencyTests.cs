using System.Net;
using BreakMeIfYouCan.Api.Data;
using BreakMeIfYouCan.Tests.Integration.Infrastructure;

namespace BreakMeIfYouCan.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class ArenaIdempotencyTests(PostgresFixture database)
{
    [Fact]
    public async Task IdempotentReplay_ReturnsTheSameTransferWithoutDoubleDebit()
    {
        await using var factory = new ApiFactory(database);
        using var client = new ArenaClient(factory);

        var session = await client.GetSessionAsync();
        var accountA = session.Accounts.Single(a => a.Code == "A");
        var accountB = session.Accounts.Single(a => a.Code == "B");
        var key = "idem-" + Guid.NewGuid().ToString("N");

        var first = await client.TransferAsync(accountA.Id, accountB.Id, 2_500, "optimistic", key);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await ArenaClient.ReadTransferAsync(first);
        Assert.NotNull(firstBody);

        var second = await client.TransferAsync(accountA.Id, accountB.Id, 2_500, "optimistic", key);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await ArenaClient.ReadTransferAsync(second);
        Assert.NotNull(secondBody);

        Assert.True(secondBody.Replayed);
        Assert.Equal(firstBody.TransferId, secondBody.TransferId);
        Assert.Equal(TransferStatus.Completed, secondBody.Status);

        var ledger = await client.GetLedgerAsync();
        Assert.Equal(1, ledger.Integrity.CompletedTransfers);
        Assert.Equal(2, ledger.Integrity.LedgerEntries);
        Assert.True(ledger.Integrity.Ok);

        var after = await client.GetSessionAsync();
        Assert.Equal(97_500, after.Accounts.Single(a => a.Code == "A").BalanceCents);
        Assert.Equal(102_500, after.Accounts.Single(a => a.Code == "B").BalanceCents);
    }

    [Fact]
    public async Task ConcurrentRequestsWithTheSameKey_CreateExactlyOneTransfer()
    {
        await using var factory = new ApiFactory(database);
        using var client = new ArenaClient(factory);

        var session = await client.GetSessionAsync();
        var accountA = session.Accounts.Single(a => a.Code == "A");
        var accountB = session.Accounts.Single(a => a.Code == "B");
        var key = "race-" + Guid.NewGuid().ToString("N");

        var responses = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => client.TransferAsync(accountA.Id, accountB.Id, 1_000, "optimistic", key)));

        Assert.All(responses, r => Assert.True(
            r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            "Each duplicate request should complete or replay, not fail."));

        var bodies = await Task.WhenAll(responses.Select(ArenaClient.ReadTransferAsync));
        Assert.Single(bodies.Select(b => b!.TransferId).Distinct());

        var ledger = await client.GetLedgerAsync();
        Assert.Equal(1, ledger.Integrity.CompletedTransfers);
        Assert.Equal(2, ledger.Integrity.LedgerEntries);

        var after = await client.GetSessionAsync();
        Assert.Equal(99_000, after.Accounts.Single(a => a.Code == "A").BalanceCents);
        Assert.Equal(101_000, after.Accounts.Single(a => a.Code == "B").BalanceCents);
    }

    [Fact]
    public async Task ReplayOfARejectedTransfer_IsStillRejectedAndIdempotent()
    {
        await using var factory = new ApiFactory(database);
        using var client = new ArenaClient(factory);

        var session = await client.GetSessionAsync();
        var accountA = session.Accounts.Single(a => a.Code == "A");
        var accountB = session.Accounts.Single(a => a.Code == "B");

        // Drain account A completely, one maximum-size transfer at a time.
        for (var i = 0; i < 10; i++)
        {
            var drained = await client.TransferAsync(accountA.Id, accountB.Id, 10_000, "optimistic", "drain-" + i);
            Assert.Equal(HttpStatusCode.Created, drained.StatusCode);
        }

        var key = "overdraft-" + Guid.NewGuid().ToString("N");
        var rejected = await client.TransferAsync(accountA.Id, accountB.Id, 10_000, "optimistic", key);
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        var rejectedBody = await ArenaClient.ReadTransferAsync(rejected);
        Assert.Equal("insufficient_funds", rejectedBody!.FailureReason);

        var replay = await client.TransferAsync(accountA.Id, accountB.Id, 10_000, "optimistic", key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayBody = await ArenaClient.ReadTransferAsync(replay);
        Assert.True(replayBody!.Replayed);
        Assert.Equal(TransferStatus.Rejected, replayBody.Status);

        var ledger = await client.GetLedgerAsync();
        Assert.Equal(0, ledger.Integrity.NetCents);
        Assert.Equal(10, ledger.Integrity.CompletedTransfers);
        Assert.Equal(20, ledger.Integrity.LedgerEntries);
        Assert.Equal(0, ledger.Integrity.NegativeAccounts);

        var after = await client.GetSessionAsync();
        Assert.Equal(0, after.Accounts.Single(a => a.Code == "A").BalanceCents);
        Assert.Equal(200_000, after.Accounts.Single(a => a.Code == "B").BalanceCents);
    }
}