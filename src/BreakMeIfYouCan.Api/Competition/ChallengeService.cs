using System.Text.Json;
using BreakMeIfYouCan.Api.Arena;
using BreakMeIfYouCan.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace BreakMeIfYouCan.Api.Competition;

public sealed record ChallengeExecution(ChallengeRun Run, ArenaRunResult Result, int? Rank, long PersonalBest);

/// <summary>
/// Executes the fixed challenges on the server and records only the metrics the server
/// produced. A ranked entry exists only when the ledger integrity check passed and every
/// requested transfer completed.
/// </summary>
public sealed class ChallengeService(
    ArenaDbContext db,
    IServiceScopeFactory scopeFactory,
    ArenaRunService runs,
    SandboxService sandboxes)
{
    public async Task<ChallengeExecution> RunAsync(
        ChallengeDefinition definition,
        Guid playerId,
        Guid visitorId,
        CancellationToken cancellationToken)
    {
        var session = await sandboxes.GetOrCreateAsync(visitorId, cancellationToken);
        var request = new ArenaRunRequest
        {
            Strategy = definition.Strategy,
            Requests = definition.Requests,
            Concurrency = definition.Concurrency,
            AmountCents = definition.AmountCents,
            Pattern = definition.Pattern,
            Idempotency = definition.Idempotency,
            ReplayKeys = 5,
        };

        var result = await runs.RunAsync(session.Sandbox.Id, session.Accounts, request, cancellationToken);
        var verified = result.Integrity.Ok
            && result.Accepted == definition.Requests
            && result.Rejected == 0;

        var score = CompetitionScoring.Score(
            result.Accepted,
            result.Rejected,
            result.Conflicts,
            result.TotalRetries,
            result.DurationMs);

        var run = new ChallengeRun
        {
            Id = Guid.TryParse(result.RunId, out var runId) ? runId : Guid.NewGuid(),
            PlayerId = playerId,
            SandboxId = session.Sandbox.Id,
            ChallengeKey = definition.Key,
            Strategy = definition.Strategy,
            Requests = definition.Requests,
            Concurrency = definition.Concurrency,
            AmountCents = definition.AmountCents,
            Pattern = definition.Pattern,
            Idempotency = definition.Idempotency,
            Accepted = result.Accepted,
            Rejected = result.Rejected,
            Replayed = result.Replayed,
            TotalRetries = result.TotalRetries,
            Conflicts = result.Conflicts,
            DurationMs = result.DurationMs,
            Score = score,
            Verified = verified,
            IntegrityJson = JsonSerializer.Serialize(result.Integrity),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // A ranked run can take tens of seconds. The request-scoped DbContext was used to
        // authenticate before the run; its pooled connection may have gone stale while the
        // arena worked. Record the result through a fresh scope so a slow run cannot fail on
        // a dead connection at the last step.
        await using var writeScope = scopeFactory.CreateAsyncScope();
        var writeDb = writeScope.ServiceProvider.GetRequiredService<ArenaDbContext>();
        writeDb.ChallengeRuns.Add(run);

        try
        {
            await writeDb.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new CompetitionException(StatusCodes.Status409Conflict, "This run has already been recorded.");
        }

        var rank = verified ? await RankAsync(writeDb, definition.Key, playerId, cancellationToken) : null;
        var personalBest = await PersonalBestAsync(writeDb, definition.Key, playerId, cancellationToken);
        return new ChallengeExecution(run, result, rank, personalBest);
    }

    public async Task<LeaderboardView> LeaderboardAsync(string key, int limit, CancellationToken cancellationToken)
    {
        var definition = ChallengeCatalog.Find(key)
            ?? throw new CompetitionException(StatusCodes.Status404NotFound, "Unknown challenge.");

        var take = Math.Clamp(limit, 1, 100);
        var best = await LoadBestAsync(db, definition.Key, take, cancellationToken);

        var entries = best
            .Select((row, index) => new LeaderboardEntryView(
                index + 1,
                row.Username,
                row.Score,
                row.DurationMs,
                row.Accepted,
                row.Conflicts,
                row.Retries,
                row.AchievedAt))
            .ToList();

        return new LeaderboardView(definition.Key, definition.Name, definition.Summary, entries);
    }

    public async Task<PerformanceView> PerformanceAsync(Player player, CancellationToken cancellationToken)
    {
        var all = await db.ChallengeRuns
            .AsNoTracking()
            .Where(run => run.PlayerId == player.Id)
            .OrderByDescending(run => run.CreatedAt)
            .ToListAsync(cancellationToken);

        var bests = all
            .Where(run => run.Verified)
            .GroupBy(run => run.ChallengeKey)
            .Select(group => group.OrderByDescending(run => run.Score).ThenBy(run => run.DurationMs).First())
            .Select(run => new BestRunView(
                run.ChallengeKey,
                ChallengeName(run.ChallengeKey),
                run.Score,
                run.DurationMs,
                run.CreatedAt))
            .OrderByDescending(run => run.Score)
            .ToList();

        var recent = all
            .Take(20)
            .Select(run => new RunHistoryView(
                run.ChallengeKey,
                ChallengeName(run.ChallengeKey),
                run.Score,
                run.Verified,
                run.DurationMs,
                run.Accepted,
                run.Rejected,
                run.Conflicts,
                run.TotalRetries,
                run.CreatedAt))
            .ToList();

        return new PerformanceView(
            new PlayerView(player.Id, player.Username, player.CreatedAt),
            bests,
            recent,
            all.Count,
            all.Count(run => run.Verified));
    }

    private async Task<int?> RankAsync(ArenaDbContext context, string key, Guid playerId, CancellationToken cancellationToken)
    {
        var best = await LoadBestAsync(context, key, 100, cancellationToken);
        var index = best.FindIndex(row => row.PlayerId == playerId);
        return index >= 0 ? index + 1 : null;
    }

    private async Task<long> PersonalBestAsync(ArenaDbContext context, string key, Guid playerId, CancellationToken cancellationToken)
    {
        var scores = await context.ChallengeRuns
            .AsNoTracking()
            .Where(run => run.PlayerId == playerId && run.ChallengeKey == key && run.Verified)
            .Select(run => (long?)run.Score)
            .ToListAsync(cancellationToken);

        return scores.Max() ?? 0;
    }

    private async Task<List<LeaderboardRow>> LoadBestAsync(ArenaDbContext context, string key, int take, CancellationToken cancellationToken)
    {
        // Load a bounded slice ordered by the score, then keep each player's best. The slice is
        // deliberately larger than the requested page so a prolific player cannot crowd out
        // everyone else with repeated runs.
        var rows = await context.ChallengeRuns
            .AsNoTracking()
            .Where(run => run.ChallengeKey == key && run.Verified)
            .OrderByDescending(run => run.Score)
            .ThenBy(run => run.DurationMs)
            .ThenBy(run => run.CreatedAt)
            .Select(run => new LeaderboardRow(
                run.PlayerId,
                run.Player!.Username,
                run.Score,
                run.DurationMs,
                run.Accepted,
                run.Conflicts,
                run.TotalRetries,
                run.CreatedAt))
            .Take(1000)
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.PlayerId)
            .Select(group => group.First())
            .Take(take)
            .ToList();
    }

    private static string ChallengeName(string key) =>
        ChallengeCatalog.Find(key)?.Name ?? key;

    private sealed record LeaderboardRow(
        Guid PlayerId,
        string Username,
        long Score,
        double DurationMs,
        int Accepted,
        int Conflicts,
        int Retries,
        DateTimeOffset AchievedAt);
}
