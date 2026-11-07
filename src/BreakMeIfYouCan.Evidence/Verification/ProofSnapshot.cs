using BreakMeIfYouCan.Evidence.Manifest;

namespace BreakMeIfYouCan.Evidence.Verification;

/// <summary>
/// Everything the verifier is allowed to reason about. Nothing here is assumed by the
/// verifier itself: each value is either read from the CI artifact or from an
/// independent source such as the Fly Machines API or the GitHub attestations API.
/// </summary>
public sealed record ProofSnapshot
{
    public CiEvidenceManifest? Evidence { get; init; }

    /// <summary>Human readable origin of the manifest, for example "github-actions-artifact".</summary>
    public string EvidenceSource { get; init; } = "none";

    public DateTimeOffset? EvidenceFetchedAt { get; init; }

    /// <summary>Repository the deployment is configured to trust.</summary>
    public string? ExpectedRepository { get; init; }

    /// <summary>Workflow file the deployment is configured to trust.</summary>
    public string? ExpectedWorkflowFile { get; init; }

    /// <summary>Commit compiled into the currently running assembly.</summary>
    public string? RunningCommitSha { get; init; }

    /// <summary>Digest the deploy job asked the platform to run, from a deployment record.</summary>
    public string? RequestedDeploymentDigest { get; init; }

    /// <summary>What the platform observes running right now.</summary>
    public RuntimeObservation? Runtime { get; init; }

    /// <summary>What GitHub reports for the image digest, queried at request time.</summary>
    public ArtifactAttestationObservation? Attestation { get; init; }

    public double CoverageThresholdPercent { get; init; } = 75;

    public TimeSpan MaxEvidenceAge { get; init; } = TimeSpan.FromHours(24);

    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Diagnostics from the evidence sources, surfaced so an UNKNOWN is explainable.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

public sealed record RuntimeObservation
{
    public bool Available { get; init; }

    /// <summary>Digest of the image the platform reports as running.</summary>
    public string? Digest { get; init; }

    public string? ImageReference { get; init; }

    public string? AppName { get; init; }

    public string? MachineId { get; init; }

    public string? Region { get; init; }

    public bool? Healthy { get; init; }

    public string? HealthDetail { get; init; }

    public DateTimeOffset? MachineCreatedAt { get; init; }

    public DateTimeOffset? ObservedAt { get; init; }

    /// <summary>Where the observation came from, for example "fly-machines-api".</summary>
    public string Source { get; init; } = "unknown";

    public string? Failure { get; init; }

    public string? SourceUrl { get; init; }
}

public sealed record ArtifactAttestationObservation
{
    public bool Available { get; init; }

    public bool Found { get; init; }

    public uint? AttestationCount { get; init; }

    public string? SubjectDigest { get; init; }

    public string? PredicateType { get; init; }

    public string? Repository { get; init; }

    public string? CommitSha { get; init; }

    public string? WorkflowRef { get; init; }

    public string? RunId { get; init; }

    public DateTimeOffset? ObservedAt { get; init; }

    public string Source { get; init; } = "unknown";

    public string? SourceUrl { get; init; }

    public string? Failure { get; init; }
}