using BreakMeIfYouCan.Evidence.Generation;
using BreakMeIfYouCan.Evidence.Manifest;

namespace BreakMeIfYouCan.Tests.Unit;

public sealed class ManifestBuilderTests
{
    private const string Trx = """
        <?xml version="1.0" encoding="utf-8"?>
        <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
          <TestDefinitions>
            <UnitTest name="T" storage="bin/BreakMeIfYouCan.Tests.Unit.dll" id="t1" />
          </TestDefinitions>
          <Results>
            <UnitTestResult testId="t1" outcome="Passed" duration="00:00:00.5" />
          </Results>
        </TestRun>
        """;

    private const string Coverage = """
        <coverage line-rate="0.82" branch-rate="0.61" lines-covered="82" lines-valid="100" branches-covered="61" branches-valid="100" />
        """;

    private const string CleanAudit = """{ "version": 1, "projects": [ { "path": "p" } ] }""";

    private static CiEvidenceManifest Build(TestTempDirectory directory, double threshold = 75)
    {
        var trx = directory.Write("unit.trx", Trx);
        var coverage = directory.Write("coverage.xml", Coverage);
        var audit = directory.Write("audit.json", CleanAudit);

        return ManifestBuilder.Build(new EvidenceBuildRequest
        {
            Context = new EvidenceBuildContext
            {
                Repository = ProofFixtures.Repository,
                CommitSha = ProofFixtures.Commit,
                Branch = "main",
                Workflow = ProofFixtures.Manifest().Workflow,
                Build = ProofFixtures.Manifest().Build,
                Image = ProofFixtures.Manifest().Image,
                Attestations = ProofFixtures.Manifest().Attestations,
            },
            TestResultFiles = [trx],
            CoverageReportFiles = [coverage],
            AuditReportPath = audit,
            AuditExitCode = 0,
            CoverageThresholdPercent = threshold,
            ExpectedTestAssemblies = ["BreakMeIfYouCan.Tests.Unit"],
            GeneratedAt = DateTimeOffset.UtcNow,
        });
    }

    [Fact]
    public void Build_ProducesANonTrivialManifestFromRealReports()
    {
        using var directory = new TestTempDirectory();
        var manifest = Build(directory);

        Assert.Equal(PipelineStatus.Success, manifest.Tests.Status);
        Assert.Equal(1, manifest.Tests.Total);
        Assert.Equal(82, manifest.Coverage.LinePercent);
        Assert.True(manifest.Coverage.MeetsThreshold);
        Assert.Equal(AuditStatus.Clean, manifest.DependencyAudit.Status);
        Assert.Equal(ProofFixtures.Digest, manifest.Image.Digest);
        Assert.Equal(PipelineStatus.Success, manifest.Attestations.BuildProvenance.Status);
        Assert.NotEmpty(manifest.SecurityPosture.ImplementedControls);
        Assert.Contains("not a penetration test", manifest.SecurityPosture.Disclaimer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_MarksMissingExpectedTestReports()
    {
        using var directory = new TestTempDirectory();
        var trx = directory.Write("unit.trx", Trx);

        var manifest = ManifestBuilder.Build(new EvidenceBuildRequest
        {
            Context = new EvidenceBuildContext
            {
                Repository = ProofFixtures.Repository,
                CommitSha = ProofFixtures.Commit,
                Branch = "main",
            },
            TestResultFiles = [trx],
            ExpectedTestAssemblies = ["BreakMeIfYouCan.Tests.Unit", "BreakMeIfYouCan.Tests.Integration"],
            GeneratedAt = DateTimeOffset.UtcNow,
        });

        Assert.Equal(PipelineStatus.Missing, manifest.Tests.Status);
        Assert.Contains("BreakMeIfYouCan.Tests.Integration", manifest.Tests.MissingReports);
        Assert.Equal(1, manifest.Tests.Total);
    }

    [Fact]
    public void Build_RecordsCoverageBelowTheThreshold()
    {
        using var directory = new TestTempDirectory();
        var manifest = Build(directory, threshold: 90);

        Assert.Equal(82, manifest.Coverage.LinePercent);
        Assert.False(manifest.Coverage.MeetsThreshold);
        Assert.Equal(90, manifest.Coverage.ThresholdPercent);
    }

    [Fact]
    public void Build_RecordsMissingAuditAsMissing()
    {
        using var directory = new TestTempDirectory();
        var trx = directory.Write("unit.trx", Trx);

        var manifest = ManifestBuilder.Build(new EvidenceBuildRequest
        {
            Context = new EvidenceBuildContext { Repository = "r", CommitSha = ProofFixtures.Commit, Branch = "main" },
            TestResultFiles = [trx],
            AuditReportPath = null,
            GeneratedAt = DateTimeOffset.UtcNow,
        });

        Assert.Equal(AuditStatus.Missing, manifest.DependencyAudit.Status);
        Assert.Null(manifest.Coverage.LinePercent);
    }

    [Fact]
    public void Manifest_RoundTripsThroughTheCanonicalJson()
    {
        using var directory = new TestTempDirectory();
        var manifest = Build(directory);
        var path = Path.Combine(directory.Path, "ci-evidence.json");

        var hash = EvidenceManifestFile.Write(manifest, path);
        var loaded = EvidenceManifestFile.Read(path);

        Assert.Equal(64, hash.Length);
        Assert.Equal(manifest.Repository, loaded.Repository);
        Assert.Equal(manifest.CommitSha, loaded.CommitSha);
        Assert.Equal(manifest.Tests.Total, loaded.Tests.Total);
        Assert.Equal(manifest.Coverage.LinePercent, loaded.Coverage.LinePercent);
        Assert.Equal(manifest.DependencyAudit.Status, loaded.DependencyAudit.Status);
        Assert.Equal(manifest.Attestations.BuildProvenance.SubjectDigest, loaded.Attestations.BuildProvenance.SubjectDigest);
        Assert.Equal(EvidenceJson.Serialize(manifest), EvidenceJson.Serialize(loaded));
    }

    [Fact]
    public void ManifestFile_RejectsOversizedDocuments()
    {
        using var directory = new TestTempDirectory();
        var path = directory.Write("huge.json", new string('x', (int)EvidenceManifestFile.MaxSizeBytes + 1));

        Assert.Throws<InvalidDataException>(() => EvidenceManifestFile.Read(path));
    }
}
