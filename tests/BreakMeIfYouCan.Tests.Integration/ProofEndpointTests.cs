using System.Net;
using System.Net.Http.Json;
using BreakMeIfYouCan.Evidence.Generation;
using BreakMeIfYouCan.Evidence.Manifest;
using BreakMeIfYouCan.Evidence.Verification;
using BreakMeIfYouCan.Tests.Integration.Infrastructure;

namespace BreakMeIfYouCan.Tests.Integration;

[Collection(PostgresCollection.Name)]
public sealed class ProofEndpointTests(PostgresFixture database)
{
    [Fact]
    public async Task ProofEndpoint_WithoutRuntimeEvidence_ReportsUnknownAndSaysWhy()
    {
        var directory = Path.Combine(Path.GetTempPath(), "bm-proof-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            var path = Path.Combine(directory, "ci-evidence.json");
            EvidenceManifestFile.Write(MinimalManifest(), path);

            await using var factory = new ApiFactory(database, new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Proof:LocalEvidencePath"] = path,
                // No GitHub or Fly credentials here: the endpoint must degrade to UNKNOWN
                // rather than reach out to a network service during the test.
                ["Proof:Repository"] = string.Empty,
                ["Proof:CacheSeconds"] = "0",
            });
            using var client = factory.CreateClient();

            var response = await client.GetAsync(new Uri("/proof", UriKind.Relative));

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("unknown", response.Headers.GetValues("X-Evidence-State").Single());

            var report = await response.Content.ReadFromJsonAsync<ProofReport>(TestJson.Options);
            Assert.NotNull(report);
            Assert.Equal(EvidenceState.Unknown, report.State);
            Assert.Equal("local-file", report.EvidenceSource);
            Assert.NotEmpty(report.Checks);
            Assert.NotEmpty(report.Notes);
            Assert.Contains(report.Checks, check => check.Id == "deployment.health" && check.State == EvidenceState.Unknown);
            Assert.Contains(report.Checks, check => check.Id == "evidence.present" && check.State == EvidenceState.Verified);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProofEndpoint_WithNoEvidenceAtAll_StillReturnsAWellFormedReport()
    {
        await using var factory = new ApiFactory(database, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["Proof:Repository"] = string.Empty,
            ["Proof:CacheSeconds"] = "0",
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/proof", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var report = await response.Content.ReadFromJsonAsync<ProofReport>(TestJson.Options);
        Assert.NotNull(report);
        Assert.Equal(EvidenceState.Unknown, report.State);
        Assert.Contains(report.Checks, check => check.Id == "evidence.present" && check.State == EvidenceState.Unknown);
        Assert.Contains("cannot be verified", report.Summary, StringComparison.OrdinalIgnoreCase);
    }

    private static CiEvidenceManifest MinimalManifest() => new()
    {
        SchemaVersion = CiEvidenceManifest.CurrentSchemaVersion,
        Repository = "Skinny1ne/break-me-if-you-can",
        CommitSha = "0123456789abcdef0123456789abcdef01234567",
        Branch = "main",
        GeneratedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
        Workflow = new WorkflowEvidence
        {
            Name = "CI",
            File = ".github/workflows/ci.yml",
            RunId = "1234567890",
            RunAttempt = "1",
            RunUrl = "https://github.com/Skinny1ne/break-me-if-you-can/actions/runs/1234567890",
            Event = "push",
            Conclusion = PipelineStatus.Success,
        },
        Build = new BuildEvidence { Status = PipelineStatus.Success, Configuration = "Release", Framework = "net10.0" },
        Tests = new TestEvidence { Status = PipelineStatus.Success, Total = 94, Passed = 94, Failed = 0, Skipped = 0, Reports = ["tests.trx"] },
        Coverage = new CoverageEvidence { LinePercent = 92.7, BranchPercent = 80.8, ThresholdPercent = 75, MeetsThreshold = true },
        DependencyAudit = new DependencyAuditEvidence { Status = AuditStatus.Clean },
        Image = new ImageEvidence { Reference = "ghcr.io/Skinny1ne/break-me-if-you-can", Digest = "sha256:" + new string('a', 64) },
        Attestations = new AttestationEvidence
        {
            BuildProvenance = new AttestationRecord
            {
                Status = PipelineStatus.Success,
                PredicateType = "https://slsa.dev/provenance/v1",
                SubjectDigest = "sha256:" + new string('a', 64),
                Repository = "Skinny1ne/break-me-if-you-can",
                CommitSha = "0123456789abcdef0123456789abcdef01234567",
            },
        },
    };
}