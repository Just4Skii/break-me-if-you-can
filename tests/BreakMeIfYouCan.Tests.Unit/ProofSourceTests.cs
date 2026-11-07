using System.Net;
using System.Text;
using System.Text.Json;
using BreakMeIfYouCan.Api.Proof;
using BreakMeIfYouCan.Evidence.Generation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BreakMeIfYouCan.Tests.Unit;

public sealed class ProofSourceTests
{
    private const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    // ---------------------------------------------------------------- Fly probe

    [Fact]
    public async Task FlyProbe_WithoutConfigurationIsUnavailable()
    {
        var probe = new FlyRuntimeProbe(
            new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            Options.Create(new ProofOptions()),
            NullLogger<FlyRuntimeProbe>.Instance);

        var observation = await probe.ProbeAsync(CancellationToken.None);

        Assert.False(observation.Available);
        Assert.Contains("AppName", observation.Failure);
    }

    [Fact]
    public async Task FlyProbe_WithoutTokenIsUnavailable()
    {
        var probe = new FlyRuntimeProbe(
            new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            Options.Create(new ProofOptions { Fly = new FlyProofOptions { AppName = "break-me" } }),
            NullLogger<FlyRuntimeProbe>.Instance);

        var observation = await probe.ProbeAsync(CancellationToken.None);

        Assert.False(observation.Available);
        Assert.Contains("token", observation.Failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FlyProbe_ReadsTheDigestAndHealthFromTheMachinesApi()
    {
        var json = """
            [
              {
                "id": "machine-1",
                "state": "started",
                "region": "jnb",
                "created_at": "2026-11-07T10:00:00Z",
                "config": { "image": "ghcr.io/owner/repo@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" }
              }
            ]
            """;

        var probe = new FlyRuntimeProbe(
            new StubHttpClientFactory(request =>
            {
                Assert.Equal("https://api.machines.dev/v1/apps/break-me/machines", request.RequestUri!.ToString());
                Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
                return JsonResponse(json);
            }),
            Options.Create(new ProofOptions
            {
                Fly = new FlyProofOptions { AppName = "break-me", ApiToken = "fly-token" },
            }),
            NullLogger<FlyRuntimeProbe>.Instance);

        var observation = await probe.ProbeAsync(CancellationToken.None);

        Assert.True(observation.Available);
        Assert.Equal("sha256:" + Sha, observation.Digest);
        Assert.True(observation.Healthy);
        Assert.Equal("jnb", observation.Region);
        Assert.Equal("machine-1", observation.MachineId);
        Assert.Equal("fly-machines-api", observation.Source);
    }

    [Fact]
    public async Task FlyProbe_ReportsATagAsAnUnavailableDigest()
    {
        var json = """[ { "id": "m", "state": "started", "config": { "image": "ghcr.io/owner/repo:latest" } } ]""";

        var probe = new FlyRuntimeProbe(
            new StubHttpClientFactory(_ => JsonResponse(json)),
            Options.Create(new ProofOptions { Fly = new FlyProofOptions { AppName = "break-me", ApiToken = "t" } }),
            NullLogger<FlyRuntimeProbe>.Instance);

        var observation = await probe.ProbeAsync(CancellationToken.None);

        Assert.True(observation.Available);
        Assert.Null(observation.Digest);
        Assert.Contains("tag", observation.Failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FlyProbe_HandlesAnErrorStatus()
    {
        var probe = new FlyRuntimeProbe(
            new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)),
            Options.Create(new ProofOptions { Fly = new FlyProofOptions { AppName = "break-me", ApiToken = "t" } }),
            NullLogger<FlyRuntimeProbe>.Instance);

        var observation = await probe.ProbeAsync(CancellationToken.None);

        Assert.False(observation.Available);
        Assert.Contains("500", observation.Failure);
    }

    [Fact]
    public async Task FlyProbe_SelectsTheConfiguredMachine()
    {
        var json = """
            [
              { "id": "other", "state": "started", "config": { "image": "ghcr.io/o/r@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" } },
              { "id": "wanted", "state": "started", "config": { "image": "ghcr.io/o/r@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" } }
            ]
            """;

        var probe = new FlyRuntimeProbe(
            new StubHttpClientFactory(_ => JsonResponse(json)),
            Options.Create(new ProofOptions
            {
                Fly = new FlyProofOptions { AppName = "app", ApiToken = "t", MachineId = "wanted" },
            }),
            NullLogger<FlyRuntimeProbe>.Instance);

        var observation = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal("wanted", observation.MachineId);
        Assert.Equal("sha256:" + Sha, observation.Digest);
    }

    // ---------------------------------------------------------------- GitHub attestations

    [Fact]
    public async Task AttestationProbe_ReportsAMissingAttestationAsFoundEqualsFalse()
    {
        var options = GitHubOptions();
        var github = new GitHubActionsClient(
            new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.NotFound)),
            options,
            NullLogger<GitHubActionsClient>.Instance);
        var probe = new GitHubAttestationProbe(github, options, NullLogger<GitHubAttestationProbe>.Instance);

        var observation = await probe.ProbeAsync("sha256:" + Sha, CancellationToken.None);

        Assert.True(observation.Available);
        Assert.False(observation.Found);
    }

