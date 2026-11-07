using System.Text.Json;
using BreakMeIfYouCan.Evidence.Generation;
using BreakMeIfYouCan.Evidence.Manifest;
using Microsoft.Extensions.Options;

namespace BreakMeIfYouCan.Api.Proof;

public sealed record EvidenceBundle(
    CiEvidenceManifest? Manifest,
    string Source,
    DateTimeOffset FetchedAt,
    string? Failure);

public interface ICiEvidenceProvider
{
    Task<EvidenceBundle> GetAsync(string commitSha, CancellationToken cancellationToken);
}

public sealed record DeploymentIntent(
    string? RequestedDigest,
    string? CommitSha,
    string? RunId,
    DateTimeOffset? DeployedAt);

public interface IDeploymentIntentProvider
{
    Task<DeploymentIntent?> GetAsync(string commitSha, CancellationToken cancellationToken);
}

/// <summary>
/// Loads the canonical evidence manifest produced by the CI run for the running commit.
/// It never falls back to "the most recent run": the run is discovered by commit and the
/// manifest must agree with the commit and the run id, so evidence cannot cross workflow
/// boundaries.
/// </summary>
public sealed class CiEvidenceProvider(
    GitHubActionsClient github,
    IOptions<ProofOptions> options,
    ILogger<CiEvidenceProvider> logger) : ICiEvidenceProvider
{
    private readonly ProofOptions _options = options.Value;

    public async Task<EvidenceBundle> GetAsync(string commitSha, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        if (!string.IsNullOrWhiteSpace(_options.LocalEvidencePath))
        {
            return LoadLocal(now);
        }

        if (!github.IsConfigured)
        {
            return new EvidenceBundle(null, "none", now, "Proof:Repository is not configured for this deployment.");
        }

        try
        {
            var run = await github.FindSuccessfulRunAsync(commitSha, cancellationToken);
            if (run is null)
            {
                return new EvidenceBundle(
                    null,
                    "github-actions-artifact",
                    now,
                    "No successful " + _options.WorkflowFile + " run exists for commit " + Short(commitSha) + ".");
            }

            var json = await github.DownloadArtifactFileAsync(run.Id, "ci-evidence", "ci-evidence.json", cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new EvidenceBundle(
                    null,
                    "github-actions-artifact",
                    now,
                    "The ci-evidence artifact for run " + run.Id + " is missing or expired.");
            }

            var manifest = EvidenceJson.Deserialize(json);
            if (manifest is null)
            {
                return new EvidenceBundle(null, "github-actions-artifact", now, "The ci-evidence manifest could not be parsed.");
            }

            if (!string.Equals(manifest.CommitSha, commitSha, StringComparison.OrdinalIgnoreCase))
            {
                return new EvidenceBundle(
                    null,
                    "github-actions-artifact",
                    now,
                    "The manifest is bound to commit " + Short(manifest.CommitSha) + ", not the running commit.");
            }

            if (!string.Equals(manifest.Workflow.RunId, run.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                return new EvidenceBundle(
                    null,
                    "github-actions-artifact",
                    now,
                    "The manifest records run " + manifest.Workflow.RunId + " but was fetched from run " + run.Id + ".");
            }

            return new EvidenceBundle(manifest, "github-actions-artifact", now, null);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {
            logger.LogWarning(exception, "The CI evidence could not be retrieved.");
            return new EvidenceBundle(
                null,
                "github-actions-artifact",
                now,
                "The CI evidence could not be retrieved: " + exception.Message);
        }
    }

    private EvidenceBundle LoadLocal(DateTimeOffset now)
    {
        var path = _options.LocalEvidencePath!;
        if (!File.Exists(path))
        {
            return new EvidenceBundle(null, "local-file", now, "Local evidence file not found.");
        }

        try
        {
            var manifest = EvidenceManifestFile.Read(path);
            return new EvidenceBundle(manifest, "local-file", now, null);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return new EvidenceBundle(null, "local-file", now, "Local evidence could not be read: " + exception.Message);
        }
    }

    private static string Short(string? value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value.Length > 12 ? value[..12] : value;
}

/// <summary>
/// Reads the deployment intent recorded by the deploy job. This is not the running image:
/// it is what the deployment asked the platform to run, and it is compared against the
/// digest the platform reports at runtime.
/// </summary>
public sealed class DeploymentIntentProvider(
    GitHubActionsClient github,
    IOptions<ProofOptions> options,
    ILogger<DeploymentIntentProvider> logger) : IDeploymentIntentProvider
{
    private readonly ProofOptions _options = options.Value;

    public async Task<DeploymentIntent?> GetAsync(string commitSha, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.LocalDeploymentIntentPath))
        {
            return ParseFile(_options.LocalDeploymentIntentPath!);
        }

        var localSibling = string.IsNullOrWhiteSpace(_options.LocalEvidencePath)
            ? null
            : Path.Combine(Path.GetDirectoryName(_options.LocalEvidencePath)!, "deploy-intent.json");

        if (localSibling is not null && File.Exists(localSibling))
        {
            return ParseFile(localSibling);
        }

        if (!github.IsConfigured)
        {
            return null;
        }

        try
        {
            var run = await github.FindSuccessfulRunAsync(commitSha, cancellationToken);
            if (run is null)
            {
                return null;
            }

            var json = await github.DownloadArtifactFileAsync(run.Id, "deployment-intent", "deploy-intent.json", cancellationToken);
            return string.IsNullOrWhiteSpace(json) ? null : Parse(json);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "The deployment intent could not be retrieved.");
            return null;
        }
    }

    private static DeploymentIntent? ParseFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    public static DeploymentIntent? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var digest = ReadString(root, "requestedDigest") ?? ReadString(root, "digest") ?? ReadString(root, "imageDigest");
        var commit = ReadString(root, "commitSha") ?? ReadString(root, "commit");
        var runId = ReadString(root, "runId");
        DateTimeOffset? deployedAt = null;

        if (root.TryGetProperty("deployedAt", out var deployed) && deployed.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(deployed.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
        {
            deployedAt = parsed;
        }

        return new DeploymentIntent(digest, commit, runId, deployedAt);
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
