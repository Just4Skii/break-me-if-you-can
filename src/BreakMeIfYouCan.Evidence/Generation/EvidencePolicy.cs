using BreakMeIfYouCan.Evidence.Manifest;

namespace BreakMeIfYouCan.Evidence.Generation;

public sealed record PolicyViolation(string Id, string Detail);

/// <summary>
/// Release policy. This is the gate the workflow enforces before an image may be built,
/// and the same rules the runtime verifier applies. UNKNOWN is a violation: the release
/// process may not treat missing evidence as a pass.
/// </summary>
public static class EvidencePolicy
{
    public static IReadOnlyList<PolicyViolation> Evaluate(CiEvidenceManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var violations = new List<PolicyViolation>();

        if (!IsFullSha(manifest.CommitSha))
        {
            violations.Add(new PolicyViolation("commit.invalid", "The manifest is not bound to a full 40 character commit SHA."));
        }

        if (manifest.Tests.Status != PipelineStatus.Success)
        {
            violations.Add(new PolicyViolation(
                "tests.not_passing",
                "Test evidence is " + manifest.Tests.Status + ": " + manifest.Tests.Failed + " failed, " + manifest.Tests.MissingReports.Count + " missing report(s)."));
        }
        else if (manifest.Tests.Total == 0)
        {
            violations.Add(new PolicyViolation("tests.empty", "The test report contains zero results."));
        }

        if (manifest.Coverage.LinePercent is null)
        {
            violations.Add(new PolicyViolation("coverage.missing", "No line coverage figure was produced."));
        }
        else if (manifest.Coverage.LinePercent.Value + 0.0001 < manifest.Coverage.ThresholdPercent)
        {
            violations.Add(new PolicyViolation(
                "coverage.below_threshold",
                manifest.Coverage.LinePercent.Value.ToString("0.##") + "% is below the " + manifest.Coverage.ThresholdPercent.ToString("0.##") + "% threshold."));
        }

        if (manifest.DependencyAudit.Status != AuditStatus.Clean)
        {
            violations.Add(new PolicyViolation(
                "dependency.audit",
                "The dependency audit is " + manifest.DependencyAudit.Status + "."));
        }

        if (string.IsNullOrWhiteSpace(manifest.Image.Digest))
        {
            violations.Add(new PolicyViolation("image.digest_missing", "The container image digest was not recorded."));
        }

        if (manifest.Attestations.BuildProvenance.Status != PipelineStatus.Success)
        {
            violations.Add(new PolicyViolation(
                "attestation.not_verified",
                "Build provenance attestation verification is " + manifest.Attestations.BuildProvenance.Status + "."));
        }

        if (manifest.Workflow.Conclusion != PipelineStatus.Success)
        {
            violations.Add(new PolicyViolation("workflow.not_successful", "The recorded workflow conclusion is " + manifest.Workflow.Conclusion + "."));
        }

        return violations;
    }

    private static bool IsFullSha(string value) =>
        value.Length == 40 && value.All(Uri.IsHexDigit);
}
