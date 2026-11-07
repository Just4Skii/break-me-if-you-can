using System.Text.Json;
using BreakMeIfYouCan.Evidence.Verification;
using Microsoft.Extensions.Options;

namespace BreakMeIfYouCan.Api.Proof;

public interface IArtifactAttestationProbe
{
    Task<ArtifactAttestationObservation> ProbeAsync(string? digest, CancellationToken cancellationToken);
}

/// <summary>
/// Queries GitHub's attestations API at request time. This is independent of anything the
/// CI pipeline wrote into its own manifest: if GitHub has no attestation for the digest,
/// the release is not verified, regardless of what the manifest claims.
/// </summary>
public sealed class GitHubAttestationProbe(
    GitHubActionsClient github,
    IOptions<ProofOptions> options,
    ILogger<GitHubAttestationProbe> logger) : IArtifactAttestationProbe
{
    private readonly ProofOptions _options = options.Value;

    public async Task<ArtifactAttestationObservation> ProbeAsync(string? digest, CancellationToken cancellationToken)
    {
        if (!github.IsConfigured)
        {
            return Unavailable("Proof:Repository is not configured.");
        }

        if (string.IsNullOrWhiteSpace(digest))
        {
            return Unavailable("No attested image digest is available to query.");
        }

        try
        {
            var result = await github.GetAttestationsAsync(digest, cancellationToken);
            if (!result.Success)
            {
                return Unavailable("The GitHub attestations API request failed.");
            }

            if (result.Document is null)
            {
                return new ArtifactAttestationObservation
                {
                    Available = true,
                    Found = false,
                    ObservedAt = DateTimeOffset.UtcNow,
                    Source = "github-attestations-api",
                    SourceUrl = AttestationUrl(digest),
                };
            }

            using var document = result.Document;
            if (!document.RootElement.TryGetProperty("attestations", out var attestations) || attestations.ValueKind != JsonValueKind.Array)
            {
                return new ArtifactAttestationObservation
                {
                    Available = true,
                    Found = false,
                    ObservedAt = DateTimeOffset.UtcNow,
                    Source = "github-attestations-api",
                    SourceUrl = AttestationUrl(digest),
                };
            }

            var count = 0u;
            AttestationPayload? selected = null;

            foreach (var attestation in attestations.EnumerateArray())
            {
                count++;

                if (!attestation.TryGetProperty("bundle", out var bundle) ||
                    !bundle.TryGetProperty("dsseEnvelope", out var envelope) ||
                    !envelope.TryGetProperty("payload", out var payload) ||
                    payload.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var parsed = AttestationPayloadParser.Parse(payload.GetString());
                if (parsed is null)
                {
                    continue;
                }

                // Prefer SLSA build provenance when the repository publishes several statements.
                if (selected is null ||
                    (parsed.PredicateType?.Contains("slsa.dev/provenance", StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    selected = parsed;
                }
            }

            if (selected is null)
            {
                return Unavailable("GitHub returned attestations, but none could be parsed.");
            }

            return new ArtifactAttestationObservation
            {
                Available = true,
                Found = true,
                AttestationCount = count,
                SubjectDigest = selected.SubjectDigest,
                PredicateType = selected.PredicateType,
                Repository = selected.Repository,
                CommitSha = selected.CommitSha,
                WorkflowRef = selected.WorkflowRef,
                RunId = selected.RunId,
                ObservedAt = DateTimeOffset.UtcNow,
                Source = "github-attestations-api",
                SourceUrl = AttestationUrl(digest),
            };
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogWarning(exception, "The GitHub attestations API could not be queried.");
            return Unavailable("The GitHub attestations API could not be queried: " + exception.Message);
        }
    }

    private string AttestationUrl(string digest) =>
        _options.GitHubApiBaseUrl.TrimEnd('/') + "/repos/" + _options.Repository + "/attestations/" + digest;

    private ArtifactAttestationObservation Unavailable(string failure) => new()
    {
        Available = false,
        Failure = failure,
        ObservedAt = null,
        Source = "github-attestations-api",
    };
}
