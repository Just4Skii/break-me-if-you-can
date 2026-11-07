using BreakMeIfYouCan.Evidence.Manifest;
using BreakMeIfYouCan.Evidence.Verification;

namespace BreakMeIfYouCan.Tests.Unit;

public sealed class ProofVerifierTests
{
    private static readonly ProofVerifier Verifier = new();

    [Fact]
    public void FullyCorroboratedRelease_IsVerified()
    {
        var report = Verifier.Verify(ProofFixtures.Snapshot());

        Assert.Equal(EvidenceState.Verified, report.State);
        Assert.All(report.Checks.Where(c => c.Required), check => Assert.Equal(EvidenceState.Verified, check.State));
        Assert.Equal(ProofFixtures.Digest, report.Image.BuiltDigest);
        Assert.Equal(ProofFixtures.Digest, report.Image.AttestedDigest);
        Assert.Equal(ProofFixtures.Digest, report.Image.RequestedDeploymentDigest);
        Assert.Equal(ProofFixtures.Digest, report.Image.ObservedRunningDigest);
        Assert.NotNull(report.VerifyCommand);
        Assert.Contains(ProofFixtures.Digest, report.VerifyCommand);
        Assert.Contains(ProofFixtures.Repository, report.VerifyCommand);
        Assert.Contains("verified", report.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingEvidence_IsUnknown()
    {
        var snapshot = ProofFixtures.Snapshot() with { Evidence = null, EvidenceSource = "none" };
        var report = Verifier.Verify(snapshot);

        Assert.Equal(EvidenceState.Unknown, report.State);
        Assert.Contains(report.Checks, c => c.Id == "evidence.present" && c.State == EvidenceState.Unknown);
        Assert.Contains("cannot be verified", report.Summary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StaleEvidence_IsUnknown()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = ProofFixtures.Snapshot(now: now) with
        {
            Evidence = ProofFixtures.Manifest(now.AddHours(-30)),
            MaxEvidenceAge = TimeSpan.FromHours(24),
        };

        var report = Verifier.Verify(snapshot);

        Assert.Equal(EvidenceState.Unknown, report.State);
        Assert.True(report.Freshness.Stale);
        Assert.Contains(report.Checks, c => c.Id == "evidence.freshness" && c.State == EvidenceState.Unknown);
    }

    [Fact]
    public void EvidenceWithAFutureTimestamp_IsUnknown()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = ProofFixtures.Snapshot(now: now) with
        {
            Evidence = ProofFixtures.Manifest(now.AddHours(2)),
        };

        var report = Verifier.Verify(snapshot);

        Assert.Equal(EvidenceState.Unknown, report.State);
        Assert.Contains(report.Checks, c => c.Id == "evidence.freshness" && c.State == EvidenceState.Unknown);
    }

    [Fact]
    public void UnknownEvidenceSchema_IsUnknown()
    {
        var manifest = ProofFixtures.Manifest() with { SchemaVersion = 99 };
        var report = Verifier.Verify(ProofFixtures.Snapshot(manifest));

        Assert.Equal(EvidenceState.Unknown, report.State);
        Assert.Contains(report.Checks, c => c.Id == "evidence.schema" && c.State == EvidenceState.Unknown);
    }

    [Fact]
    public void MismatchedRequestedDigest_FailsAndBreaksTheChain()
    {
        var snapshot = ProofFixtures.Snapshot() with { RequestedDeploymentDigest = ProofFixtures.OtherDigest };
        var report = Verifier.Verify(snapshot);

        Assert.Equal(EvidenceState.Failed, report.State);
        Assert.Contains(report.Checks, c => c.Id == "digest.attested_vs_requested" && c.State == EvidenceState.Failed);
        Assert.Contains(report.Checks, c => c.Id == "digest.requested_vs_observed" && c.State == EvidenceState.Failed);
        Assert.Contains(report.Checks, c => c.Id == "digest.chain" && c.State == EvidenceState.Failed);
    }

    [Fact]
    public void ObservedRuntimeDigestThatDiffersFromTheBuild_Fails()
    {
        var snapshot = ProofFixtures.Snapshot() with
        {
            Runtime = ProofFixtures.Snapshot().Runtime! with { Digest = ProofFixtures.OtherDigest },
        };

        var report = Verifier.Verify(snapshot);

        Assert.Equal(EvidenceState.Failed, report.State);
        Assert.Contains(report.Checks, c => c.Id == "digest.built_vs_observed" && c.State == EvidenceState.Failed);
    }

    [Fact]
    public void UnavailableRuntimeObservation_IsUnknownRatherThanFailed()
    {
        var snapshot = ProofFixtures.Snapshot() with
        {
            Runtime = new RuntimeObservation { Available = false, Failure = "FLY_API_TOKEN is not configured." },
        };

        var report = Verifier.Verify(snapshot);

        Assert.Equal(EvidenceState.Unknown, report.State);
        Assert.Contains(report.Checks, c => c.Id == "deployment.health" && c.State == EvidenceState.Unknown);
        Assert.Contains(report.Checks, c => c.Id == "digest.chain" && c.State == EvidenceState.Unknown);
        Assert.Equal("unknown", report.Deployment.Health);
    }

    [Fact]
    public void MissingAttestedDigest_IsUnknown()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with
        {
            Attestations = manifest.Attestations with
            {
                BuildProvenance = manifest.Attestations.BuildProvenance with { SubjectDigest = null, Status = PipelineStatus.Missing },
                QualityEvidence = manifest.Attestations.QualityEvidence with { SubjectDigest = null },
            },
            Image = manifest.Image with { Digest = ProofFixtures.Digest },
        };

        var snapshot = ProofFixtures.Snapshot(manifest) with
        {
            Attestation = new ArtifactAttestationObservation { Available = false, Failure = "GitHub API unavailable." },
        };

        var report = Verifier.Verify(snapshot);

        Assert.Equal(EvidenceState.Unknown, report.State);
        Assert.Contains(report.Checks, c => c.Id == "digest.build_vs_attested" && c.State == EvidenceState.Unknown);
    }

    [Fact]
    public void AttestationMissingOnGitHub_Fails()
    {
        var snapshot = ProofFixtures.Snapshot() with
        {
            Attestation = ProofFixtures.Snapshot().Attestation! with { Found = false, AttestationCount = 0 },
        };

        var report = Verifier.Verify(snapshot);

        Assert.Equal(EvidenceState.Failed, report.State);
        Assert.Contains(report.Checks, c => c.Id == "attestation.independent" && c.State == EvidenceState.Failed);
    }

    [Fact]
    public void AttestationBoundToAnotherCommit_Fails()
    {
        var snapshot = ProofFixtures.Snapshot() with
        {
            Attestation = ProofFixtures.Snapshot().Attestation! with { CommitSha = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef" },
        };

        var report = Verifier.Verify(snapshot);

        Assert.Equal(EvidenceState.Failed, report.State);
        Assert.Contains(
            report.Checks,
            c => c.Id == "attestation.independent" && c.State == EvidenceState.Failed && c.Detail.Contains("commit", StringComparison.Ordinal));
    }

    [Fact]
    public void AttestationBoundToAnotherWorkflow_Fails()
    {
        var snapshot = ProofFixtures.Snapshot() with
        {
            Attestation = ProofFixtures.Snapshot().Attestation! with
            {
                WorkflowRef = ProofFixtures.Repository + "/.github/workflows/other.yml@refs/heads/main",
            },
        };

        var report = Verifier.Verify(snapshot);

        Assert.Equal(EvidenceState.Failed, report.State);
        Assert.Contains(report.Checks, c => c.Id == "attestation.independent" && c.State == EvidenceState.Failed);
    }

    [Fact]
    public void FailedTests_FailTheRelease()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with
        {
            Tests = manifest.Tests with { Status = PipelineStatus.Failure, Passed = 117, Failed = 1 },
        };

        var report = Verifier.Verify(ProofFixtures.Snapshot(manifest));

        Assert.Equal(EvidenceState.Failed, report.State);
        Assert.Contains(report.Checks, c => c.Id == "tests.executed" && c.State == EvidenceState.Failed);
    }

    [Fact]
    public void TestReportWithNoResults_IsUnknownNotAPass()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with
        {
            Tests = manifest.Tests with { Status = PipelineStatus.Success, Total = 0, Passed = 0, Skipped = 0 },
        };

        var report = Verifier.Verify(ProofFixtures.Snapshot(manifest));

        Assert.Equal(EvidenceState.Unknown, report.State);
        Assert.Contains(report.Checks, c => c.Id == "tests.executed" && c.State == EvidenceState.Unknown);
    }

