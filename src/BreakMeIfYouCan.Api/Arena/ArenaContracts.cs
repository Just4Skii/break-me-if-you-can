namespace BreakMeIfYouCan.Api.Arena;

public sealed record AccountView
{
    public required Guid Id { get; init; }

    public required string Code { get; init; }

    public required string Label { get; init; }

    public required long BalanceCents { get; init; }
}

public sealed record ArenaLimits
{
    public required int MaxRequestsPerRun { get; init; }

    public required int MaxConcurrencyPerRun { get; init; }

    public required long MaxAmountCents { get; init; }

    public required int GlobalConcurrencyLimit { get; init; }

    public required int MaxRetryAttempts { get; init; }
}

public sealed record SandboxView
{
    public required Guid SandboxId { get; init; }

    public required string ScenarioKey { get; init; }

    public required string ScenarioName { get; init; }

    public required string ScenarioDescription { get; init; }

    public required long InitialBalanceCents { get; init; }

    public required IReadOnlyList<AccountView> Accounts { get; init; }

    public required ArenaLimits Limits { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record TransferRequest
{
    public Guid FromAccountId { get; init; }

    public Guid ToAccountId { get; init; }

    public long AmountCents { get; init; }

    public string? Strategy { get; init; }
}

public sealed record TransferResponse
{
    public required Guid TransferId { get; init; }

    public required string Status { get; init; }

    public required bool Replayed { get; init; }

    public required string Strategy { get; init; }

    public required int Attempts { get; init; }

    public required int Conflicts { get; init; }

    public required double DurationMs { get; init; }

    public string? FailureReason { get; init; }

    public required IReadOnlyList<AccountView> Balances { get; init; }
}

public sealed record ArenaRunRequest
{
    public string Strategy { get; init; } = "optimistic";

    public int Requests { get; init; } = 50;

    public int Concurrency { get; init; } = 8;

    public long AmountCents { get; init; } = 1_000;

    /// <summary>"alternating" sends transfers in both directions; "a_to_b" always drains A.</summary>
    public string Pattern { get; init; } = "alternating";

    /// <summary>"unique" gives every request its own key; "replay" reuses a small key pool.</summary>
    public string Idempotency { get; init; } = "unique";

    public int ReplayKeys { get; init; } = 5;
}

public sealed record RunReceipt
{
    public required int Index { get; init; }

    public required string Status { get; init; }

    public required bool Replayed { get; init; }

    public required int Attempts { get; init; }

    public required int Conflicts { get; init; }

    public required double DurationMs { get; init; }

    public string? FailureReason { get; init; }
}

public sealed record LatencyStats
{
    public required double MinMs { get; init; }

    public required double P50Ms { get; init; }

    public required double P95Ms { get; init; }

    public required double MaxMs { get; init; }

    public required double MeanMs { get; init; }
}

public sealed record LedgerIntegrityReport
{
    public required bool Balanced { get; init; }

    public required long NetCents { get; init; }

    public required int CompletedTransfers { get; init; }

    public required int LedgerEntries { get; init; }

    public required int MalformedTransfers { get; init; }

    public required int NegativeAccounts { get; init; }

    public required long TotalBalanceCents { get; init; }

    public bool Ok => Balanced && MalformedTransfers == 0 && NegativeAccounts == 0;
}

public sealed record ArenaRunResult
{
    public required string RunId { get; init; }

    public required string Strategy { get; init; }

    public required string Pattern { get; init; }

    public required string Idempotency { get; init; }

    public required int Requested { get; init; }

    public required int Concurrency { get; init; }

    public required long AmountCents { get; init; }

    public required int Accepted { get; init; }

    public required int Rejected { get; init; }

    public required int Replayed { get; init; }

    public required int TotalRetries { get; init; }

    public required int Conflicts { get; init; }

    public required double DurationMs { get; init; }

    public required LatencyStats Latency { get; init; }

    public required IReadOnlyList<AccountView> FinalBalances { get; init; }

    public required LedgerIntegrityReport Integrity { get; init; }

    public required IReadOnlyList<RunReceipt> Receipts { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    public required DateTimeOffset CompletedAt { get; init; }
}

public sealed record LedgerEntryView
{
    public required string Direction { get; init; }

    public required long AmountCents { get; init; }

    public required string AccountCode { get; init; }
}

public sealed record LedgerTransferView
{
    public required Guid TransferId { get; init; }

    public required long AmountCents { get; init; }

    public required string Status { get; init; }

    public required string Strategy { get; init; }

    public required int Attempts { get; init; }

    public required int Conflicts { get; init; }

    public required string FromCode { get; init; }

    public required string ToCode { get; init; }

    public string? FailureReason { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required IReadOnlyList<LedgerEntryView> Entries { get; init; }
}

public sealed record LedgerView
{
    public required IReadOnlyList<LedgerTransferView> Transfers { get; init; }

    public required LedgerIntegrityReport Integrity { get; init; }
}

public sealed class ArenaException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
