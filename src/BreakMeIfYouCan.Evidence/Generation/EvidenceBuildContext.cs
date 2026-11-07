using BreakMeIfYouCan.Evidence.Manifest;

namespace BreakMeIfYouCan.Evidence.Generation;

/// <summary>
/// The run metadata the workflow records before the evidence generator runs. Paths to
/// reports are supplied separately; this type only carries facts the workflow knows.
/// </summary>
public sealed record EvidenceBuildContext
{
    public string Repository { get; init; } = string.Empty;

    public string CommitSha { get; init; } = string.Empty;

    public string Branch { get; init; } = string.Empty;

    public WorkflowEvidence Workflow { get; init; } = new();

    public BuildEvidence Build { get; init; } = new();

    public ImageEvidence Image { get; init; } = new();

    public AttestationEvidence Attestations { get; init; } = new();
}

public sealed record EvidenceBuildRequest
{
    public required EvidenceBuildContext Context { get; init; }

    public IReadOnlyList<string> TestResultFiles { get; init; } = [];

    public IReadOnlyList<string> CoverageReportFiles { get; init; } = [];

    public string? AuditReportPath { get; init; }

    public int AuditExitCode { get; init; }

    public double CoverageThresholdPercent { get; init; } = 75;

    /// <summary>Test project names whose TRX output is required for a complete manifest.</summary>
    public IReadOnlyList<string> ExpectedTestAssemblies { get; init; } = [];

    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
}
