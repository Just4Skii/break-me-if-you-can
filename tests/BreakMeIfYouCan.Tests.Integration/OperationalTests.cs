using System.Net;
using BreakMeIfYouCan.Tests.Integration.Infrastructure;

namespace BreakMeIfYouCan.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class OperationalTests(PostgresFixture database)
{
    [Fact]
    public async Task KillSwitch_DisablesTheArenaButKeepsHealthAndProofAvailable()
    {
        await using var factory = new ApiFactory(database, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Runtime:KillSwitch"] = "true",
            ["Proof:Repository"] = string.Empty,
            ["Proof:CacheSeconds"] = "0",
        });
        using var client = factory.CreateClient();

        var arena = await client.GetAsync(new Uri("/api/arena/session", UriKind.Relative));
        var health = await client.GetAsync(new Uri("/healthz", UriKind.Relative));
        var proof = await client.GetAsync(new Uri("/api/proof", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, arena.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal(HttpStatusCode.OK, proof.StatusCode);
        Assert.Equal("300", arena.Headers.GetValues("Retry-After").Single());
    }

    [Fact]
    public async Task KillSwitchIsOffByDefault()
    {
        await using var factory = new ApiFactory(database);
        using var client = factory.CreateClient();

        var arena = await client.GetAsync(new Uri("/api/arena/session", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, arena.StatusCode);
    }
}
