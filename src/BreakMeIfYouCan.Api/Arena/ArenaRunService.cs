using System.Collections.Concurrent;
using System.Diagnostics;
using BreakMeIfYouCan.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BreakMeIfYouCan.Api.Arena;

/// <summary>
/// Runs a fixed challenge scenario: a burst of transfers against the visitor's own
/// sandbox, executed with a bounded degree of parallelism and a process-wide transaction
/// cap. Every number in the result is measured, not simulated.
/// </summary>
public sealed class ArenaRunService(
    TransferExecutor executor,
    ArenaConcurrencyGate gate,
    LedgerIntegrityService integrity,
    IServiceScopeFactory scopeFactory,
    IOptions<ArenaOptions> options,
    ILogger<ArenaRunService> logger)
{
    private readonly ArenaOptions _options = options.Value;

    public async Task<ArenaRunResult> RunAsync(
        Guid sandboxId,
        IReadOnlyList<Account> accounts,
        ArenaRunRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(accounts);

        var scenario = ArenaScenarioKeys.StarterScenario;
        var strategy = TransferStrategies.IsValid(request.Strategy)
            ? TransferStrategies.Normalize(request.Strategy)
            : throw new ArenaException("Strategy must be 'optimistic' or 'pessimistic'.", StatusCodes.Status422UnprocessableEntity);

        var maxRequests = Math.Min(_options.MaxRequestsPerRun, scenario.MaxRequests);
        if (request.Requests is < 1 || request.Requests > maxRequests)
        {
            throw new ArenaException("Requests must be between 1 and " + maxRequests + ".", StatusCodes.Status422UnprocessableEntity);
        }

        var maxConcurrency = Math.Min(_options.MaxConcurrencyPerRun, scenario.MaxConcurrency);
        if (request.Concurrency is < 1 || request.Concurrency > maxConcurrency)
        {
            throw new ArenaException("Concurrency must be between 1 and " + maxConcurrency + ".", StatusCodes.Status422UnprocessableEntity);
        }

        if (request.AmountCents is < 1 || request.AmountCents > scenario.MaxAmountCents)
        {
            throw new ArenaException(
                "Amount must be between 1 and " + scenario.MaxAmountCents + " cents.",
                StatusCodes.Status422UnprocessableEntity);
        }

        var pattern = request.Pattern?.Trim().ToLowerInvariant() ?? "alternating";
        if (pattern is not ("alternating" or "a_to_b"))
        {
            throw new ArenaException("Pattern must be 'alternating' or 'a_to_b'.", StatusCodes.Status422UnprocessableEntity);
        }

        var idempotency = request.Idempotency?.Trim().ToLowerInvariant() ?? "unique";
        if (idempotency is not ("unique" or "replay"))
        {
            throw new ArenaException("Idempotency must be 'unique' or 'replay'.", StatusCodes.Status422UnprocessableEntity);
        }

        var replayKeys = Math.Clamp(request.ReplayKeys, 1, 20);
        if (idempotency == "replay")
        {
            // Replaying a key must not also reinterpret the transfer direction.
            pattern = "a_to_b";
        }

        var byCode = accounts.ToDictionary(a => a.Code, StringComparer.OrdinalIgnoreCase);
        if (!byCode.TryGetValue("A", out var accountA) || !byCode.TryGetValue("B", out var accountB))
        {
            throw new ArenaException("The sandbox does not have the expected accounts.", StatusCodes.Status409Conflict);
        }

        var runId = Guid.NewGuid();
        var commands = BuildCommands(runId, sandboxId, accountA, accountB, pattern, idempotency, replayKeys, request);

        using var runLease = await gate.AcquireRunAsync(cancellationToken);
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var receipts = new ConcurrentBag<RunReceipt>();
        var index = 0;

        await Parallel.ForEachAsync(
            commands,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = request.Concurrency,
                CancellationToken = cancellationToken,
            },
            async (command, token) =>
            {
                var receiptIndex = Interlocked.Increment(ref index) - 1;
                try
                {
                    var outcome = await executor.ExecuteAsync(command, token);
                    receipts.Add(new RunReceipt
                    {
                        Index = receiptIndex,
                        Status = outcome.Status,
                        Replayed = outcome.Replayed,
                        Attempts = outcome.Attempts,
                        Conflicts = outcome.Conflicts,
                        DurationMs = outcome.DurationMs,
                        FailureReason = outcome.FailureReason,
                    });
                }
                catch (ArenaException exception)
                {
                    receipts.Add(new RunReceipt
                    {
                        Index = receiptIndex,
                        Status = "error",
                        Replayed = false,
                        Attempts = 1,
                        Conflicts = 0,
                        DurationMs = 0,
                        FailureReason = exception.Message,
                    });
                }
            });

        stopwatch.Stop();
        var completedAt = DateTimeOffset.UtcNow;

        var ordered = receipts.OrderBy(r => r.Index).ToList();
        var (balances, integrityReport) = await ReadFinalStateAsync(sandboxId, cancellationToken);

        var result = new ArenaRunResult
        {
            RunId = runId.ToString(),
            Strategy = strategy,
            Pattern = pattern,
            Idempotency = idempotency,
            Requested = commands.Count,
            Concurrency = request.Concurrency,
            AmountCents = request.AmountCents,
            Accepted = ordered.Count(r => r.Status == TransferStatus.Completed),
            Rejected = ordered.Count(r => r.Status == TransferStatus.Rejected),
            Replayed = ordered.Count(r => r.Replayed),
            TotalRetries = ordered.Sum(r => Math.Max(0, r.Attempts - 1)),
            Conflicts = ordered.Sum(r => r.Conflicts),
            DurationMs = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
            Latency = ComputeLatency(ordered),
            FinalBalances = balances,
            Integrity = integrityReport,
            Receipts = ordered,
            StartedAt = startedAt,
            CompletedAt = completedAt,
        };

        logger.LogInformation(
            "Arena run {RunId}: strategy={Strategy} requests={Requests} accepted={Accepted} rejected={Rejected} retries={Retries} conflicts={Conflicts} duration={Duration}ms",
            result.RunId,
            result.Strategy,
            result.Requested,
            result.Accepted,
            result.Rejected,
            result.TotalRetries,
            result.Conflicts,
            result.DurationMs);

        return result;
    }

    private static List<TransferCommand> BuildCommands(
        Guid runId,
        Guid sandboxId,
        Account accountA,
        Account accountB,
        string pattern,
        string idempotency,
        int replayKeys,
        ArenaRunRequest request)
    {
        var commands = new List<TransferCommand>(request.Requests);

        for (var i = 0; i < request.Requests; i++)
        {
            var fromA = pattern == "a_to_b" || i % 2 == 0;
            var from = fromA ? accountA : accountB;
            var to = fromA ? accountB : accountA;

            var key = idempotency == "replay"
                ? "run-" + runId.ToString("N") + "-key-" + (i % replayKeys)
                : "run-" + runId.ToString("N") + "-" + i.ToString("D4");

            commands.Add(new TransferCommand
            {
                SandboxId = sandboxId,
                FromAccountId = from.Id,
                ToAccountId = to.Id,
                AmountCents = request.AmountCents,
                Strategy = request.Strategy,
                IdempotencyKey = key,
                MaxAmountCents = ArenaScenarioKeys.StarterScenario.MaxAmountCents,
            });
        }

        return commands;
    }

    private async Task<(IReadOnlyList<AccountView> Balances, LedgerIntegrityReport Integrity)> ReadFinalStateAsync(
        Guid sandboxId,
        CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ArenaDbContext>();

        var accounts = await db.Accounts
            .AsNoTracking()
            .Where(a => a.SandboxId == sandboxId)
            .OrderBy(a => a.Code)
            .ToListAsync(cancellationToken);

        var report = await integrity.CheckAsync(db, sandboxId, cancellationToken);

        return (
            accounts.Select(a => new AccountView
            {
                Id = a.Id,
                Code = a.Code,
                Label = a.Label,
                BalanceCents = a.BalanceCents,
            }).ToList(),
            report);
    }

    private static LatencyStats ComputeLatency(IReadOnlyList<RunReceipt> receipts)
    {
        if (receipts.Count == 0)
        {
            return new LatencyStats { MinMs = 0, P50Ms = 0, P95Ms = 0, MaxMs = 0, MeanMs = 0 };
        }

        var values = receipts.Select(r => r.DurationMs).OrderBy(v => v).ToArray();
        return new LatencyStats
        {
            MinMs = Math.Round(values[0], 3),
            P50Ms = Math.Round(Percentile(values, 0.50), 3),
            P95Ms = Math.Round(Percentile(values, 0.95), 3),
            MaxMs = Math.Round(values[^1], 3),
            MeanMs = Math.Round(values.Average(), 3),
        };
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        if (sorted.Length == 1)
        {
            return sorted[0];
        }

        var rank = (percentile * (sorted.Length - 1));
        var low = (int)Math.Floor(rank);
        var high = (int)Math.Ceiling(rank);
        return low == high ? sorted[low] : sorted[low] + ((sorted[high] - sorted[low]) * (rank - low));
    }
}
