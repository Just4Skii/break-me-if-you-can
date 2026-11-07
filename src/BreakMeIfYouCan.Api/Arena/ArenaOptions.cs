namespace BreakMeIfYouCan.Api.Arena;

public sealed class ArenaOptions
{
    public const string SectionName = "Arena";

    /// <summary>Process-wide cap on simultaneous database transactions from visitors.</summary>
    public int GlobalConcurrencyLimit { get; set; } = 8;

    /// <summary>Total attempts for the optimistic strategy (the first attempt is not a retry).</summary>
    public int MaxRetryAttempts { get; set; } = 10;

    public int BackoffBaseMilliseconds { get; set; } = 5;

    public int BackoffMaxMilliseconds { get; set; } = 200;

    /// <summary>Process-wide cap on simultaneous arena runs.</summary>
    public int MaxConcurrentRuns { get; set; } = 2;

    public int MaxRequestsPerRun { get; set; } = 100;

    public int MaxConcurrencyPerRun { get; set; } = 16;

    public int SandboxLifetimeDays { get; set; } = 30;

    public bool EnableReset { get; set; } = true;
}
