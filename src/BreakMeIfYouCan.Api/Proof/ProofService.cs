using BreakMeIfYouCan.Api.Infrastructure;
using BreakMeIfYouCan.Evidence.Verification;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BreakMeIfYouCan.Api.Proof;

/// <summary>
/// Assembles the proof from four independent sources and hands them to the verifier:
/// the CI evidence manifest, the deployment intent, the Fly Machines API observation and
/// the GitHub attestations API. Nothing here decides the verdict; the verifier does.
/// </summary>
public sealed class ProofService(
    ICiEvidenceProvider evidenceProvider,
    IDeploymentIntentProvider deploymentIntentProvider,
    IRuntimeImageProbe runtimeProbe,
    IArtifactAttestationProbe attestationProbe,
    ProofVerifier verifier,
    IOptions<ProofOptions> options,
    IRunningBuild commit,
    IMemoryCache cache,
    ILogger<ProofService> logger)
{
    private readonly ProofOptions _options = options.Value;

    public async Task<ProofReport> GetReportAsync(CancellationToken cancellationToken)
    {
        var key = "proof-report:" + commit.CommitSha;

        if (_options.CacheSeconds > 0 && cache.TryGetValue(key, out ProofReport? cached) && cached is not null)
        {
            return cached;
        }

        var report = await BuildAsync(cancellationToken);

        if (_options.CacheSeconds > 0)
        {
            cache.Set(key, report, TimeSpan.FromSeconds(_options.CacheSeconds));
        }

        return report;
    }

    private async Task<ProofReport> BuildAsync(CancellationToken cancellationToken)
    {
        var bundle = await evidenceProvider.GetAsync(commit.CommitSha, cancellationToken);
        var runtime = await runtimeProbe.ProbeAsync(cancellationToken);

        var attestedDigest =
            bundle.Manifest?.Attestations.BuildProvenance.SubjectDigest
            ?? bundle.Manifest?.Attestations.QualityEvidence.SubjectDigest;

        var attestation = await attestationProbe.ProbeAsync(attestedDigest, cancellationToken);

        var intent = bundle.Manifest is null
            ? null
            : await deploymentIntentProvider.GetAsync(commit.CommitSha, cancellationToken);

        var notes = new List<string>();
        AddNote(notes, bundle.Failure);
        AddNote(notes, runtime.Failure);
        AddNote(notes, attestation.Failure);
        AddNote(
            notes,
            intent is null && bundle.Manifest is not null
                ? "No deployment intent was recorded for this release, so the requested digest is unknown."
                : null);

        logger.LogInformation(
            "Proof assembled: evidence={EvidenceSource} commit={Commit} runtime={RuntimeSource} attestation={AttestationSource}",
            bundle.Source,
            commit.CommitSha,
            runtime.Source,
            attestation.Source);

        var snapshot = new ProofSnapshot
        {
            Evidence = bundle.Manifest,
            EvidenceSource = bundle.Source,
            EvidenceFetchedAt = bundle.FetchedAt,
            ExpectedRepository = NullIfBlank(_options.Repository),
            ExpectedWorkflowFile = NullIfBlank(_options.WorkflowFile),
            RunningCommitSha = commit.HasCommit ? commit.CommitSha : null,
            RequestedDeploymentDigest = intent?.RequestedDigest,
            Runtime = runtime,
            Attestation = attestation,
            CoverageThresholdPercent = _options.CoverageThresholdPercent,
            MaxEvidenceAge = TimeSpan.FromHours(Math.Max(1, _options.MaxEvidenceAgeHours)),
            Now = DateTimeOffset.UtcNow,
            Notes = notes,
        };

        return verifier.Verify(snapshot);
    }

    private static void AddNote(List<string> notes, string? note)
    {
        if (!string.IsNullOrWhiteSpace(note))
        {
            notes.Add(note);
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}