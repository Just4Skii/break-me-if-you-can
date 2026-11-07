namespace BreakMeIfYouCan.Api.Proof;

public sealed class ProofOptions
{
    public const string SectionName = "Proof";

    /// <summary>Repository in owner/name form that the deployment trusts.</summary>
    public string Repository { get; set; } = string.Empty;

    /// <summary>The only workflow allowed to produce release evidence.</summary>
    public string WorkflowFile { get; set; } = ".github/workflows/ci.yml";

    public double CoverageThresholdPercent { get; set; } = 75;

    /// <summary>Evidence older than this is UNKNOWN, never verified.</summary>
    public int MaxEvidenceAgeHours { get; set; } = 24;

    public int CacheSeconds { get; set; } = 60;

    /// <summary>
    /// Read evidence from a local file instead of GitHub. Used for local development and
    /// integration tests; the report labels the source so it is never mistaken for CI.
    /// </summary>
    public string? LocalEvidencePath { get; set; }

    public string? LocalDeploymentIntentPath { get; set; }

    public string GitHubApiBaseUrl { get; set; } = "https://api.github.com";

    /// <summary>Read-only token with Actions:read. Never logged; supplied as a secret.</summary>
    public string? GitHubToken { get; set; }

    public FlyProofOptions Fly { get; set; } = new();

    public string? RepositoryUrl =>
        string.IsNullOrWhiteSpace(Repository) ? null : "https://github.com/" + Repository;
}

public sealed class FlyProofOptions
{
    public string AppName { get; set; } = string.Empty;

    /// <summary>Optional machine to inspect; otherwise the first started machine is used.</summary>
    public string? MachineId { get; set; }

    /// <summary>Fly organisation token with read access. Supplied as a secret.</summary>
    public string? ApiToken { get; set; }

    public string ApiBaseUrl { get; set; } = "https://api.machines.dev";

    public int TimeoutSeconds { get; set; } = 5;
}
