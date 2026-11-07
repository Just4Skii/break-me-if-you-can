using BreakMeIfYouCan.Evidence.Manifest;

namespace BreakMeIfYouCan.Evidence.Verification;

/// <summary>
/// The public contract of GET /proof. Every value in this document is either read from a
/// CI artifact produced by the build, observed from a runtime API, or explicitly unknown.
/// There are no default-success values anywhere in the type.
/// </summary>
public sealed record ProofReport
{
    public EvidenceState State { get; init; } = EvidenceState.Unknown;

    public string Summary { get; init; } = string.Empty;

    public DateTimeOffset GeneratedAt { get; init; }

    public string EvidenceSource { get; init; } = "none";

    public DateTimeOffset? EvidenceFetchedAt { get; init; }

    public FreshnessInfo Freshness { get; init; } = new();

    public SourceInfo Source { get; init; } = new();

    public WorkflowInfo Workflow { get; init; } = new();

    public BuildInfo Build { get; init; } = new();

    public TestInfo Tests { get; init; } = new();

    public CoverageInfo Coverage { get; init; } = new();

    public DependencyAuditInfo DependencyAudit { get; init; } = new();

    public ImageInfo Image { get; init; } = new();

    public DeploymentInfo Deployment { get; init; } = new();

    public AttestationInfo Attestation { get; init; } = new();

    public SecurityPostureInfo SecurityPosture { get; init; } = new();

    public IReadOnlyList<ProofCheck> Checks { get; init; } = [];

    /// <summary>Plain-language notes about why evidence is missing, shown beside the verdict.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    /// <summary>Copy-paste command that lets the visitor verify the attestation themselves.</summary>
    public string? VerifyCommand { get; init; }
}

public sealed record FreshnessInfo
{
    public DateTimeOffset? EvidenceGeneratedAt { get; init; }

    public double? AgeSeconds { get; init; }

    public double MaxAgeSeconds { get; init; }

    public bool Stale { get; init; }

    public string? Detail { get; init; }
}

public sealed record SourceInfo
{
    public string Repository { get; init; } = string.Empty;

    public string CommitSha { get; init; } = string.Empty;

    public string ShortCommitSha { get; init; } = string.Empty;

    public string Branch { get; init; } = string.Empty;

    /// <summary>Commit compiled into the running assembly. Not a deployment environment variable.</summary>
    public string RunningCommitSha { get; init; } = string.Empty;
}

public sealed record WorkflowInfo
{
    public EvidenceState State { get; init; } = EvidenceState.Unknown;

    public string Name { get; init; } = string.Empty;

    public string File { get; init; } = string.Empty;

    public string RunId { get; init; } = string.Empty;

    public string RunAttempt { get; init; } = string.Empty;

    public string RunUrl { get; init; } = string.Empty;

    public string Event { get; init; } = string.Empty;

    public string Conclusion { get; init; } = string.Empty;
}

public sealed record BuildInfo
{
    public EvidenceState State { get; init; } = EvidenceState.Unknown;

    public string Configuration { get; init; } = string.Empty;

    public string Framework { get; init; } = string.Empty;

    public string DotnetVersion { get; init; } = string.Empty;

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string Runner { get; init; } = string.Empty;
}

public sealed record TestInfo
{
    public EvidenceState State { get; init; } = EvidenceState.Unknown;

    public int Total { get; init; }

    public int Passed { get; init; }

    public int Failed { get; init; }

    public int Skipped { get; init; }

    public double DurationSeconds { get; init; }

    public IReadOnlyList<TestAssemblyEvidence> Assemblies { get; init; } = [];

    public IReadOnlyList<string> Reports { get; init; } = [];
}

public sealed record CoverageInfo
{
    public EvidenceState State { get; init; } = EvidenceState.Unknown;

    public double? LinePercent { get; init; }

    public double? BranchPercent { get; init; }

    public double ThresholdPercent { get; init; }

    public bool? MeetsThreshold { get; init; }

    public int? LinesCovered { get; init; }

    public int? LinesValid { get; init; }

    public string? ReportArtifact { get; init; }
}

public sealed record DependencyAuditInfo
{
    public EvidenceState State { get; init; } = EvidenceState.Unknown;

    public string Status { get; init; } = "missing";

    public int DirectCount { get; init; }

    public int TransitiveCount { get; init; }

    public IReadOnlyDictionary<string, int> BySeverity { get; init; } = new Dictionary<string, int>();

    public IReadOnlyList<VulnerablePackage> Packages { get; init; } = [];

    public string Command { get; init; } = string.Empty;

    /// <summary>Scope statement: what the scan covers and what it does not.</summary>
    public string Scope { get; init; } = string.Empty;
}

public sealed record ImageInfo
{
    public string Reference { get; init; } = string.Empty;

    public string? BuiltDigest { get; init; }

    public string? AttestedDigest { get; init; }

    public string? RequestedDeploymentDigest { get; init; }

    public string? ObservedRunningDigest { get; init; }

    public OciCommandInfo Verify { get; init; } = new();
}

public sealed record OciCommandInfo
{
    public string? VerifyCommand { get; init; }

    public string? OciReference { get; init; }
}

public sealed record DeploymentInfo
{
    public EvidenceState State { get; init; } = EvidenceState.Unknown;

    public string Health { get; init; } = "unknown";

    public string? Detail { get; init; }

    public DateTimeOffset? ObservedAt { get; init; }

    public DateTimeOffset? MachineCreatedAt { get; init; }

    public string? AppName { get; init; }

    public string? MachineId { get; init; }

    public string? Region { get; init; }

    public string Source { get; init; } = "unknown";

    public string? SourceUrl { get; init; }
}

public sealed record AttestationInfo
{
    public EvidenceState State { get; init; } = EvidenceState.Unknown;

    public string? SubjectDigest { get; init; }

    public string? PredicateType { get; init; }

    public string? Repository { get; init; }

    public string? CommitSha { get; init; }

    public string? WorkflowRef { get; init; }

    public string? RunId { get; init; }

    public DateTimeOffset? ObservedAt { get; init; }

    public string Source { get; init; } = "unknown";

    public string? SourceUrl { get; init; }

    public string? Command { get; init; }

    public string? Note { get; init; }
}

public sealed record SecurityPostureInfo
{
    public string Disclaimer { get; init; } = string.Empty;

    public string ScanScope { get; init; } = string.Empty;

    public IReadOnlyList<string> ImplementedControls { get; init; } = [];

    public IReadOnlyList<string> NotAssessed { get; init; } = [];
}
