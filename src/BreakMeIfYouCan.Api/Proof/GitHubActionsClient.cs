using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace BreakMeIfYouCan.Api.Proof;

public sealed record GitHubWorkflowRun(
    long Id,
    string Name,
    string Path,
    string Event,
    string Conclusion,
    string HeadSha,
    string RunAttempt,
    string HtmlUrl,
    string HeadBranch);

/// <summary>
/// Minimal GitHub Actions REST client. It only reads what the proof needs: the successful
/// run for a commit, the artifacts it produced, and the attestations for a digest.
/// </summary>
public sealed class GitHubActionsClient(IHttpClientFactory httpClientFactory, IOptions<ProofOptions> options, ILogger<GitHubActionsClient> logger)
{
    public const string HttpClientName = "github";

    private readonly ProofOptions _options = options.Value;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Repository);

    public async Task<GitHubWorkflowRun?> FindSuccessfulRunAsync(string commitSha, CancellationToken cancellationToken)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(commitSha) || commitSha.Length != 40)
        {
            return null;
        }

        var uri = new Uri(
            _options.GitHubApiBaseUrl.TrimEnd('/') +
            "/repos/" + _options.Repository + "/actions/runs?head_sha=" + commitSha + "&per_page=30");

        using var document = await GetJsonAsync(uri, cancellationToken);
        if (document is null || !document.RootElement.TryGetProperty("workflow_runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var run in runs.EnumerateArray())
        {
            var path = ReadString(run, "path");
            var conclusion = ReadString(run, "conclusion");
            var @event = ReadString(run, "event");

            if (!string.Equals(path, _options.WorkflowFile, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.Equals(conclusion, "success", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!IsReleaseEvent(@event))
            {
                continue;
            }

            return new GitHubWorkflowRun(
                ReadLong(run, "id"),
                ReadString(run, "name"),
                path,
                @event,
                conclusion,
                ReadString(run, "head_sha"),
                ReadString(run, "run_attempt"),
                ReadString(run, "html_url"),
                ReadString(run, "head_branch"));
        }

        logger.LogInformation("No successful {Workflow} run was found for commit {Commit}.", _options.WorkflowFile, commitSha);
        return null;
    }

    public async Task<string?> DownloadArtifactFileAsync(long runId, string artifactName, string fileName, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return null;
        }

        var artifactId = await FindArtifactIdAsync(runId, artifactName, cancellationToken);
        if (artifactId is null)
        {
            return null;
        }

        var uri = new Uri(
            _options.GitHubApiBaseUrl.TrimEnd('/') +
            "/repos/" + _options.Repository + "/actions/artifacts/" + artifactId.Value + "/zip");

        using var client = CreateClient();
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            logger.LogWarning("GitHub refused the evidence artifact download ({Status}).", (int)response.StatusCode);
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var entry = archive.Entries.FirstOrDefault(e =>
            string.Equals(Path.GetFileName(e.FullName), fileName, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            return null;
        }

        await using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    public async Task<GitHubJsonResult> GetAttestationsAsync(string digest, CancellationToken cancellationToken)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(digest))
        {
            return new GitHubJsonResult(false, null);
        }

        var uri = new Uri(
            _options.GitHubApiBaseUrl.TrimEnd('/') +
            "/repos/" + _options.Repository + "/attestations/" + Uri.EscapeDataString(digest));

        return await GetJsonResultAsync(uri, cancellationToken);
    }

    private async Task<long?> FindArtifactIdAsync(long runId, string artifactName, CancellationToken cancellationToken)
    {
        var uri = new Uri(
            _options.GitHubApiBaseUrl.TrimEnd('/') +
            "/repos/" + _options.Repository + "/actions/runs/" + runId + "/artifacts");

        using var document = await GetJsonAsync(uri, cancellationToken);
        if (document is null || !document.RootElement.TryGetProperty("artifacts", out var artifacts) || artifacts.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var artifact in artifacts.EnumerateArray())
        {
            if (!string.Equals(ReadString(artifact, "name"), artifactName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (artifact.TryGetProperty("expired", out var expired) && expired.ValueKind == JsonValueKind.True)
            {
                logger.LogWarning("Artifact {Artifact} for run {RunId} has expired.", artifactName, runId);
                return null;
            }

            return ReadLong(artifact, "id");
        }

        return null;
    }

    private async Task<GitHubJsonResult> GetJsonResultAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new GitHubJsonResult(true, null);
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("GitHub API request failed with status {Status} for {Path}.", (int)response.StatusCode, uri.AbsolutePath);
            return new GitHubJsonResult(false, null);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return new GitHubJsonResult(true, await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken));
    }

    private async Task<JsonDocument?> GetJsonAsync(Uri uri, CancellationToken cancellationToken, bool allowNotFound = false)
    {
        using var client = CreateClient();
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("GitHub API request failed with status {Status} for {Path}.", (int)response.StatusCode, uri.AbsolutePath);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private HttpClient CreateClient()
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("break-me-if-you-can-proof/1.0");

        if (!string.IsNullOrWhiteSpace(_options.GitHubToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.GitHubToken);
        }

        return client;
    }

    private static bool IsReleaseEvent(string value) =>
        value.Equals("push", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("workflow_dispatch", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("release", StringComparison.OrdinalIgnoreCase);

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static long ReadLong(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;
}
