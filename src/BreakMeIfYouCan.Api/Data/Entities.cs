namespace BreakMeIfYouCan.Api.Data;

/// <summary>
/// One anonymous visitor sandbox. Visitors are never registered; a protected cookie maps
/// a browser to its sandbox. A reset archives the sandbox and starts a new one so the
/// ledger of the previous run stays immutable.
/// </summary>
public sealed class Sandbox
{
    public Guid Id { get; set; }

    public Guid VisitorId { get; set; }

    public string ScenarioKey { get; set; } = ArenaScenarioKeys.Starter;

    public long InitialBalanceCents { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public List<Account> Accounts { get; set; } = [];
}

public sealed class Account
{
    public Guid Id { get; set; }

    public Guid SandboxId { get; set; }

    public Sandbox? Sandbox { get; set; }

    /// <summary>Short stable code used by the UI, for example "A" or "B".</summary>
    public string Code { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>Balance in minor units. Guarded by a database CHECK constraint.</summary>
    public long BalanceCents { get; set; }

    /// <summary>
    /// PostgreSQL's transaction id (xmin) mapped as an optimistic concurrency token.
    /// A uint so that it maps cleanly to the xid system column.
    /// </summary>
    public uint Version { get; set; }
}

public sealed class Transfer
{
    public Guid Id { get; set; }

    public Guid SandboxId { get; set; }

    public Guid FromAccountId { get; set; }

    public Guid ToAccountId { get; set; }

    public long AmountCents { get; set; }

    /// <summary>optimistic or pessimistic.</summary>
    public string Strategy { get; set; } = TransferStrategies.Optimistic;

    /// <summary>completed or rejected.</summary>
    public string Status { get; set; } = TransferStatus.Completed;

    public string? IdempotencyKey { get; set; }

    /// <summary>1 means no retry was required.</summary>
    public int Attempts { get; set; } = 1;

    /// <summary>Optimistic concurrency conflicts observed while completing this transfer.</summary>
    public int Conflicts { get; set; }

    public int DurationMicroseconds { get; set; }

    public string? FailureReason { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public List<LedgerEntry> Entries { get; set; } = [];
}

/// <summary>
/// One side of a double-entry transfer. Entries are append-only; the database rejects
/// updates and deletes, and a deferred constraint trigger rejects an unbalanced transfer.
/// </summary>
public sealed class LedgerEntry
{
    public Guid Id { get; set; }

    public Guid TransferId { get; set; }

    public Guid AccountId { get; set; }

    /// <summary>debit or credit.</summary>
    public string Direction { get; set; } = LedgerDirections.Debit;

    public long AmountCents { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public static class TransferStrategies
{
    public const string Optimistic = "optimistic";
    public const string Pessimistic = "pessimistic";

    public static bool IsValid(string? value) =>
        value is not null &&
        (value.Equals(Optimistic, StringComparison.OrdinalIgnoreCase) ||
         value.Equals(Pessimistic, StringComparison.OrdinalIgnoreCase));

    public static string Normalize(string? value) =>
        value is not null && value.Equals(Pessimistic, StringComparison.OrdinalIgnoreCase)
            ? Pessimistic
            : Optimistic;
}

public static class TransferStatus
{
    public const string Completed = "completed";
    public const string Rejected = "rejected";
}

public static class LedgerDirections
{
    public const string Debit = "debit";
    public const string Credit = "credit";
}

public sealed record ArenaScenario(
    string Key,
    string Name,
    string Description,
    long InitialBalanceCents,
    long MaxAmountCents,
    int MaxRequests,
    int MaxConcurrency);

public static class ArenaScenarioKeys
{
    public const string Starter = "starter";

    public static ArenaScenario StarterScenario { get; } = new(
        Starter,
        "The classic race",
        "Two accounts, one shared balance row and a burst of concurrent transfers. Optimistic control detects the conflict and retries; pessimistic control serialises the row.",
        100_000,
        10_000,
        50,
        16);
}
