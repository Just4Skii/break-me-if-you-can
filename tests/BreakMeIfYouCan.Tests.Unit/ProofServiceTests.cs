using BreakMeIfYouCan.Api.Infrastructure;
using BreakMeIfYouCan.Api.Proof;
using BreakMeIfYouCan.Evidence.Verification;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BreakMeIfYouCan.Tests.Unit;

public sealed class ProofServiceTests
{
    [Fact]
    public async Task FullyCorroboratedSources_ProduceVerified()
    {
        var service = CreateService(out _);

        var report = await service.GetReportAsync(CancellationToken.None);

        Assert.Equal(EvidenceState.Verified, report.State);
        Assert.Empty(report.Notes);
    }

    [Fact]
    public async Task MissingEvidence_ProducesUnknownWithAnExplanation()
    {
        var service = CreateService(out var sources, evidence: new EvidenceBundle(null, "none", DateTimeOffset.UtcNow, "No CI run exists for this commit."));

        var report = await service.GetReportAsync(CancellationToken.None);

        Assert.Equal(EvidenceState.Unknown, report.State);
        Assert.Contains(report.Notes, note => note.Contains("No CI run", StringComparison.Ordinal));
        Assert.Empty(sources.IntentCalls);
    }

    [Fact]
    public async Task MissingDeploymentIntent_ProducesUnknownAndIsExplained()
    {
        var service = CreateService(out _, includeIntent: false);

        var report = await service.GetReportAsync(CancellationToken.None);

        Assert.Equal(EvidenceState.Unknown, report.State);
        Assert.Null(report.Image.RequestedDeploymentDigest);
        Assert.Contains(report.Notes, note => note.Contains("deployment intent", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RuntimeProbeFailure_IsUnknownAndExplained()
    {
        var service = CreateService(out _, runtime: new RuntimeObservation
        {
            Available = false,
            Failure = "A Fly.io API token is not configured.",
            Source = "fly-machines-api",
        });

        var report = await service.GetReportAsync(CancellationToken.None);

        Assert.Equal(EvidenceState.Unknown, report.State);
        Assert.Contains(report.Notes, note => note.Contains("Fly.io", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("unknown", report.Deployment.Health);
    }

    [Fact]
    public async Task AttestationProbeFailure_IsUnknownAndExplained()
    {
        var service = CreateService(out _, attestation: new ArtifactAttestationObservation
        {
            Available = false,
            Failure = "The GitHub attestations API request failed.",
        });

        var report = await service.GetReportAsync(CancellationToken.None);

        Assert.Equal(EvidenceState.Unknown, report.State);
        Assert.Contains(report.Notes, note => note.Contains("GitHub attestations", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReportIsCachedForTheConfiguredWindow()
    {
        var service = CreateService(out var sources, cacheSeconds: 60);

        await service.GetReportAsync(CancellationToken.None);
        await service.GetReportAsync(CancellationToken.None);

        Assert.Equal(1, sources.EvidenceCalls);
    }

    [Fact]
    public async Task ReportIsNotCachedWhenTheWindowIsZero()
    {
        var service = CreateService(out var sources, cacheSeconds: 0);

        await service.GetReportAsync(CancellationToken.None);
        await service.GetReportAsync(CancellationToken.None);

        Assert.Equal(2, sources.EvidenceCalls);
    }

    private static ProofService CreateService(
        out Sources sources,
        EvidenceBundle? evidence = null,
        DeploymentIntent? intent = default,
        RuntimeObservation? runtime = null,
        ArtifactAttestationObservation? attestation = null,
        int cacheSeconds = 0,
        bool includeIntent = true)
    {
        var now = DateTimeOffset.UtcNow;
        sources = new Sources
        {
            Evidence = evidence ?? new EvidenceBundle(ProofFixtures.Manifest(now.AddMinutes(-30)), "github-actions-artifact", now.AddMinutes(-1), null),
            Intent = includeIntent
                ? intent ?? new DeploymentIntent(ProofFixtures.Digest, ProofFixtures.Commit, "1234567890", now.AddMinutes(-5))
                : null,
            Runtime = runtime ?? ProofFixtures.Snapshot(now: now).Runtime!,
            Attestation = attestation ?? ProofFixtures.Snapshot(now: now).Attestation!,
        };

        return new ProofService(
            sources,
            sources,
            sources,
            sources,
            new ProofVerifier(),
            Options.Create(new ProofOptions
            {
                Repository = ProofFixtures.Repository,
                WorkflowFile = ProofFixtures.Workflow,
                CoverageThresholdPercent = 75,
                MaxEvidenceAgeHours = 24,
                CacheSeconds = cacheSeconds,
            }),
            new FakeBuild(ProofFixtures.Commit),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<ProofService>.Instance);
    }

    private sealed class FakeBuild(string commit) : IRunningBuild
    {
        public string CommitSha => commit;

        public string InformationalVersion => "1.0.0+" + commit;

        public bool HasCommit => true;
    }

    private sealed class Sources : ICiEvidenceProvider, IDeploymentIntentProvider, IRuntimeImageProbe, IArtifactAttestationProbe
    {
        public EvidenceBundle Evidence { get; set; } = null!;

        public DeploymentIntent? Intent { get; set; }

        public RuntimeObservation Runtime { get; set; } = null!;

        public ArtifactAttestationObservation Attestation { get; set; } = null!;

        public int EvidenceCalls { get; private set; }

        public List<int> IntentCalls { get; } = [];

        public Task<EvidenceBundle> GetAsync(string commitSha, CancellationToken cancellationToken)
        {
            _ = commitSha;
            _ = cancellationToken;
            EvidenceCalls++;
            return Task.FromResult(Evidence);
        }

        Task<DeploymentIntent?> IDeploymentIntentProvider.GetAsync(string commitSha, CancellationToken cancellationToken)
        {
            _ = commitSha;
            _ = cancellationToken;
            IntentCalls.Add(1);
            return Task.FromResult(Intent);
        }

        public Task<RuntimeObservation> ProbeAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult(Runtime);
        }

        Task<ArtifactAttestationObservation> IArtifactAttestationProbe.ProbeAsync(string? digest, CancellationToken cancellationToken)
        {
            _ = digest;
            _ = cancellationToken;
            return Task.FromResult(Attestation);
        }
    }
}