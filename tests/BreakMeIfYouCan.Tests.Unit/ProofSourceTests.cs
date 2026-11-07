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

    private const string OtherSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

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
    public async Task FlyProbe_UsesImageRefDigestAndNeverConfigImage()
    {
        // config.image deliberately points at a different image and digest.
        var list = MachineList(MachineJson(
            "m1",
            "started",
            "sha256:" + Sha,
            configImage: "ghcr.io/someone/else@sha256:" + OtherSha));

        var requests = new List<string>();
        var probe = CreateFlyProbe(request =>
        {
            requests.Add(request.RequestUri!.ToString());
            return JsonResponse(list);
        });

        var observation = await probe.ProbeAsync(CancellationToken.None);

        Assert.True(observation.Available);
        Assert.Equal("sha256:" + Sha, observation.Digest);
        Assert.NotEqual(OtherSha, observation.Digest);
        Assert.Equal("ghcr.io", observation.Registry);
        Assert.Equal("just4skii/break-me-if-you-can", observation.Repository);
        Assert.Equal("ghcr.io/just4skii/break-me-if-you-can:sha-abc@sha256:" + Sha, observation.ImageReference);
        Assert.Null(observation.Failure);
        Assert.Equal(1, observation.MachineCount);
        Assert.Single(requests);
    }

    [Fact]
    public async Task FlyProbe_MissingImageRefIsUnknownEvenWithConfigImage()
    {
        var list = MachineList(MachineJson(
            "m1",
            "started",
            null,
            includeImageRef: false,
            configImage: "ghcr.io/just4skii/break-me-if-you-can@sha256:" + Sha));

        var probe = CreateFlyProbe(request => IsListRequest(request)
            ? JsonResponse(list)
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        var observation = await probe.ProbeAsync(CancellationToken.None);

        Assert.True(observation.Available);
        Assert.Null(observation.Digest);
        Assert.Null(observation.ImageReference);
        Assert.Contains("image_ref.digest", observation.Failure);
    }

    [Fact]
    public async Task FlyProbe_MissingDigestIsUnknown()
    {
        var list = MachineList(MachineJson(
            "m1",
            "started",
            null,
            configImage: "ghcr.io/just4skii/break-me-if-you-can@sha256:" + Sha));

        var probe = CreateFlyProbe(request => IsListRequest(request)
            ? JsonResponse(list)
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        var observation = await probe.ProbeAsync(CancellationToken.None);

        Assert.True(observation.Available);
        Assert.Null(observation.Digest);
        Assert.Contains("image_ref.digest", observation.Failure);
    }

    [Fact]
    public async Task FlyProbe_EmptyDigestIsRefetchedFromTheSingleMachineEndpoint()
    {
        var list = MachineList(MachineJson("m1", "started", string.Empty));
        var refreshed = MachineJson("m1", "started", "sha256:" + Sha);
        var singleCalls = 0;

        var probe = CreateFlyProbe(request =>
        {
            if (IsListRequest(request))
            {
                return JsonResponse(list);
            }

            singleCalls++;
            return JsonResponse(refreshed);
        });

        var observation = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal("sha256:" + Sha, observation.Digest);
        Assert.Null(observation.Failure);
        Assert.Equal(1, singleCalls);
    }

    [Fact]
    public async Task FlyProbe_EmptyDigestAfterRetriesIsUnknown()
    {
        var list = MachineList(MachineJson("m1", "started", string.Empty));
        var singleCalls = 0;

        var probe = CreateFlyProbe(request =>
        {
            if (IsListRequest(request))
            {
                return JsonResponse(list);
            }

            singleCalls++;
            return JsonResponse(MachineJson("m1", "started", string.Empty));
        });

        var observation = await probe.ProbeAsync(CancellationToken.None);

        Assert.True(observation.Available);
        Assert.Null(observation.Digest);
        Assert.Equal(2, singleCalls);
        Assert.Contains("image_ref.digest", observation.Failure);
    }

    [Fact]
    public async Task FlyProbe_ConfiguredMachineNotFoundIsUnavailableAndDoesNotFallBack()
    {
        var list = MachineList(MachineJson("m1", "started", "sha256:" + Sha));
        var requests = 0;

        var probe = CreateFlyProbe(
            request =>
            {
                requests++;
                return IsListRequest(request) ? JsonResponse(list) : new HttpResponseMessage(HttpStatusCode.NotFound);
            },
            machineId: "wanted");

        var observation = await probe.ProbeAsync(CancellationToken.None);

        Assert.False(observation.Available);
        Assert.Contains("wanted", observation.Failure);
        Assert.Equal(1, requests);
    }

    [Fact]
    public async Task FlyProbe_MultipleStartedMachinesWithDifferentDigestsIsUnknown()
    {
        var list = MachineList(
            MachineJson("m1", "started", "sha256:" + Sha),
            MachineJson("m2", "started", "sha256:" + OtherSha));

        var probe = CreateFlyProbe(request => IsListRequest(request)
            ? JsonResponse(list)
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        var observation = await probe.ProbeAsync(CancellationToken.None);

        Assert.True(observation.Available);
        Assert.Null(observation.Digest);
        Assert.Equal(2, observation.MachineCount);
        Assert.Contains("different image digests", observation.Failure);
    }

    [Fact]
    public async Task FlyProbe_MultipleStartedMachinesWithTheSameDigestIsObserved()
    {
        var list = MachineList(
            MachineJson("m1", "started", "sha256:" + Sha),
            MachineJson("m2", "started", "sha256:" + Sha));

        var probe = CreateFlyProbe(request => IsListRequest(request)
            ? JsonResponse(list)
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        var observation = await probe.ProbeAsync(CancellationToken.None);

        Assert.Equal("sha256:" + Sha, observation.Digest);
        Assert.Equal(2, observation.MachineCount);
        Assert.Null(observation.MachineId);
        Assert.True(observation.Healthy);
    }

    [Fact]
    public async Task FlyProbe_NoStartedMachineIsUnavailable()
    {
        var list = MachineList(MachineJson("m1", "stopped", "sha256:" + Sha));

        var probe = CreateFlyProbe(request => IsListRequest(request)
            ? JsonResponse(list)
            : new HttpResponseMessage(HttpStatusCode.NotFound));

        var observation = await probe.ProbeAsync(CancellationToken.None);

        Assert.False(observation.Available);
        Assert.Contains("started", observation.Failure);
    }

    [Fact]
    public async Task FlyProbe_SelectsTheConfiguredStoppedMachineAndReportsUnhealthy()
    {
        var list = MachineList(
            MachineJson("other", "started", "sha256:" + OtherSha),
            MachineJson("wanted", "stopped", "sha256:" + Sha));

        var probe = CreateFlyProbe(
            request => IsListRequest(request) ? JsonResponse(list) : new HttpResponseMessage(HttpStatusCode.NotFound),
            machineId: "wanted");

        var observation = await probe.ProbeAsync(CancellationToken.None);

        Assert.True(observation.Available);
        Assert.Equal("wanted", observation.MachineId);
        Assert.Equal("sha256:" + Sha, observation.Digest);
        Assert.False(observation.Healthy);
    }

    [Fact]
    public async Task FlyProbe_HandlesAnErrorStatus()
    {
        var probe = CreateFlyProbe(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var observation = await probe.ProbeAsync(CancellationToken.None);

        Assert.False(observation.Available);
        Assert.Contains("500", observation.Failure);
    }

    private static FlyRuntimeProbe CreateFlyProbe(
        Func<HttpRequestMessage, HttpResponseMessage> handler,
        string? machineId = null) =>
        new(
            new StubHttpClientFactory(handler),
            Options.Create(new ProofOptions
            {
                Fly = new FlyProofOptions
                {
                    AppName = "break-me",
                    ApiToken = "fly-token",
                    MachineId = machineId,
                },
            }),
            NullLogger<FlyRuntimeProbe>.Instance);

    private static bool IsListRequest(HttpRequestMessage request) =>
        request.RequestUri!.AbsolutePath.EndsWith("/machines", StringComparison.Ordinal);

    private static string MachineList(params string[] machines) => "[" + string.Join(",", machines) + "]";

    private static string MachineJson(
        string id,
        string state,
        string? digest,
        string? registry = "ghcr.io",
        string? repository = "just4skii/break-me-if-you-can",
        string? configImage = null,
        bool includeImageRef = true)
    {
        var machine = new Dictionary<string, object?>
        {
            ["id"] = id,
            ["state"] = state,
            ["region"] = "jnb",
            ["created_at"] = "2026-11-07T10:00:00Z",
        };

        if (includeImageRef)
        {
            machine["image_ref"] = new Dictionary<string, object?>
            {
                ["registry"] = registry,
                ["repository"] = repository,
                ["tag"] = "sha-abc",
                ["digest"] = digest,
            };
        }

        if (configImage is not null)
        {
            machine["config"] = new Dictionary<string, object?> { ["image"] = configImage };
        }

        return JsonSerializer.Serialize(machine);
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
                  "externalParameters": { "workflow": { "repository": "https://github.com/Just4Skii/break-me-if-you-can", "path": ".github/workflows/ci.yml", "ref": "refs/heads/main" } },
                  "resolvedDependencies": [ { "digest": { "gitCommit": "0123456789abcdef0123456789abcdef01234567" } } ]
                },
                "runDetails": { "metadata": { "invocationId": "https://github.com/Just4Skii/break-me-if-you-can/actions/runs/1234567890" } }
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
