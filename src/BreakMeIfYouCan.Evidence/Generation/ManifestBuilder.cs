using BreakMeIfYouCan.Evidence.Manifest;
using BreakMeIfYouCan.Evidence.Parsing;

namespace BreakMeIfYouCan.Evidence.Generation;

/// <summary>
/// Builds the canonical evidence manifest from build outputs. The builder never invents
/// a value: anything the pipeline did not produce stays missing, and the verifier will
/// report it as UNKNOWN.
/// </summary>
public static class ManifestBuilder
{
    public static CiEvidenceManifest Build(EvidenceBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Context);

        var context = request.Context;
        var tests = TrxParser.Parse(request.TestResultFiles);

        var reportedAssemblies = new HashSet<string>(
            tests.Assemblies.Select(a => a.Assembly),
            StringComparer.OrdinalIgnoreCase);

        var missing = request.ExpectedTestAssemblies
            .Where(expected => !reportedAssemblies.Contains(expected))
            .OrderBy(expected => expected, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (missing.Count > 0)
        {
            tests = tests with
            {
                Status = PipelineStatus.Missing,
                MissingReports = missing,
            };
        }
        else if (tests.Status == PipelineStatus.Success && request.ExpectedTestAssemblies.Count > 0)
        {
            tests = tests with { Status = PipelineStatus.Success };
        }

        var coverage = CoverageParser.Parse(request.CoverageReportFiles);
        var audit = NuGetAuditParser.Parse(request.AuditReportPath, request.AuditExitCode);

        var coverageEvidence = new CoverageEvidence
        {
            LinePercent = coverage.LinePercent,
            BranchPercent = coverage.BranchPercent,
            ThresholdPercent = request.CoverageThresholdPercent,
            MeetsThreshold = coverage.LinePercent is { } line && line + 0.0001 >= request.CoverageThresholdPercent,
            ReportArtifact = coverage.Reports.Count == 0 ? null : string.Join(", ", coverage.Reports),
            LinesCovered = coverage.LinesCovered,
            LinesValid = coverage.LinesValid,
        };

        return new CiEvidenceManifest
        {
            SchemaVersion = CiEvidenceManifest.CurrentSchemaVersion,
            Repository = context.Repository,
            CommitSha = context.CommitSha,
            Branch = context.Branch,
            GeneratedAt = request.GeneratedAt,
            Workflow = context.Workflow,
            Build = context.Build,
            Tests = tests,
            Coverage = coverageEvidence,
            DependencyAudit = audit,
            Image = context.Image,
            Attestations = context.Attestations,
            SecurityPosture = SecurityPosture(),
            Artifacts =
            [
                new EvidenceArtifact { Name = "test-results" },
                new EvidenceArtifact { Name = "coverage-report" },
                new EvidenceArtifact { Name = "nuget-audit" },
                new EvidenceArtifact { Name = "ci-evidence" },
            ],
        };
    }

    public static SecurityPostureEvidence SecurityPosture() => new()
    {
        Disclaimer =
            "A clean dependency scan covers known advisories in the NuGet packages this build resolves. " +
            "It is not a penetration test, a threat model or a complete security assessment. " +
            "No visitor-supplied code is executed, no container is created per visitor, and pull requests never reach production.",
        ImplementedControls =
        [
            "Input validation and bounded request models on every arena endpoint.",
            "Per-visitor sandbox records with a signed, HttpOnly identity cookie; no registration.",
            "Rate limiting and a global concurrency cap on transfer execution.",
            "Parameterised SQL through Entity Framework Core; no string-concatenated SQL.",
            "Double-entry ledger with a database CHECK constraint preventing negative balances.",
            "Unique (sandbox, idempotency key) constraint preventing duplicate transfers.",
            "Security headers (CSP, X-Content-Type-Options, Referrer-Policy, frame denial).",
            "Safe exception handling: no stack traces or connection strings in responses.",
            "Structured logs with request correlation ids.",
            "Pinned GitHub Actions, least-privilege workflow permissions and image digest deployment.",
        ],
        NotAssessed =
        [
            "Penetration testing and adversarial runtime exploitation.",
            "Availability under sustained denial-of-service load.",
            "Third-party hosting and registry infrastructure security.",
            "Supply-chain integrity of the NuGet ecosystem beyond reported advisories.",
        ],
    };
}
