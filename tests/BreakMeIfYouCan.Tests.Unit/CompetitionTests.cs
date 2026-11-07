using BreakMeIfYouCan.Api.Competition;

namespace BreakMeIfYouCan.Tests.Unit;

public sealed class CompetitionTests
{
    [Fact]
    public void PasswordHash_RoundTripsAndRejectsTheWrongPassword()
    {
        var hash = PlayerPasswords.Hash("correct horse battery staple");

        Assert.True(PlayerPasswords.Verify("correct horse battery staple", hash));
        Assert.False(PlayerPasswords.Verify("Correct horse battery staple", hash));
        Assert.False(PlayerPasswords.Verify("wrong password", hash));
        Assert.False(PlayerPasswords.Verify(string.Empty, hash));
    }

    [Fact]
    public void PasswordHash_UsesAFreshSaltForEveryHash()
    {
        var first = PlayerPasswords.Hash("same password");
        var second = PlayerPasswords.Hash("same password");

        Assert.NotEqual(first, second);
        Assert.True(PlayerPasswords.Verify("same password", first));
        Assert.True(PlayerPasswords.Verify("same password", second));
    }

    [Fact]
    public void Scoring_RewardsCompletedTransfersAndPenalisesConflictsAndTime()
    {
        var clean = CompetitionScoring.Score(accepted: 20, rejected: 0, conflicts: 0, retries: 0, durationMs: 1_000);
        var contended = CompetitionScoring.Score(accepted: 20, rejected: 0, conflicts: 20, retries: 20, durationMs: 5_000);
        var failed = CompetitionScoring.Score(accepted: 19, rejected: 1, conflicts: 0, retries: 0, durationMs: 1_000);

        Assert.Equal(19_980, clean);
        Assert.True(contended < clean);
        Assert.True(failed < clean);
        Assert.Equal(0, CompetitionScoring.Score(0, 1, 100, 100, 60_000));
    }

    [Fact]
    public void Catalog_HasDistinctValidChallenges()
    {
        Assert.NotEmpty(ChallengeCatalog.All);
        Assert.Equal(ChallengeCatalog.All.Count, ChallengeCatalog.All.Select(c => c.Key).Distinct(StringComparer.Ordinal).Count());

        foreach (var challenge in ChallengeCatalog.All)
        {
            Assert.True(challenge.Requests > 0);
            Assert.True(challenge.Concurrency > 0);
            Assert.True(challenge.Concurrency <= challenge.Requests);
            Assert.True(challenge.AmountCents > 0);
            Assert.Contains(challenge.Strategy, new[] { "optimistic", "pessimistic" });
            Assert.NotNull(ChallengeCatalog.Find(challenge.Key));
        }
    }
}
