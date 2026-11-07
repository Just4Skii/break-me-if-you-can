namespace BreakMeIfYouCan.Evidence.Manifest;

/// <summary>Outcome recorded by the CI pipeline for one stage.</summary>
public enum PipelineStatus
{
    Unknown = 0,
    Success = 1,
    Failure = 2,
    Missing = 3,
}

/// <summary>Outcome of the NuGet vulnerability audit.</summary>
public enum AuditStatus
{
    Unknown = 0,
    Clean = 1,
    Vulnerable = 2,
    Failed = 3,
    Missing = 4,
}

/// <summary>
/// The canonical, machine-generated record of one CI workflow run. Everything in this
/// document is produced from build outputs, not typed by hand, and the shipping
/// application treats every field as unverified input until it is cross-checked.
/// </summary>
public sealed record CiEvidenceManifest
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Repository in owner/name form.</summary>
    public string Repository { get; init; } = string.Empty;

    /// <summary>Full 40 character commit SHA that produced the image.</summary>
    public string CommitSha { get; init; } = string.Empty;

    public string Branch { get; init; } = string.Empty;

    public DateTimeOffset GeneratedAt { get; init; }

    public WorkflowEvidence Workflow { get; init; } = new();

    public BuildEvidence Build { get; init; } = new();

    public TestEvidence Tests { get; init; } = new();

    public CoverageEvidence Coverage { get; init; } = new();

    public DependencyAuditEvidence DependencyAudit { get; init; } = new();

    public ImageEvidence Image { get; init; } = new();

    public AttestationEvidence Attestations { get; init; } = new();

    public SecurityPostureEvidence SecurityPosture { get; init; } = new();

    public IReadOnlyList<EvidenceArtifact> Artifacts { get; init; } = [];
}

public sealed record WorkflowEvidence
{
    public string Name { get; init; } = string.Empty;

    /// <summary>Path of the workflow file, for example .github/workflows/ci.yml.</summary>
    public string File { get; init; } = string.Empty;

    public string RunId { get; init; } = string.Empty;

    public string RunAttempt { get; init; } = string.Empty;

    public string RunUrl { get; init; } = string.Empty;

    /// <summary>The GitHub event that triggered the run (push, pull_request, workflow_dispatch).</summary>
    public string Event { get; init; } = string.Empty;

    public string Actor { get; init; } = string.Empty;

    public PipelineStatus Conclusion { get; init; } = PipelineStatus.Missing;
}

public sealed record BuildEvidence
{
    public PipelineStatus Status { get; init; } = PipelineStatus.Missing;

    public string Configuration { get; init; } = string.Empty;

    public string Framework { get; init; } = string.Empty;

    public string DotnetVersion { get; init; } = string.Empty;

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public string Runner { get; init; } = string.Empty;
}

public sealed record TestEvidence
{
    public PipelineStatus Status { get; init; } = PipelineStatus.Missing;

    public int Total { get; init; }

    public int Passed { get; init; }

    public int Failed { get; init; }

    public int Skipped { get; init; }

    public double DurationSeconds { get; init; }

    public IReadOnlyList<TestAssemblyEvidence> Assemblies { get; init; } = [];

    /// <summary>Names of the TRX report files that were parsed.</summary>
    public IReadOnlyList<string> Reports { get; init; } = [];

    /// <summary>Test projects whose TRX report was expected but absent.</summary>
    public IReadOnlyList<string> MissingReports { get; init; } = [];
}

public sealed record TestAssemblyEvidence
{
    public string Assembly { get; init; } = string.Empty;

    public int Total { get; init; }

    public int Passed { get; init; }

    public int Failed { get; init; }

    public int Skipped { get; init; }

    public double DurationSeconds { get; init; }
}

public sealed record CoverageEvidence
{
    public double? LinePercent { get; init; }

    public double? BranchPercent { get; init; }

    public double ThresholdPercent { get; init; }

    public bool MeetsThreshold { get; init; }

    public string? ReportArtifact { get; init; }

    public int? LinesCovered { get; init; }

    public int? LinesValid { get; init; }
}

public sealed record DependencyAuditEvidence
{
    public AuditStatus Status { get; init; } = AuditStatus.Missing;

    public int DirectCount { get; init; }

    public int TransitiveCount { get; init; }

    public IReadOnlyDictionary<string, int> BySeverity { get; init; } = new Dictionary<string, int>();

    public IReadOnlyList<VulnerablePackage> Packages { get; init; } = [];

    public string Command { get; init; } = string.Empty;

    public string Note { get; init; } = string.Empty;
}

public sealed record VulnerablePackage
{
    public string Id { get; init; } = string.Empty;

    public string Version { get; init; } = string.Empty;

    public string Severity { get; init; } = string.Empty;

    public string? AdvisoryUrl { get; init; }

    public bool Transitive { get; init; }
}

public sealed record ImageEvidence
{
    /// <summary>Repository reference without a digest, for example ghcr.io/owner/repo.</summary>
    public string Reference { get; init; } = string.Empty;

    /// <summary>Immutable digest reference, for example sha256:abc...</summary>
    public string? Digest { get; init; }

    public DateTimeOffset? BuiltAt { get; init; }

    public string Registry { get; init; } = string.Empty;
}

public sealed record AttestationEvidence
{
    public AttestationRecord BuildProvenance { get; init; } = new();

    public AttestationRecord QualityEvidence { get; init; } = new();
}

public sealed record AttestationRecord
{
    public PipelineStatus Status { get; init; } = PipelineStatus.Missing;

    public string? PredicateType { get; init; }

    public string? SubjectDigest { get; init; }

    public string? Repository { get; init; }

    public string? CommitSha { get; init; }

    public string? WorkflowRef { get; init; }

    public string? RunId { get; init; }

    public DateTimeOffset? VerifiedAt { get; init; }

    public string Verifier { get; init; } = string.Empty;

    public string Command { get; init; } = string.Empty;

    public int ExitCode { get; init; }
}

public sealed record SecurityPostureEvidence
{
    /// <summary>
    /// Honest framing: a clean dependency scan covers known advisories in NuGet packages only.
    /// It is not a penetration test, threat model or full security assessment.
    /// </summary>
    public string Disclaimer { get; init; } = string.Empty;

    public IReadOnlyList<string> ImplementedControls { get; init; } = [];

    public IReadOnlyList<string> NotAssessed { get; init; } = [];
}

public sealed record EvidenceArtifact
{
    public string Name { get; init; } = string.Empty;

    public string? Sha256 { get; init; }
}
