using BreakMeIfYouCan.Api.Arena;

namespace BreakMeIfYouCan.Api.Competition;

public sealed record RegisterRequest(string Username, string Password);

public sealed record LoginRequest(string Username, string Password);

public sealed record PlayerView(Guid Id, string Username, DateTimeOffset CreatedAt);

public sealed record AuthResponse(string Token, PlayerView Player);

public sealed record ChallengeView(
    string Key,
    string Name,
    string Summary,
    string Strategy,
    int Requests,
    int Concurrency,
    long AmountCents);

public sealed record LeaderboardEntryView(
    int Rank,
    string Username,
    long Score,
    double DurationMs,
    int Accepted,
    int Conflicts,
    int Retries,
    DateTimeOffset AchievedAt);

public sealed record LeaderboardView(
    string ChallengeKey,
    string ChallengeName,
    string Summary,
    IReadOnlyList<LeaderboardEntryView> Entries);

public sealed record ChallengeRunView(
    ChallengeView Challenge,
    long Score,
    bool Verified,
    int? Rank,
    long PersonalBest,
    ArenaRunResult Run);

public sealed record BestRunView(
    string ChallengeKey,
    string ChallengeName,
    long Score,
    double DurationMs,
    DateTimeOffset AchievedAt);

public sealed record RunHistoryView(
    string ChallengeKey,
    string ChallengeName,
    long Score,
    bool Verified,
    double DurationMs,
    int Accepted,
    int Rejected,
    int Conflicts,
    int Retries,
    DateTimeOffset CreatedAt);

public sealed record PerformanceView(
    PlayerView Player,
    IReadOnlyList<BestRunView> Bests,
    IReadOnlyList<RunHistoryView> Recent,
    int TotalRuns,
    int VerifiedRuns);
