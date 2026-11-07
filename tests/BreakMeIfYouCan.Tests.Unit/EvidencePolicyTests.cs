using BreakMeIfYouCan.Evidence.Generation;
using BreakMeIfYouCan.Evidence.Manifest;

namespace BreakMeIfYouCan.Tests.Unit;

public sealed class EvidencePolicyTests
{
    [Fact]
    public void CleanManifest_SatisfiesPolicy()
    {
        var violations = EvidencePolicy.Evaluate(ProofFixtures.Manifest());

        Assert.Empty(violations);
    }

    [Fact]
    public void FailedTests_ViolatePolicy()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with { Tests = manifest.Tests with { Status = PipelineStatus.Failure, Failed = 2 } };

        Assert.Contains(EvidencePolicy.Evaluate(manifest), v => v.Id == "tests.not_passing");
    }

    [Fact]
    public void EmptyTestReport_ViolatesPolicy()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with { Tests = manifest.Tests with { Total = 0, Passed = 0, Skipped = 0 } };

        Assert.Contains(EvidencePolicy.Evaluate(manifest), v => v.Id == "tests.empty");
    }

    [Fact]
    public void CoverageBelowThreshold_ViolatesPolicy()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with { Coverage = manifest.Coverage with { LinePercent = 60, MeetsThreshold = false } };

        Assert.Contains(EvidencePolicy.Evaluate(manifest), v => v.Id == "coverage.below_threshold");
    }

    [Fact]
    public void MissingCoverage_ViolatesPolicy()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with { Coverage = manifest.Coverage with { LinePercent = null, MeetsThreshold = false } };

        Assert.Contains(EvidencePolicy.Evaluate(manifest), v => v.Id == "coverage.missing");
    }

    [Fact]
    public void VulnerableAudit_ViolatesPolicy()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with { DependencyAudit = manifest.DependencyAudit with { Status = AuditStatus.Vulnerable } };

        Assert.Contains(EvidencePolicy.Evaluate(manifest), v => v.Id == "dependency.audit");
    }

    [Fact]
    public void MissingImageDigest_ViolatesPolicy()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with { Image = manifest.Image with { Digest = null } };

        Assert.Contains(EvidencePolicy.Evaluate(manifest), v => v.Id == "image.digest_missing");
    }

    [Fact]
    public void UnverifiedAttestation_ViolatesPolicy()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with
        {
            Attestations = manifest.Attestations with
            {
                BuildProvenance = manifest.Attestations.BuildProvenance with { Status = PipelineStatus.Failure },
            },
        };

        Assert.Contains(EvidencePolicy.Evaluate(manifest), v => v.Id == "attestation.not_verified");
    }

    [Fact]
    public void ShortCommit_ViolatesPolicy()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with { CommitSha = "abc123" };

        Assert.Contains(EvidencePolicy.Evaluate(manifest), v => v.Id == "commit.invalid");
    }

    [Fact]
    public void NonSuccessfulWorkflow_ViolatesPolicy()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with { Workflow = manifest.Workflow with { Conclusion = PipelineStatus.Failure } };

        Assert.Contains(EvidencePolicy.Evaluate(manifest), v => v.Id == "workflow.not_successful");
    }

    [Fact]
    public void MissingEverything_ProducesTheWholeSetOfViolations()
    {
        var manifest = new CiEvidenceManifest
        {
            Repository = "r",
            CommitSha = string.Empty,
            GeneratedAt = DateTimeOffset.UtcNow,
            Workflow = new WorkflowEvidence { Conclusion = PipelineStatus.Missing },
            Tests = new TestEvidence { Status = PipelineStatus.Missing },
            Coverage = new CoverageEvidence(),
            DependencyAudit = new DependencyAuditEvidence { Status = AuditStatus.Missing },
            Attestations = new AttestationEvidence(),
        };

        var violations = EvidencePolicy.Evaluate(manifest);

        Assert.Contains(violations, v => v.Id == "commit.invalid");
        Assert.Contains(violations, v => v.Id == "tests.not_passing");
        Assert.Contains(violations, v => v.Id == "coverage.missing");
        Assert.Contains(violations, v => v.Id == "dependency.audit");
        Assert.Contains(violations, v => v.Id == "image.digest_missing");
        Assert.Contains(violations, v => v.Id == "attestation.not_verified");
        Assert.Contains(violations, v => v.Id == "workflow.not_successful");
    }
}
