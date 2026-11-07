namespace BreakMeIfYouCan.Api.Competition;

/// <summary>
/// Fixed, server-owned challenges. The client chooses a key; it cannot alter the request size,
/// concurrency or strategy of a ranked run.
/// </summary>
public sealed record ChallengeDefinition(
    string Key,
    string Name,
    string Summary,
    string Strategy,
    int Requests,
    int Concurrency,
    long AmountCents,
    string Pattern,
    string Idempotency);

public static class ChallengeCatalog
{
    public static readonly IReadOnlyList<ChallengeDefinition> All =
    [
        new(
            "optimistic-8",
            "Optimistic · 8",
            "Eight transfers, two at a time, optimistic xmin retries with jitter. Conflicts are expected; the run still has to balance.",
            "optimistic",
            8,
            2,
            1_000,
            "alternating",
            "unique"),
        new(
            "pessimistic-20",
            "Pessimistic · 20",
            "Twenty transfers, five at a time, SELECT FOR UPDATE in a consistent account order. Fewer conflicts, more waiting.",
            "pessimistic",
            20,
            5,
            1_000,
            "alternating",
            "unique"),
    ];

    public static ChallengeDefinition? Find(string? key) =>
        All.FirstOrDefault(challenge => string.Equals(challenge.Key, key, StringComparison.Ordinal));
}

/// <summary>
/// The score is computed by the server from the run the server executed. It rewards completed
/// transfers and penalises rejected transfers, conflicts, retries and wall-clock time.
/// </summary>
public static class CompetitionScoring
{
    public static long Score(int accepted, int rejected, int conflicts, int retries, double durationMs)
    {
        var timePenalty = (long)(Math.Max(0, durationMs) / 50.0);
        var score = (accepted * 1_000L) - (rejected * 500L) - (conflicts * 25L) - (retries * 10L) - timePenalty;
        return Math.Max(0, score);
    }
}