    [Fact]
    public async Task AttestationProbe_ReportsAnApiErrorAsUnavailable()
    {
        var options = GitHubOptions();
        var github = new GitHubActionsClient(
            new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)),
            options,
            NullLogger<GitHubActionsClient>.Instance);
        var probe = new GitHubAttestationProbe(github, options, NullLogger<GitHubAttestationProbe>.Instance);

        var observation = await probe.ProbeAsync("sha256:" + Sha, CancellationToken.None);

        Assert.False(observation.Available);
    }

    [Fact]
    public async Task AttestationProbe_ParsesTheAttestationBundle()
    {
        var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("""
            {
              "subject": [ { "digest": { "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" } } ],
              "predicateType": "https://slsa.dev/provenance/v1",
              "predicate": {
                "buildDefinition": {
                  "externalParameters": { "workflow": { "repository": "https://github.com/Skinny1ne/break-me-if-you-can", "path": ".github/workflows/ci.yml", "ref": "refs/heads/main" } },
                  "resolvedDependencies": [ { "digest": { "gitCommit": "0123456789abcdef0123456789abcdef01234567" } } ]
                },
                "runDetails": { "metadata": { "invocationId": "https://github.com/Skinny1ne/break-me-if-you-can/actions/runs/1234567890" } }
              }
            }
            """));

        var body = JsonSerializer.Serialize(new
        {
            attestations = new[]
            {
                new { bundle = new { dsseEnvelope = new { payload } } },
            },
        });

        var options = GitHubOptions();
        var github = new GitHubActionsClient(
            new StubHttpClientFactory(_ => JsonResponse(body)),
            options,
            NullLogger<GitHubActionsClient>.Instance);
        var probe = new GitHubAttestationProbe(github, options, NullLogger<GitHubAttestationProbe>.Instance);

        var observation = await probe.ProbeAsync("sha256:" + Sha, CancellationToken.None);

        Assert.True(observation.Available);
        Assert.True(observation.Found);
        Assert.Equal(1u, observation.AttestationCount);
        Assert.Equal("sha256:" + Sha, observation.SubjectDigest);
        Assert.Equal(ProofFixtures.Repository, observation.Repository);
        Assert.Equal(ProofFixtures.Commit, observation.CommitSha);
        Assert.Equal("1234567890", observation.RunId);
    }

    // ---------------------------------------------------------------- deployment intent

    [Fact]
    public void DeploymentIntent_ParsesTheRequestedDigest()
    {
        var intent = DeploymentIntentProvider.Parse("""
            { "requestedDigest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "commitSha": "0123456789abcdef0123456789abcdef01234567", "runId": "99", "deployedAt": "2026-11-07T10:00:00Z" }
            """);

        Assert.NotNull(intent);
        Assert.Equal("sha256:" + Sha, intent.RequestedDigest);
        Assert.Equal("99", intent.RunId);
        Assert.NotNull(intent.DeployedAt);
    }

    [Fact]
    public void DeploymentIntent_ToleratesAlternateFieldNames()
    {
        var intent = DeploymentIntentProvider.Parse("""{ "digest": "sha256:abc", "commit": "c1" }""");

        Assert.NotNull(intent);
        Assert.Equal("sha256:abc", intent.RequestedDigest);
        Assert.Equal("c1", intent.CommitSha);
    }

    // ---------------------------------------------------------------- local evidence

    [Fact]
    public async Task LocalEvidenceProvider_LoadsTheManifestAndLabelsTheSource()
    {
        using var directory = new TestTempDirectory();
        var path = Path.Combine(directory.Path, "ci-evidence.json");
        EvidenceManifestFile.Write(ProofFixtures.Manifest(), path);

        var provider = new CiEvidenceProvider(
            UnconfiguredGitHub(),
            Options.Create(new ProofOptions { LocalEvidencePath = path }),
            NullLogger<CiEvidenceProvider>.Instance);

        var bundle = await provider.GetAsync(ProofFixtures.Commit, CancellationToken.None);

        Assert.NotNull(bundle.Manifest);
        Assert.Equal("local-file", bundle.Source);
        Assert.Null(bundle.Failure);
    }

    [Fact]
    public async Task LocalEvidenceProvider_ReportsAMissingFileAsAFailure()
    {
        var provider = new CiEvidenceProvider(
            UnconfiguredGitHub(),
            Options.Create(new ProofOptions { LocalEvidencePath = "/no/such/file.json" }),
            NullLogger<CiEvidenceProvider>.Instance);

        var bundle = await provider.GetAsync(ProofFixtures.Commit, CancellationToken.None);

        Assert.Null(bundle.Manifest);
        Assert.Contains("not found", bundle.Failure, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EvidenceProvider_WithoutAnyConfigurationExplainsItself()
    {
        var provider = new CiEvidenceProvider(
            UnconfiguredGitHub(),
            Options.Create(new ProofOptions()),
            NullLogger<CiEvidenceProvider>.Instance);

        var bundle = await provider.GetAsync(ProofFixtures.Commit, CancellationToken.None);

        Assert.Null(bundle.Manifest);
        Assert.Contains("Repository", bundle.Failure);
    }

    private static IOptions<ProofOptions> GitHubOptions() =>
        Options.Create(new ProofOptions { Repository = ProofFixtures.Repository });

    private static GitHubActionsClient UnconfiguredGitHub() =>
        new(
            new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            Options.Create(new ProofOptions()),
            NullLogger<GitHubActionsClient>.Instance);

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };
}
