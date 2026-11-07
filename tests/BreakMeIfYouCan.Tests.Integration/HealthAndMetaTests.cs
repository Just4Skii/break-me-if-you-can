using System.Net;
using System.Text.Json;
using BreakMeIfYouCan.Tests.Integration.Infrastructure;

namespace BreakMeIfYouCan.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class HealthAndMetaTests(PostgresFixture database)
{
    [Fact]
    public async Task LivenessAndReadiness_ReportHealthy()
    {
        await using var factory = new ApiFactory(database);
        using var client = factory.CreateClient();

        var live = await client.GetAsync(new Uri("/healthz", UriKind.Relative));
        var ready = await client.GetAsync(new Uri("/readyz", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.OK, ready.StatusCode);

        using var liveDocument = JsonDocument.Parse(await live.Content.ReadAsStringAsync());
        Assert.Equal("healthy", liveDocument.RootElement.GetProperty("status").GetString());
        Assert.Contains(
            liveDocument.RootElement.GetProperty("checks").EnumerateArray(),
            check => check.GetProperty("name").GetString() == "process");

        using var readyDocument = JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
        Assert.Contains(
            readyDocument.RootElement.GetProperty("checks").EnumerateArray(),
            check => check.GetProperty("name").GetString() == "postgres");
    }

    [Fact]
    public async Task Meta_ReportsTheCompiledCommitAndRuntime()
    {
        await using var factory = new ApiFactory(database);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/api/meta", UriKind.Relative));
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Break Me If You Can", document.RootElement.GetProperty("name").GetString());
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("commit").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("runtime").GetString()));
    }

    [Fact]
    public async Task SecurityHeaders_ArePresentOnEveryResponse()
    {
        await using var factory = new ApiFactory(database);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/healthz", UriKind.Relative));

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("frame-ancestors 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("strict-origin-when-cross-origin", response.Headers.GetValues("Referrer-Policy").Single());
    }
}
