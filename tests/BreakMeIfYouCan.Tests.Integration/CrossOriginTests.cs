using System.Net;
using System.Net.Http.Json;
using BreakMeIfYouCan.Api.Arena;
using BreakMeIfYouCan.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BreakMeIfYouCan.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class CrossOriginTests(PostgresFixture database)
{
    private const string PagesOrigin = "https://just4skii.github.io";

    [Fact]
    public async Task HeaderToken_KeepsTheSandboxWithoutCookies_AndCorsAllowsThePagesOrigin()
    {
        await using var factory = new ApiFactory(database);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Origin", PagesOrigin);

        using var sessionResponse = await client.GetAsync("/api/arena/session");
        Assert.Equal(HttpStatusCode.OK, sessionResponse.StatusCode);
        Assert.Equal(PagesOrigin, sessionResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());

        var session = await sessionResponse.Content.ReadFromJsonAsync<SandboxView>(TestJson.Options);
        Assert.NotNull(session);
        Assert.False(string.IsNullOrWhiteSpace(session.SandboxToken));

        using var runRequest = new HttpRequestMessage(HttpMethod.Post, "/api/arena/runs");
        runRequest.Headers.Add("X-Sandbox-Token", session.SandboxToken);
        runRequest.Content = JsonContent.Create(
            new ArenaRunRequest
            {
                Strategy = "pessimistic",
                Requests = 5,
                Concurrency = 5,
                AmountCents = 1_000,
                Pattern = "a_to_b",
            },
            options: TestJson.Options);

        using var runResponse = await client.SendAsync(runRequest);
        Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);

        var run = await runResponse.Content.ReadFromJsonAsync<ArenaRunResult>(TestJson.Options);
        Assert.NotNull(run);
        Assert.Equal(5, run.Accepted);
        Assert.Equal(0, run.Rejected);
        Assert.True(run.Integrity.Ok);

        // No cookie ever travelled: the header alone must resolve the same sandbox.
        using var again = new HttpRequestMessage(HttpMethod.Get, "/api/arena/session");
        again.Headers.Add("X-Sandbox-Token", session.SandboxToken);
        using var againResponse = await client.SendAsync(again);
        Assert.Equal(HttpStatusCode.OK, againResponse.StatusCode);

        var same = await againResponse.Content.ReadFromJsonAsync<SandboxView>(TestJson.Options);
        Assert.NotNull(same);
        Assert.Equal(session.SandboxId, same.SandboxId);
        Assert.Equal(95_000, same.Accounts.Single(a => a.Code == "A").BalanceCents);
        Assert.Equal(105_000, same.Accounts.Single(a => a.Code == "B").BalanceCents);
    }

    [Fact]
    public async Task Preflight_AllowsTheSandboxHeaderFromPages()
    {
        await using var factory = new ApiFactory(database);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/arena/runs");
        request.Headers.Add("Origin", PagesOrigin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "content-type,x-sandbox-token");

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(PagesOrigin, response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains(
            "x-sandbox-token",
            response.Headers.GetValues("Access-Control-Allow-Headers").Single(),
            StringComparison.OrdinalIgnoreCase);
    }
}
