using BreakMeIfYouCan.Evidence.Manifest;
using BreakMeIfYouCan.Evidence.Verification;

namespace BreakMeIfYouCan.Tests.Unit;

internal static class ProofFixtures
{
    public const string Commit = "0123456789abcdef0123456789abcdef01234567";
    public const string Repository = "Skinny1ne/break-me-if-you-can";
    public const string Workflow = ".github/workflows/ci.yml";

    public static readonly string Digest = "sha256:" + new string('a', 64);
    public static readonly string OtherDigest = "sha256:" + new string('b', 64);

    public static CiEvidenceManifest Manifest(DateTimeOffset? generatedAt = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new CiEvidenceManifest
        {
            SchemaVersion = CiEvidenceManifest.CurrentSchemaVersion,
            Repository = Repository,
            CommitSha = Commit,
            Branch = "main",
            GeneratedAt = generatedAt ?? now.AddMinutes(-30),
            Workflow = new WorkflowEvidence
            {
                Name = "CI",
                File = Workflow,
                RunId = "1234567890",
                RunAttempt = "1",
                RunUrl = "https://github.com/" + Repository + "/actions/runs/1234567890",
                Event = "push",
                Actor = "Skinny1ne",
                Conclusion = PipelineStatus.Success,
            },
            Build = new BuildEvidence
            {
                Status = PipelineStatus.Success,
                Configuration = "Release",
                Framework = "net10.0",
                DotnetVersion = "10.0.202",
                StartedAt = now.AddMinutes(-30),
                CompletedAt = now.AddMinutes(-18),
                Runner = "ubuntu-latest",
            },
            Tests = new TestEvidence
            {
                Status = PipelineStatus.Success,
                Total = 120,
                Passed = 118,
                Failed = 0,
                Skipped = 2,
                DurationSeconds = 42.5,
                Reports = ["unit.trx", "integration.trx"],
                Assemblies =
                [
                    new TestAssemblyEvidence { Assembly = "BreakMeIfYouCan.Tests.Unit", Total = 90, Passed = 90, Failed = 0, Skipped = 0, DurationSeconds = 10.1 },
                    new TestAssemblyEvidence { Assembly = "BreakMeIfYouCan.Tests.Integration", Total = 30, Passed = 28, Failed = 0, Skipped = 2, DurationSeconds = 32.4 },
                ],
            },
            Coverage = new CoverageEvidence
            {
                LinePercent = 84.5,
                BranchPercent = 72.1,
                ThresholdPercent = 75,
                MeetsThreshold = true,
                ReportArtifact = "coverage.cobertura.xml",
                LinesCovered = 845,
                LinesValid = 1_000,
            },
            DependencyAudit = new DependencyAuditEvidence
            {
                Status = AuditStatus.Clean,
                DirectCount = 0,
                TransitiveCount = 0,
                Command = "dotnet list package --vulnerable --include-transitive --format json",
                Note = "No advisories reported.",
            },
            Image = new ImageEvidence
            {
                Reference = "ghcr.io/" + Repository,
                Digest = Digest,
                BuiltAt = now.AddMinutes(-10),
                Registry = "ghcr.io",
            },
            Attestations = new AttestationEvidence
            {
                BuildProvenance = new AttestationRecord
                {
                    Status = PipelineStatus.Success,
                    PredicateType = "https://slsa.dev/provenance/v1",
                    SubjectDigest = Digest,
                    Repository = Repository,
                    CommitSha = Commit,
                    WorkflowRef = Repository + "/" + Workflow + "@refs/heads/main",
                    RunId = "1234567890",
                    VerifiedAt = now.AddMinutes(-8),
                    Verifier = "gh attestation verify 2.62.0",
                    Command = "gh attestation verify oci://ghcr.io/" + Repository + "@" + Digest + " --repo " + Repository,
                    ExitCode = 0,
                },
                QualityEvidence = new AttestationRecord
                {
                    Status = PipelineStatus.Success,
                    PredicateType = "https://breakmeifyoucan.dev/attestation/quality/v1",
                    SubjectDigest = Digest,
                    Repository = Repository,
                    CommitSha = Commit,
                    WorkflowRef = Repository + "/" + Workflow + "@refs/heads/main",
                    RunId = "1234567890",
                    VerifiedAt = now.AddMinutes(-8),
                    Verifier = "gh attestation verify 2.62.0",
                    Command = "gh attestation verify oci://ghcr.io/" + Repository + "@" + Digest + " --repo " + Repository,
                    ExitCode = 0,
                },
            },
            SecurityPosture = new SecurityPostureEvidence
            {
                Disclaimer = "A clean dependency scan is not a security assessment.",
                ImplementedControls = ["Rate limiting", "CHECK constraint"],
                NotAssessed = ["Penetration testing"],
            },
        };
    }

    public static ProofSnapshot Snapshot(CiEvidenceManifest? evidence = null, DateTimeOffset? now = null)
    {
        var timestamp = now ?? DateTimeOffset.UtcNow;
        return new ProofSnapshot
        {
            Evidence = evidence ?? Manifest(timestamp.AddMinutes(-30)),
            EvidenceSource = "github-actions-artifact",
            EvidenceFetchedAt = timestamp.AddMinutes(-1),
            ExpectedRepository = Repository,
            ExpectedWorkflowFile = Workflow,
            RunningCommitSha = Commit,
            RequestedDeploymentDigest = Digest,
            Runtime = new RuntimeObservation
            {
                Available = true,
                Digest = Digest,
                ImageReference = "ghcr.io/" + Repository + "@" + Digest,
                AppName = "break-me-if-you-can",
                MachineId = "9182736450",
                Region = "jnb",
                Healthy = true,
                HealthDetail = "Machine is started",
                MachineCreatedAt = timestamp.AddMinutes(-5),
                ObservedAt = timestamp.AddSeconds(-2),
                Source = "fly-machines-api",
                SourceUrl = "https://api.machines.dev/v1/apps/break-me-if-you-can/machines",
            },
            Attestation = new ArtifactAttestationObservation
            {
                Available = true,
                Found = true,
                AttestationCount = 2,
                SubjectDigest = Digest,
                PredicateType = "https://slsa.dev/provenance/v1",
                Repository = Repository,
                CommitSha = Commit,
                WorkflowRef = Repository + "/" + Workflow + "@refs/heads/main",
                RunId = "1234567890",
                ObservedAt = timestamp.AddSeconds(-3),
                Source = "github-attestations-api",
                SourceUrl = "https://api.github.com/repos/" + Repository + "/attestations/" + Digest,
            },
            CoverageThresholdPercent = 75,
            MaxEvidenceAge = TimeSpan.FromHours(24),
            Now = timestamp,
        };
    }
}
