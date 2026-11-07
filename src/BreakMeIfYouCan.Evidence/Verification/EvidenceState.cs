namespace BreakMeIfYouCan.Evidence.Verification;

/// <summary>
/// The only three states the platform is allowed to present. There is deliberately no
/// "assumed" or "probably fine" state: missing evidence is unknown, contradictory
/// evidence is failed, and "verified" requires every required check to pass.
/// </summary>
public enum EvidenceState
{
    Unknown = 0,
    Verified = 1,
    Failed = 2,
}

/// <summary>One independently reported line of the proof.</summary>
public sealed record ProofCheck
{
    public required string Id { get; init; }

    public required string Label { get; init; }

    public required EvidenceState State { get; init; }

    public required string Detail { get; init; }

    /// <summary>When false the check is informative only and cannot change the verdict.</summary>
    public bool Required { get; init; } = true;
}