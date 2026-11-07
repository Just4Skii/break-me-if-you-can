using BreakMeIfYouCan.Api.Infrastructure;

namespace BreakMeIfYouCan.Api.Competition;

public static class CompetitionEndpoints
{
    public static IEndpointRouteBuilder MapCompetitionEndpoints(this IEndpointRouteBuilder app)
    {
        var players = app.MapGroup("/api/players").WithTags("Players");

        players.MapPost("/register", Register)
            .WithName("RegisterPlayer")
            .RequireRateLimiting("auth");

        players.MapPost("/login", Login)
            .WithName("LoginPlayer")
            .RequireRateLimiting("auth");

        players.MapPost("/logout", Logout)
            .WithName("LogoutPlayer");

        players.MapGet("/me", Me)
            .WithName("GetCurrentPlayer");

        players.MapGet("/me/performance", Performance)
            .WithName("GetPlayerPerformance");

        var challenges = app.MapGroup("/api/challenges").WithTags("Challenges");

        challenges.MapGet("", ListChallenges)
            .WithName("ListChallenges");

        challenges.MapPost("/{key}/run", RunChallenge)
            .WithName("RunChallenge")
            .RequireRateLimiting("arena-run");

        app.MapGet("/api/leaderboard", Leaderboard)
            .WithTags("Leaderboard")
            .WithName("GetLeaderboard")
            .RequireRateLimiting("arena");

        return app;
    }

    private static async Task<IResult> Register(
        RegisterRequest request,
        PlayerAuthService auth,
        CancellationToken cancellationToken)
    {
        var result = await auth.RegisterAsync(request.Username, request.Password, cancellationToken);
        return Results.Created(
            "/api/players/me",
            new AuthResponse(result.Token, ToPlayerView(result.Player)));
    }

    private static async Task<IResult> Login(
        LoginRequest request,
        PlayerAuthService auth,
        CancellationToken cancellationToken)
    {
        var result = await auth.LoginAsync(request.Username, request.Password, cancellationToken);
        return Results.Ok(new AuthResponse(result.Token, ToPlayerView(result.Player)));
    }

    private static async Task<IResult> Logout(
        HttpContext context,
        PlayerAuthService auth,
        CancellationToken cancellationToken)
    {
        var session = await RequireSession(context, auth, cancellationToken);
        await auth.LogoutAsync(session, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> Me(
        HttpContext context,
        PlayerAuthService auth,
        CancellationToken cancellationToken)
    {
        var session = await RequireSession(context, auth, cancellationToken);
        return Results.Ok(ToPlayerView(session.Player!));
    }

    private static async Task<IResult> Performance(
        HttpContext context,
        PlayerAuthService auth,
        ChallengeService challenges,
        CancellationToken cancellationToken)
    {
        var session = await RequireSession(context, auth, cancellationToken);
        var performance = await challenges.PerformanceAsync(session.Player!, cancellationToken);
        return Results.Ok(performance);
    }

    private static IResult ListChallenges() =>
        Results.Ok(ChallengeCatalog.All.Select(ToChallengeView).ToList());

    private static async Task<IResult> Leaderboard(
        string? challenge,
        int? limit,
        ChallengeService challenges,
        CancellationToken cancellationToken)
    {
        var key = string.IsNullOrWhiteSpace(challenge)
            ? ChallengeCatalog.All[0].Key
            : challenge;

        var view = await challenges.LeaderboardAsync(key, limit ?? 50, cancellationToken);
        return Results.Ok(view);
    }

    private static async Task<IResult> RunChallenge(
        string key,
        HttpContext context,
        PlayerAuthService auth,
        ChallengeService challenges,
        VisitorCookie cookie,
        CancellationToken cancellationToken)
    {
        var definition = ChallengeCatalog.Find(key)
            ?? throw new CompetitionException(StatusCodes.Status404NotFound, "Unknown challenge.");

        var session = await RequireSession(context, auth, cancellationToken);
        var visitor = cookie.Resolve(context);
        var execution = await challenges.RunAsync(definition, session.PlayerId, visitor, cancellationToken);

        return Results.Ok(new ChallengeRunView(
            ToChallengeView(definition),
            execution.Run.Score,
            execution.Run.Verified,
            execution.Rank,
            execution.PersonalBest,
            execution.Result));
    }

    private static async Task<PlayerSession> RequireSession(
        HttpContext context,
        PlayerAuthService auth,
        CancellationToken cancellationToken) =>
        await auth.AuthenticateAsync(context, cancellationToken)
        ?? throw new CompetitionException(StatusCodes.Status401Unauthorized, "Sign in to continue.");

    private static PlayerView ToPlayerView(Player player) =>
        new(player.Id, player.Username, player.CreatedAt);

    private static ChallengeView ToChallengeView(ChallengeDefinition definition) =>
        new(
            definition.Key,
            definition.Name,
            definition.Summary,
            definition.Strategy,
            definition.Requests,
            definition.Concurrency,
            definition.AmountCents);
}