    [Fact]
    public void MissingTrxReport_IsUnknownNotAPass()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with
        {
            Tests = manifest.Tests with { Status = PipelineStatus.Missing, MissingReports = ["BreakMeIfYouCan.Tests.Integration"] },
        };

        var report = Verifier.Verify(ProofFixtures.Snapshot(manifest));

        Assert.Equal(EvidenceState.Unknown, report.State);
        Assert.Contains(report.Checks, c => c.Id == "tests.executed" && c.State == EvidenceState.Unknown);
    }

    [Fact]
    public void InternallyInconsistentTestCounts_Fail()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with { Tests = manifest.Tests with { Total = 10, Passed = 5, Skipped = 0, Failed = 0 } };

        var report = Verifier.Verify(ProofFixtures.Snapshot(manifest));

        Assert.Equal(EvidenceState.Failed, report.State);
        Assert.Contains(report.Checks, c => c.Id == "tests.executed" && c.State == EvidenceState.Failed);
    }

    [Fact]
    public void CoverageBelowThreshold_Fails()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with
        {
            Coverage = manifest.Coverage with { LinePercent = 61.2, MeetsThreshold = false },
        };

        var report = Verifier.Verify(ProofFixtures.Snapshot(manifest));

        Assert.Equal(EvidenceState.Failed, report.State);
        Assert.Contains(report.Checks, c => c.Id == "coverage.threshold" && c.State == EvidenceState.Failed);
        Assert.False(report.Coverage.MeetsThreshold);
    }

    [Fact]
    public void MissingCoverage_IsUnknown()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with { Coverage = manifest.Coverage with { LinePercent = null, MeetsThreshold = false } };

        var report = Verifier.Verify(ProofFixtures.Snapshot(manifest));

        Assert.Equal(EvidenceState.Unknown, report.State);
        Assert.Contains(report.Checks, c => c.Id == "coverage.threshold" && c.State == EvidenceState.Unknown);
    }

    [Fact]
    public void VulnerableDependency_Fails()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with
        {
            DependencyAudit = manifest.DependencyAudit with
            {
                Status = AuditStatus.Vulnerable,
                DirectCount = 1,
                BySeverity = new Dictionary<string, int> { ["high"] = 1 },
                Packages =
                [
                    new VulnerablePackage { Id = "Example.Package", Version = "1.0.0", Severity = "high", AdvisoryUrl = "https://example.test" },
                ],
            },
        };

        var report = Verifier.Verify(ProofFixtures.Snapshot(manifest));

        Assert.Equal(EvidenceState.Failed, report.State);
        Assert.Contains(report.Checks, c => c.Id == "dependency.audit" && c.State == EvidenceState.Failed);
        Assert.Single(report.DependencyAudit.Packages);
    }

    [Fact]
    public void MissingDependencyAudit_IsUnknown()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with
        {
            DependencyAudit = manifest.DependencyAudit with { Status = AuditStatus.Missing },
        };

        var report = Verifier.Verify(ProofFixtures.Snapshot(manifest));

        Assert.Equal(EvidenceState.Unknown, report.State);
        Assert.Contains(report.Checks, c => c.Id == "dependency.audit" && c.State == EvidenceState.Unknown);
    }

    [Fact]
    public void CleanAuditContradictedByReportedPackages_Fails()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with
        {
            DependencyAudit = manifest.DependencyAudit with
            {
                Status = AuditStatus.Clean,
                TransitiveCount = 1,
            },
        };

        var report = Verifier.Verify(ProofFixtures.Snapshot(manifest));

        Assert.Equal(EvidenceState.Failed, report.State);
        Assert.Contains(report.Checks, c => c.Id == "dependency.audit" && c.State == EvidenceState.Failed);
    }

    [Fact]
    public void FailedBuild_Fails()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with { Build = manifest.Build with { Status = PipelineStatus.Failure } };

        var report = Verifier.Verify(ProofFixtures.Snapshot(manifest));

        Assert.Equal(EvidenceState.Failed, report.State);
        Assert.Contains(report.Checks, c => c.Id == "build.status" && c.State == EvidenceState.Failed);
    }

    [Fact]
    public void EvidenceFromAPullRequest_Fails()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with { Workflow = manifest.Workflow with { Event = "pull_request" } };

        var report = Verifier.Verify(ProofFixtures.Snapshot(manifest));

        Assert.Equal(EvidenceState.Failed, report.State);
        Assert.Contains(report.Checks, c => c.Id == "workflow.binding" && c.State == EvidenceState.Failed);
    }

    [Fact]
    public void EvidenceFromAnOlderCommit_Fails()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with { CommitSha = "fedcba9876543210fedcba9876543210fedcba98" };

        var report = Verifier.Verify(ProofFixtures.Snapshot(manifest));

        Assert.Equal(EvidenceState.Failed, report.State);
        Assert.Contains(report.Checks, c => c.Id == "evidence.commit" && c.State == EvidenceState.Failed);
    }

    [Fact]
    public void EvidenceFromAnotherRepository_Fails()
    {
        var manifest = ProofFixtures.Manifest();
        manifest = manifest with { Repository = "someone-else/another-repo" };

        var report = Verifier.Verify(ProofFixtures.Snapshot(manifest));

        Assert.Equal(EvidenceState.Failed, report.State);
        Assert.Contains(report.Checks, c => c.Id == "evidence.repository" && c.State == EvidenceState.Failed);
    }

    [Fact]
    public void UnhealthyDeployment_Fails()
    {
        var snapshot = ProofFixtures.Snapshot() with
        {
            Runtime = ProofFixtures.Snapshot().Runtime! with { Healthy = false, HealthDetail = "Machine failed to start" },
        };

        var report = Verifier.Verify(snapshot);

        Assert.Equal(EvidenceState.Failed, report.State);
        Assert.Contains(report.Checks, c => c.Id == "deployment.health" && c.State == EvidenceState.Failed);
        Assert.Equal("unhealthy", report.Deployment.Health);
    }

    [Fact]
    public void UnknownRunningCommit_MakesCommitBindingUnknown()
    {
        var snapshot = ProofFixtures.Snapshot() with { RunningCommitSha = null };
        var report = Verifier.Verify(snapshot);

        Assert.Equal(EvidenceState.Unknown, report.State);
        Assert.Contains(report.Checks, c => c.Id == "evidence.commit" && c.State == EvidenceState.Unknown);
    }

    [Fact]
    public void SecurityPosture_DistinguishesScanFromAssessment()
    {
        var report = Verifier.Verify(ProofFixtures.Snapshot());

        Assert.Contains("not a security assessment", report.SecurityPosture.Disclaimer, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(report.SecurityPosture.ImplementedControls);
        Assert.NotEmpty(report.SecurityPosture.NotAssessed);
        Assert.Contains("NuGet", report.SecurityPosture.ScanScope, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CheckIdsAreUnique()
    {
        var report = Verifier.Verify(ProofFixtures.Snapshot());
        var duplicates = report.Checks.GroupBy(c => c.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicates);
    }
}
