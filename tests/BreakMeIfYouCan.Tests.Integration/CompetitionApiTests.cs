using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using BreakMeIfYouCan.Api.Competition;
using BreakMeIfYouCan.Tests.Integration.Infrastructure;

namespace BreakMeIfYouCan.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class CompetitionApiTests(PostgresFixture database)
{
    [Fact]
    public async Task Registration_Me_Login_Logout_AndDuplicateUsername()
    {
        await using var factory = new ApiFactory(database);
        using var client = factory.CreateClient();
        var username = "racer" + Guid.NewGuid().ToString("N")[..8];

        var registerResponse = await client.PostAsJsonAsync(
            "/api/players/register",
            new { username, password = "correct horse" },
            TestJson.Options);

        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var auth = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options);
        Assert.NotNull(auth);
        Assert.True(auth.Token.Length >= 20);
        Assert.Equal(username, auth.Player.Username);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        var me = await client.GetFromJsonAsync<PlayerView>("/api/players/me", TestJson.Options);
        Assert.NotNull(me);
        Assert.Equal(username, me.Username);

        var duplicate = await client.PostAsJsonAsync(
            "/api/players/register",
            new { username, password = "another password" },
            TestJson.Options);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        using var anonymous = factory.CreateClient();
        var wrong = await anonymous.PostAsJsonAsync(
            "/api/players/login",
            new { username, password = "not the password" },
            TestJson.Options);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

        var login = await anonymous.PostAsJsonAsync(
            "/api/players/login",
            new { username, password = "correct horse" },
            TestJson.Options);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginAuth = await login.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options);
        Assert.NotNull(loginAuth);
        Assert.Equal(username, loginAuth.Player.Username);

        var logout = await client.PostAsync("/api/players/logout", content: null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var afterLogout = await client.GetAsync("/api/players/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task RankedRun_IsVerifiedAndAppearsOnThePublicLeaderboard()
    {
        await using var factory = new ApiFactory(database);
        using var player = factory.CreateClient();
        var username = "engineer" + Guid.NewGuid().ToString("N")[..8];

        var register = await player.PostAsJsonAsync(
            "/api/players/register",
            new { username, password = "correct horse" },
            TestJson.Options);
        var auth = await register.Content.ReadFromJsonAsync<AuthResponse>(TestJson.Options);
        Assert.NotNull(auth);
        player.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);

        var session = await player.GetAsync("/api/arena/session");
        Assert.Equal(HttpStatusCode.OK, session.StatusCode);

        var runResponse = await player.PostAsync("/api/challenges/optimistic-8/run", content: null);
        Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);

        var run = await runResponse.Content.ReadFromJsonAsync<ChallengeRunView>(TestJson.Options);
        Assert.NotNull(run);
        Assert.True(run.Verified);
        Assert.Equal(8, run.Run.Accepted);
        Assert.Equal(0, run.Run.Rejected);
        Assert.True(run.Run.Integrity.Ok);
        Assert.True(run.Score > 0);
        Assert.True(run.Rank >= 1);
        Assert.Equal(run.Score, run.PersonalBest);

        using var publicClient = factory.CreateClient();
        var board = await publicClient.GetFromJsonAsync<LeaderboardView>(
            "/api/leaderboard?challenge=optimistic-8",
            TestJson.Options);

        Assert.NotNull(board);
        var entry = board.Entries.Single(e => e.Username == username);
        Assert.Equal(1, entry.Rank);
        Assert.Equal(run.Score, entry.Score);

        var performance = await player.GetFromJsonAsync<PerformanceView>(
            "/api/players/me/performance",
            TestJson.Options);

        Assert.NotNull(performance);
        Assert.Equal(username, performance.Player.Username);
        Assert.Contains(performance.Bests, best => best.ChallengeKey == "optimistic-8" && best.Score == run.Score);
        Assert.Contains(performance.Recent, recent => recent.ChallengeKey == "optimistic-8" && recent.Verified);
    }

    [Fact]
    public async Task ChallengeRun_RequiresAuthentication()
    {
        await using var factory = new ApiFactory(database);
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/challenges/optimistic-8/run", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Registration_ValidatesUsernameAndPassword()
    {
        await using var factory = new ApiFactory(database);
        using var client = factory.CreateClient();

        var badUsername = await client.PostAsJsonAsync(
            "/api/players/register",
            new { username = "ab", password = "correct horse" },
            TestJson.Options);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badUsername.StatusCode);

        var badPassword = await client.PostAsJsonAsync(
            "/api/players/register",
            new { username = "validname", password = "short" },
            TestJson.Options);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, badPassword.StatusCode);
    }
}
