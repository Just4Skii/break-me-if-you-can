using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BreakMeIfYouCan.Api.Proof;

/// <summary>The facts a GitHub artifact attestation states about an image.</summary>
public sealed record AttestationPayload(
    string? SubjectDigest,
    string? PredicateType,
    string? Repository,
    string? CommitSha,
    string? WorkflowRef,
    string? RunId);

/// <summary>
/// Parses the DSSE envelope returned by GitHub's attestations API. The payload is an
/// in-toto statement; field names differ between SLSA v0.2 and v1, so both are handled
/// and anything unrecognised is reported as missing rather than guessed.
/// </summary>
public static partial class AttestationPayloadParser
{
    public static AttestationPayload? Parse(string? base64Payload)
    {
        if (string.IsNullOrWhiteSpace(base64Payload))
        {
            return null;
        }

        byte[] payloadBytes;
        try
        {
            payloadBytes = Convert.FromBase64String(base64Payload);
        }
        catch (FormatException)
        {
            return null;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(Encoding.UTF8.GetString(payloadBytes));
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            var subjectDigest = ReadSubjectDigest(root);
            var predicateType = ReadString(root, "predicateType");

            if (!root.TryGetProperty("predicate", out var predicate) || predicate.ValueKind != JsonValueKind.Object)
            {
                return new AttestationPayload(subjectDigest, predicateType, null, null, null, null);
            }

            string? repository = null;
            string? commit = null;
            string? workflowRef = null;
            string? runId = null;

            // SLSA v1 shape.
            if (predicate.TryGetProperty("buildDefinition", out var buildDefinition))
            {
                if (buildDefinition.TryGetProperty("externalParameters", out var external) &&
                    external.TryGetProperty("workflow", out var workflow))
                {
                    var statedRepository = ReadString(workflow, "repository");
                    repository = statedRepository is null ? null : ExtractRepository(statedRepository) ?? statedRepository;
                    var path = ReadString(workflow, "path");
                    var @ref = ReadString(workflow, "ref");

                    if (!string.IsNullOrWhiteSpace(repository) && !string.IsNullOrWhiteSpace(path))
                    {
                        workflowRef = @ref is null ? repository + "/" + path : repository + "/" + path + "@" + @ref;
                    }
                }

                if (buildDefinition.TryGetProperty("resolvedDependencies", out var dependencies) &&
                    dependencies.ValueKind == JsonValueKind.Array)
                {
                    foreach (var dependency in dependencies.EnumerateArray())
                    {
                        if (dependency.TryGetProperty("digest", out var digest))
                        {
                            commit ??= ReadString(digest, "gitCommit") ?? ReadString(digest, "sha1");
                        }
                    }
                }
            }

            if (predicate.TryGetProperty("runDetails", out var runDetails) &&
                runDetails.TryGetProperty("metadata", out var metadata) &&
                metadata.TryGetProperty("invocationId", out var invocationId) &&
                invocationId.ValueKind == JsonValueKind.String)
            {
                runId = ExtractRunId(invocationId.GetString());
            }

            // SLSA v0.2 shape.
            if (repository is null && predicate.TryGetProperty("invocation", out var invocation))
            {
                if (invocation.TryGetProperty("configSource", out var configSource) &&
                    configSource.TryGetProperty("uri", out var uri) &&
                    uri.ValueKind == JsonValueKind.String)
                {
                    repository = ExtractRepository(uri.GetString());
                }

                if (invocation.TryGetProperty("environment", out var environment))
                {
                    commit ??= ReadString(environment, "github_sha");
                    repository ??= ReadString(environment, "github_repository");
                    runId ??= ReadString(environment, "github_run_id");
                }
            }

            // Custom predicates written by this project.
            repository ??= ReadString(predicate, "repository");
            commit ??= ReadString(predicate, "commitSha") ?? ReadString(predicate, "commit");
            runId ??= ReadString(predicate, "runId");
            workflowRef ??= ReadString(predicate, "workflowRef");

            return new AttestationPayload(subjectDigest, predicateType, repository, commit, workflowRef, runId);
        }
    }

    public static string? ExtractRunId(string? invocationId)
    {
        if (string.IsNullOrWhiteSpace(invocationId))
        {
            return null;
        }

        var match = RunIdPattern().Match(invocationId);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ReadSubjectDigest(JsonElement root)
    {
        if (!root.TryGetProperty("subject", out var subjects) || subjects.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var subject in subjects.EnumerateArray())
        {
            if (subject.TryGetProperty("digest", out var digest))
            {
                var value = ReadString(digest, "sha256");
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return "sha256:" + value;
                }
            }
        }

        return null;
    }

    private static string? ExtractRepository(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return null;
        }

        var match = RepositoryPattern().Match(uri);
        return match.Success ? match.Groups[1].Value + "/" + match.Groups[2].Value : null;
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    [GeneratedRegex("/runs/([0-9]+)", RegexOptions.CultureInvariant)]
    private static partial Regex RunIdPattern();

    [GeneratedRegex(@"github\.com[/:]([A-Za-z0-9_.-]+)/([A-Za-z0-9_.-]+)", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPattern();
}
