using System.Globalization;
using BreakMeIfYouCan.Evidence.Manifest;

namespace BreakMeIfYouCan.Evidence.Verification;

/// <summary>
/// Turns a <see cref="ProofSnapshot"/> into a <see cref="ProofReport"/>.
///
/// The rules are deliberately boring and explicit:
///   * missing evidence is UNKNOWN,
///   * stale evidence is UNKNOWN,
///   * contradictory evidence, failed checks and mismatched digests are FAILED,
///   * VERIFIED requires every required check to be verified.
/// Nothing in this class can produce "verified" from a default value.
/// </summary>
public sealed class ProofVerifier
{
    private static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromMinutes(10);

    public ProofReport Verify(ProofSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var now = snapshot.Now == default ? DateTimeOffset.UtcNow : snapshot.Now;
        var evidence = snapshot.Evidence;
        var checks = new List<ProofCheck>();

        var expectedCommit = string.IsNullOrWhiteSpace(snapshot.RunningCommitSha)
            ? null
            : snapshot.RunningCommitSha.Trim();

        var expectedRepository = string.IsNullOrWhiteSpace(snapshot.ExpectedRepository)
            ? null
            : snapshot.ExpectedRepository.Trim();

        var expectedWorkflow = string.IsNullOrWhiteSpace(snapshot.ExpectedWorkflowFile)
            ? null
            : snapshot.ExpectedWorkflowFile.Trim();

        var builtDigest = DigestReference.Extract(evidence?.Image.Digest);
        var attestedDigest = DigestReference.Extract(
            evidence?.Attestations.BuildProvenance.SubjectDigest
            ?? evidence?.Attestations.QualityEvidence.SubjectDigest);
        var requestedDigest = DigestReference.Extract(snapshot.RequestedDeploymentDigest);
        var observedDigest = DigestReference.Extract(snapshot.Runtime?.Digest);

        var coverageThreshold = snapshot.CoverageThresholdPercent > 0
            ? snapshot.CoverageThresholdPercent
            : evidence?.Coverage.ThresholdPercent ?? 75;

        // ---------------------------------------------------------------- evidence

        checks.Add(evidence is null
            ? new ProofCheck
            {
                Id = "evidence.present",
                Label = "CI evidence manifest",
                State = EvidenceState.Unknown,
                Detail = "No CI evidence manifest could be loaded. Missing evidence can never be treated as a pass.",
            }
            : new ProofCheck
            {
                Id = "evidence.present",
                Label = "CI evidence manifest",
                State = EvidenceState.Verified,
                Detail = "Loaded from " + snapshot.EvidenceSource + ".",
            });

        var age = evidence is null ? (TimeSpan?)null : now - evidence.GeneratedAt;
        var stale = age is not null && (age.Value > snapshot.MaxEvidenceAge || age.Value < -ClockSkewTolerance);

        checks.Add(age is null
            ? Unknown("evidence.freshness", "Evidence freshness", "No manifest to date.")
            : stale
                ? Unknown(
                    "evidence.freshness",
                    "Evidence freshness",
                    "Evidence is " + Describe(age.Value) + " old or carries a future timestamp; the maximum accepted age is " +
                    Describe(snapshot.MaxEvidenceAge) + ". Stale evidence is reported as UNKNOWN, never as a pass.")
                : Verified(
                    "evidence.freshness",
                    "Evidence freshness",
                    "Evidence was generated " + Describe(age.Value) + " before this request (limit " +
                    Describe(snapshot.MaxEvidenceAge) + ")."));

        if (evidence is not null)
        {
            checks.Add(evidence.SchemaVersion == CiEvidenceManifest.CurrentSchemaVersion
                ? Verified("evidence.schema", "Evidence schema", "Schema version " + evidence.SchemaVersion + " is supported.")
                : Unknown(
                    "evidence.schema",
                    "Evidence schema",
                    "Schema version " + evidence.SchemaVersion + " is not understood by this deployment."));

            // commit binding
            if (expectedCommit is null)
            {
                checks.Add(Unknown("evidence.commit", "Evidence is bound to the running commit", "The running commit SHA could not be determined."));
            }
            else if (string.IsNullOrWhiteSpace(evidence.CommitSha))
            {
                checks.Add(Unknown("evidence.commit", "Evidence is bound to the running commit", "The manifest does not declare a commit SHA."));
            }
            else if (!string.Equals(evidence.CommitSha.Trim(), expectedCommit, StringComparison.OrdinalIgnoreCase))
            {
                checks.Add(Failed(
                    "evidence.commit",
                    "Evidence is bound to the running commit",
                    "Manifest commit " + Short(evidence.CommitSha) + " does not match the running commit " + Short(expectedCommit) + "."));
            }
            else
            {
                checks.Add(Verified(
                    "evidence.commit",
                    "Evidence is bound to the running commit",
                    "Manifest commit matches the commit compiled into the running assembly (" + Short(expectedCommit) + ")."));
            }

            // repository binding
            if (expectedRepository is null)
            {
                checks.Add(Unknown("evidence.repository", "Evidence is bound to the trusted repository", "No repository is configured for comparison."));
            }
            else if (string.IsNullOrWhiteSpace(evidence.Repository))
            {
                checks.Add(Unknown("evidence.repository", "Evidence is bound to the trusted repository", "The manifest does not declare a repository."));
            }
            else
            {
                checks.Add(string.Equals(evidence.Repository.Trim(), expectedRepository, StringComparison.OrdinalIgnoreCase)
                    ? Verified("evidence.repository", "Evidence is bound to the trusted repository", "Manifest repository matches " + expectedRepository + ".")
                    : Failed(
                        "evidence.repository",
                        "Evidence is bound to the trusted repository",
                        "Manifest repository " + evidence.Repository + " is not the configured repository " + expectedRepository + "."));
            }

            // workflow binding
            var workflowFile = evidence.Workflow.File;
            var workflowDetail = "Workflow " + Text(workflowFile) + " run " + Text(evidence.Workflow.RunId) + " (" + Text(evidence.Workflow.Event) + ").";
            if (expectedWorkflow is null)
            {
                checks.Add(Unknown("workflow.binding", "Workflow provenance", "No expected workflow file is configured."));
            }
            else if (string.IsNullOrWhiteSpace(workflowFile))
            {
                checks.Add(Unknown("workflow.binding", "Workflow provenance", "The manifest does not declare a workflow file."));
            }
            else if (!string.Equals(workflowFile.Trim(), expectedWorkflow, StringComparison.OrdinalIgnoreCase))
            {
                checks.Add(Failed(
                    "workflow.binding",
                    "Workflow provenance",
                    "Evidence came from " + workflowFile + " but the release must come from " + expectedWorkflow + "."));
            }
            else if (string.IsNullOrWhiteSpace(evidence.Workflow.RunId) || string.IsNullOrWhiteSpace(evidence.Workflow.RunUrl))
            {
                checks.Add(Unknown("workflow.binding", "Workflow provenance", "The manifest lacks a run id or run URL."));
            }
            else if (evidence.Workflow.Conclusion != PipelineStatus.Success)
            {
                checks.Add(Failed("workflow.binding", "Workflow provenance", "The recorded workflow conclusion is " + evidence.Workflow.Conclusion + ". " + workflowDetail));
            }
            else if (!IsReleaseEvent(evidence.Workflow.Event))
            {
                checks.Add(Failed(
                    "workflow.binding",
                    "Workflow provenance",
                    "Workflow event '" + evidence.Workflow.Event + "' is not a release-producing event."));
            }
            else
            {
                checks.Add(Verified("workflow.binding", "Workflow provenance", workflowDetail));
            }

            // build
            checks.Add(evidence.Build.Status switch
            {
                PipelineStatus.Success => Verified("build.status", "Release build", "Release build succeeded."),
                PipelineStatus.Failure => Failed("build.status", "Release build", "The recorded build failed."),
                _ => Unknown("build.status", "Release build", "No build status was recorded."),
            });

            // tests
            var tests = evidence.Tests;
            if (tests.Status == PipelineStatus.Missing || tests.Reports.Count == 0)
            {
                checks.Add(Unknown("tests.executed", "Automated tests", "No TRX test report was recorded. A missing report is not a passing report."));
            }
            else if (tests.Status == PipelineStatus.Failure)
            {
                checks.Add(Failed("tests.executed", "Automated tests", tests.Failed + " of " + tests.Total + " tests failed."));
            }
            else if (tests.Total <= 0)
            {
                checks.Add(Unknown("tests.executed", "Automated tests", "The test report parsed to zero results."));
            }
            else if (tests.Passed + tests.Failed + tests.Skipped != tests.Total)
            {
                checks.Add(Failed(
                    "tests.executed",
                    "Automated tests",
                    "The test report is internally inconsistent: passed + failed + skipped does not equal total."));
            }
            else if (tests.Failed > 0)
            {
                checks.Add(Failed("tests.executed", "Automated tests", tests.Failed + " tests failed."));
            }
            else
            {
                checks.Add(Verified(
                    "tests.executed",
                    "Automated tests",
                    tests.Passed + " of " + tests.Total + " tests passed across " + tests.Assemblies.Count + " assemblies (" +
                    tests.Skipped + " skipped)."));
            }

            // coverage
            var coverage = evidence.Coverage;
            var threshold = coverageThreshold;
            if (coverage.LinePercent is null)
            {
                checks.Add(Unknown("coverage.threshold", "Coverage threshold", "No coverage report was recorded."));
            }
            else if (coverage.LinePercent is < 0 or > 100)
            {
                checks.Add(Failed("coverage.threshold", "Coverage threshold", "The coverage figure is outside the possible range."));
            }
            else if (coverage.LinePercent.Value + 0.0001 < threshold)
            {
                checks.Add(Failed(
                    "coverage.threshold",
                    "Coverage threshold",
                    FormatPercent(coverage.LinePercent.Value) + " line coverage is below the required " + FormatPercent(threshold) + "."));
            }
            else if (!coverage.MeetsThreshold)
            {
                checks.Add(Failed(
                    "coverage.threshold",
                    "Coverage threshold",
                    "The pipeline recorded that the coverage gate did not pass."));
            }
            else
            {
                checks.Add(Verified(
                    "coverage.threshold",
                    "Coverage threshold",
                    FormatPercent(coverage.LinePercent.Value) + " line coverage meets the required " + FormatPercent(threshold) + "."));
            }

            // dependency audit
            var audit = evidence.DependencyAudit;
            var vulnerabilityCount = audit.DirectCount + audit.TransitiveCount;
            checks.Add(audit.Status switch
            {
                AuditStatus.Clean when vulnerabilityCount == 0 => Verified(
                    "dependency.audit",
                    "Dependency vulnerability audit",
                    "NuGet audit reported no known vulnerable packages. This is a dependency scan, not a security assessment."),
                AuditStatus.Clean => Failed(
                    "dependency.audit",
                    "Dependency vulnerability audit",
                    "The manifest claims a clean audit but lists " + vulnerabilityCount + " vulnerable packages."),
                AuditStatus.Vulnerable => Failed(
                    "dependency.audit",
                    "Dependency vulnerability audit",
                    vulnerabilityCount + " vulnerable package advisories were reported."),
                AuditStatus.Failed => Failed(
                    "dependency.audit",
                    "Dependency vulnerability audit",
                    "The audit command failed, so its result cannot be trusted."),
                _ => Unknown(
                    "dependency.audit",
                    "Dependency vulnerability audit",
                    "No dependency audit report was recorded."),
            });
        }
        else
        {
            checks.Add(Unknown("evidence.commit", "Evidence is bound to the running commit", "No manifest."));
            checks.Add(Unknown("evidence.repository", "Evidence is bound to the trusted repository", "No manifest."));
            checks.Add(Unknown("workflow.binding", "Workflow provenance", "No manifest."));
            checks.Add(Unknown("build.status", "Release build", "No manifest."));
            checks.Add(Unknown("tests.executed", "Automated tests", "No manifest."));
            checks.Add(Unknown("coverage.threshold", "Coverage threshold", "No manifest."));
            checks.Add(Unknown("dependency.audit", "Dependency vulnerability audit", "No manifest."));
        }

        // ---------------------------------------------------------------- digests

        checks.Add(CompareDigests("digest.build_vs_attested", "Built digest vs attested digest", builtDigest, attestedDigest, "built image", "attestation subject"));
        checks.Add(CompareDigests("digest.attested_vs_requested", "Attested digest vs requested deployment digest", attestedDigest, requestedDigest, "attestation subject", "deployment request"));
        checks.Add(CompareDigests("digest.requested_vs_observed", "Requested digest vs observed running digest", requestedDigest, observedDigest, "deployment request", "observed runtime"));
        checks.Add(CompareDigests("digest.built_vs_observed", "Built digest vs observed running digest", builtDigest, observedDigest, "built image", "observed runtime"));

        checks.Add(BuildChainCheck(builtDigest, attestedDigest, requestedDigest, observedDigest));

        // ---------------------------------------------------------------- attestation

        var attestation = snapshot.Attestation;
        var recorded = evidence?.Attestations.BuildProvenance;

        if (evidence is null)
        {
            checks.Add(Unknown("attestation.recorded", "Attestation recorded by CI", "No manifest."));
        }
        else
        {
            checks.Add(recorded?.Status switch
            {
                PipelineStatus.Success => Verified(
                    "attestation.recorded",
                    "Attestation recorded by CI",
                    "The pipeline recorded a successful " + Text(recorded.PredicateType) + " verification."),
                PipelineStatus.Failure => Failed(
                    "attestation.recorded",
                    "Attestation recorded by CI",
                    "The pipeline recorded a failed attestation verification (exit code " + (recorded?.ExitCode ?? 0) + ")."),
                _ => Unknown("attestation.recorded", "Attestation recorded by CI", "The pipeline did not record an attestation verification result."),
            });

            if (recorded?.Status == PipelineStatus.Success && builtDigest is not null && attestedDigest is null)
            {
                checks.Add(Unknown(
                    "attestation.subject",
                    "Attestation subject digest",
                    "The pipeline verified an attestation but did not record the subject digest."));
            }
            else if (recorded?.Status == PipelineStatus.Success
                && DigestReference.Extract(recorded?.SubjectDigest) is { } stated
                && builtDigest is not null
                && !string.Equals(stated, builtDigest, StringComparison.Ordinal))
            {
                checks.Add(Failed(
                    "attestation.subject",
                    "Attestation subject digest",
                    "The verified attestation covers " + stated + " but the built image is " + builtDigest + "."));
            }
            else if (recorded?.Status == PipelineStatus.Success)
            {
                checks.Add(Verified("attestation.subject", "Attestation subject digest", "The verified attestation is bound to the built image digest."));
            }
        }

        if (attestation is null || !attestation.Available)
        {
            checks.Add(Unknown(
                "attestation.independent",
                "Attestation queried from GitHub",
                "The GitHub attestations API could not be queried" +
                (string.IsNullOrWhiteSpace(attestation?.Failure) ? "." : ": " + Punctuate(attestation.Failure))));
        }
        else if (!attestation.Found)
        {
            checks.Add(Failed(
                "attestation.independent",
                "Attestation queried from GitHub",
                "No attestation exists on GitHub for the image digest. Attestation is required for release."));
        }
        else if (builtDigest is null || DigestReference.Extract(attestation.SubjectDigest) is null)
        {
            checks.Add(Unknown(
                "attestation.independent",
                "Attestation queried from GitHub",
                "An attestation exists, but the built digest or the attested subject digest is unknown, so the two cannot be tied together."));
        }
        else
        {
            var problems = new List<string>();
            if (!string.Equals(DigestReference.Extract(attestation.SubjectDigest), builtDigest, StringComparison.Ordinal))
            {
                problems.Add("the attested subject digest does not match the built digest");
            }

            if (expectedCommit is not null && !string.Equals(attestation.CommitSha, expectedCommit, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add("the attested commit does not match the running commit");
            }

            if (expectedRepository is not null
                && !string.Equals(attestation.Repository, expectedRepository, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add("the attested repository does not match the trusted repository");
            }

            if (expectedWorkflow is not null
                && attestation.WorkflowRef is not null
                && !attestation.WorkflowRef.Contains(expectedWorkflow, StringComparison.OrdinalIgnoreCase))
            {
                problems.Add("the attested workflow ref does not match the release workflow");
            }

            checks.Add(problems.Count == 0
                ? Verified(
                    "attestation.independent",
                    "Attestation queried from GitHub",
                    "GitHub reports " + (attestation.AttestationCount ?? 0) + " attestation(s) for the running digest, bound to the expected repository, commit and workflow.")
                : Failed(
                    "attestation.independent",
                    "Attestation queried from GitHub",
                    "Attestation binding failed: " + string.Join("; ", problems) + "."));
        }

        // ---------------------------------------------------------------- deployment

        var runtime = snapshot.Runtime;
        if (runtime is null || !runtime.Available)
        {
            checks.Add(Unknown(
                "deployment.health",
                "Deployment health",
                "The runtime platform could not be queried" +
                (string.IsNullOrWhiteSpace(runtime?.Failure) ? "." : ": " + Punctuate(runtime.Failure))));
        }
        else if (runtime.Healthy == false)
        {
            checks.Add(Failed(
                "deployment.health",
                "Deployment health",
                "The platform reports the machine as unhealthy" +
                (string.IsNullOrWhiteSpace(runtime.HealthDetail) ? "." : ": " + runtime.HealthDetail + ".")));
        }
        else if (runtime.Healthy is null)
        {
            checks.Add(Unknown("deployment.health", "Deployment health", "The platform did not report machine health."));
        }
        else
        {
            checks.Add(Verified("deployment.health", "Deployment health", "The platform reports the machine as healthy."));
        }

        // ---------------------------------------------------------------- verdict

        var required = checks.Where(c => c.Required).ToList();
        var failedChecks = required.Where(c => c.State == EvidenceState.Failed).ToList();
        var unknownChecks = required.Where(c => c.State == EvidenceState.Unknown).ToList();
        var verifiedChecks = required.Count - failedChecks.Count - unknownChecks.Count;

        var verdict = failedChecks.Count > 0
            ? EvidenceState.Failed
            : unknownChecks.Count > 0
                ? EvidenceState.Unknown
                : EvidenceState.Verified;

        var summary = verdict switch
        {
            EvidenceState.Verified =>
                "All " + required.Count + " required checks are verified for commit " + Short(expectedCommit ?? evidence?.CommitSha) + ".",
            EvidenceState.Failed =>
                failedChecks.Count + " of " + required.Count + " required checks failed: " +
                string.Join(", ", failedChecks.Select(c => c.Label)) + ".",
            _ =>
                unknownChecks.Count + " of " + required.Count +
                " required checks cannot be verified. Missing evidence is reported as UNKNOWN, never as a pass.",
        };

        var verifyCommand = evidence?.Attestations.BuildProvenance.Command;
        var ociReference = DigestReference.BuildOciReference(evidence?.Image.Reference, builtDigest);
        if (string.IsNullOrWhiteSpace(verifyCommand) && ociReference is not null && !string.IsNullOrWhiteSpace(evidence?.Repository))
        {
            verifyCommand = "gh attestation verify " + ociReference + " --repo " + evidence.Repository +
                            " --predicate-type https://slsa.dev/provenance/v1";
        }

        return new ProofReport
        {
            State = verdict,
            Summary = summary,
            GeneratedAt = now,
            EvidenceSource = snapshot.EvidenceSource,
            EvidenceFetchedAt = snapshot.EvidenceFetchedAt,
            Freshness = new FreshnessInfo
            {
                EvidenceGeneratedAt = evidence?.GeneratedAt,
                AgeSeconds = age?.TotalSeconds,
                MaxAgeSeconds = snapshot.MaxEvidenceAge.TotalSeconds,
                Stale = stale,
                Detail = evidence is null ? null : "Manifest generated at " + evidence.GeneratedAt.ToString("O", CultureInfo.InvariantCulture) + ".",
            },
            Source = new SourceInfo
            {
                Repository = evidence?.Repository ?? string.Empty,
                CommitSha = evidence?.CommitSha ?? string.Empty,
                ShortCommitSha = Short(evidence?.CommitSha ?? expectedCommit),
                Branch = evidence?.Branch ?? string.Empty,
                RunningCommitSha = snapshot.RunningCommitSha ?? string.Empty,
            },
            Workflow = new WorkflowInfo
            {
                State = CheckState(checks, "workflow.binding"),
                Name = evidence?.Workflow.Name ?? string.Empty,
                File = evidence?.Workflow.File ?? string.Empty,
                RunId = evidence?.Workflow.RunId ?? string.Empty,
                RunAttempt = evidence?.Workflow.RunAttempt ?? string.Empty,
                RunUrl = evidence?.Workflow.RunUrl ?? string.Empty,
                Event = evidence?.Workflow.Event ?? string.Empty,
                Conclusion = evidence?.Workflow.Conclusion.ToString() ?? string.Empty,
            },
            Build = new BuildInfo
            {
                State = CheckState(checks, "build.status"),
                Configuration = evidence?.Build.Configuration ?? string.Empty,
                Framework = evidence?.Build.Framework ?? string.Empty,
                DotnetVersion = evidence?.Build.DotnetVersion ?? string.Empty,
                StartedAt = evidence?.Build.StartedAt,
                CompletedAt = evidence?.Build.CompletedAt,
                Runner = evidence?.Build.Runner ?? string.Empty,
            },
            Tests = new TestInfo
            {
                State = CheckState(checks, "tests.executed"),
                Total = evidence?.Tests.Total ?? 0,
                Passed = evidence?.Tests.Passed ?? 0,
                Failed = evidence?.Tests.Failed ?? 0,
                Skipped = evidence?.Tests.Skipped ?? 0,
                DurationSeconds = evidence?.Tests.DurationSeconds ?? 0,
                Assemblies = evidence?.Tests.Assemblies ?? [],
                Reports = evidence?.Tests.Reports ?? [],
            },
            Coverage = new CoverageInfo
            {
                State = CheckState(checks, "coverage.threshold"),
                LinePercent = evidence?.Coverage.LinePercent,
                BranchPercent = evidence?.Coverage.BranchPercent,
                ThresholdPercent = coverageThreshold,
                MeetsThreshold = evidence?.Coverage.LinePercent is { } line
                    ? line + 0.0001 >= coverageThreshold
                    : null,
                LinesCovered = evidence?.Coverage.LinesCovered,
                LinesValid = evidence?.Coverage.LinesValid,
                ReportArtifact = evidence?.Coverage.ReportArtifact,
            },
            DependencyAudit = new DependencyAuditInfo
            {
                State = CheckState(checks, "dependency.audit"),
                Status = (evidence?.DependencyAudit.Status ?? AuditStatus.Missing).ToString().ToLowerInvariant(),
                DirectCount = evidence?.DependencyAudit.DirectCount ?? 0,
                TransitiveCount = evidence?.DependencyAudit.TransitiveCount ?? 0,
                BySeverity = evidence?.DependencyAudit.BySeverity ?? new Dictionary<string, int>(),
                Packages = evidence?.DependencyAudit.Packages ?? [],
                Command = evidence?.DependencyAudit.Command ?? string.Empty,
                Scope = evidence?.SecurityPosture.Disclaimer ?? string.Empty,
            },
            Image = new ImageInfo
            {
                Reference = evidence?.Image.Reference ?? string.Empty,
                BuiltDigest = builtDigest,
                AttestedDigest = attestedDigest,
                RequestedDeploymentDigest = requestedDigest,
                ObservedRunningDigest = observedDigest,
                Verify = new OciCommandInfo
                {
                    VerifyCommand = verifyCommand,
                    OciReference = ociReference,
                },
            },
            Deployment = new DeploymentInfo
            {
                State = CheckState(checks, "deployment.health"),
                Health = runtime is null || !runtime.Available
                    ? "unknown"
                    : runtime.Healthy switch { true => "healthy", false => "unhealthy", null => "unknown" },
                Detail = runtime?.HealthDetail ?? runtime?.Failure,
                ObservedAt = runtime?.ObservedAt,
                MachineCreatedAt = runtime?.MachineCreatedAt,
                AppName = runtime?.AppName,
                MachineId = runtime?.MachineId,
                Region = runtime?.Region,
                Source = runtime?.Source ?? "unknown",
                SourceUrl = runtime?.SourceUrl,
            },
            Attestation = new AttestationInfo
            {
                State = CheckState(checks, "attestation.independent"),
                SubjectDigest = attestedDigest,
                PredicateType = evidence?.Attestations.BuildProvenance.PredicateType,
                Repository = attestation?.Repository ?? evidence?.Attestations.BuildProvenance.Repository,
                CommitSha = attestation?.CommitSha ?? evidence?.Attestations.BuildProvenance.CommitSha,
                WorkflowRef = attestation?.WorkflowRef ?? evidence?.Attestations.BuildProvenance.WorkflowRef,
                RunId = attestation?.RunId ?? evidence?.Attestations.BuildProvenance.RunId,
                ObservedAt = attestation?.ObservedAt,
                Source = attestation?.Source ?? "ci-record",
                SourceUrl = attestation?.SourceUrl,
                Command = verifyCommand,
                Note = "The CI record and the live GitHub query are reported separately. A green scan is not a security assessment.",
            },
            SecurityPosture = new SecurityPostureInfo
            {
                Disclaimer = evidence?.SecurityPosture.Disclaimer ??
                             "A clean dependency scan covers known advisories in declared NuGet packages. It is not a penetration test or a complete security assessment.",
                ScanScope = "NuGet package advisories (direct and transitive) reported by the .NET SDK audit command.",
                ImplementedControls = evidence?.SecurityPosture.ImplementedControls ?? [],
                NotAssessed = evidence?.SecurityPosture.NotAssessed ?? [],
            },
            Checks = checks,
            Notes = snapshot.Notes,
            VerifyCommand = verifyCommand,
        };
    }

    private static ProofCheck BuildChainCheck(string? built, string? attested, string? requested, string? observed)
    {
        var missing = new List<string>();
        if (built is null) missing.Add("built");
        if (attested is null) missing.Add("attested");
        if (requested is null) missing.Add("requested deployment");
        if (observed is null) missing.Add("observed runtime");

        if (missing.Count > 0)
        {
            return Unknown(
                "digest.chain",
                "Digest chain: build to attestation to deployment to runtime",
                "Incomplete chain, missing: " + string.Join(", ", missing) + ". All four digests are required to verify a release.");
        }

        var all = new[] { built, attested, requested, observed };
        var distinct = all.Distinct(StringComparer.Ordinal).Count();
        return distinct == 1
            ? Verified(
                "digest.chain",
                "Digest chain: build to attestation to deployment to runtime",
                "All four digests are identical: " + built + ".")
            : Failed(
                "digest.chain",
                "Digest chain: build to attestation to deployment to runtime",
                "The four digests are not identical, so the deployed image cannot be the attested build.");
    }

    private static ProofCheck CompareDigests(string id, string label, string? left, string? right, string leftName, string rightName)
    {
        if (left is null || right is null)
        {
            var missing = left is null && right is null
                ? "both values are unknown"
                : (left is null ? "the " + leftName + " digest is unknown" : "the " + rightName + " digest is unknown");
            return Unknown(id, label, "Cannot compare: " + missing + ".");
        }

        return string.Equals(left, right, StringComparison.Ordinal)
            ? Verified(id, label, "The " + leftName + " and " + rightName + " agree (" + left + ").")
            : Failed(
                id,
                label,
                "The " + leftName + " (" + left + ") and " + rightName + " (" + right + ") disagree.");
    }

    private static bool IsReleaseEvent(string? value) =>
        value is not null &&
        (value.Equals("push", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("workflow_dispatch", StringComparison.OrdinalIgnoreCase) ||
         value.Equals("release", StringComparison.OrdinalIgnoreCase));

    private static EvidenceState CheckState(IReadOnlyList<ProofCheck> checks, string id) =>
        checks.FirstOrDefault(c => c.Id == id)?.State ?? EvidenceState.Unknown;

    private static ProofCheck Verified(string id, string label, string detail) =>
        new() { Id = id, Label = label, State = EvidenceState.Verified, Detail = detail };

    private static ProofCheck Failed(string id, string label, string detail) =>
        new() { Id = id, Label = label, State = EvidenceState.Failed, Detail = detail };

    private static ProofCheck Unknown(string id, string label, string detail) =>
        new() { Id = id, Label = label, State = EvidenceState.Unknown, Detail = detail };

    private static string Punctuate(string value) =>
        value.Length > 0 && value[^1] is '.' or '!' or '?' ? value : value + ".";

    private static string Short(string? sha) =>
        string.IsNullOrWhiteSpace(sha) ? "unknown" : sha.Length <= 12 ? sha : sha[..12];

    private static string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "unknown" : value;

    private static string FormatPercent(double value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture) + "%";

    private static string Describe(TimeSpan span)
    {
        var value = span.Duration();
        if (value.TotalDays >= 1)
        {
            return value.TotalDays.ToString("0.#", CultureInfo.InvariantCulture) + " days";
        }

        if (value.TotalHours >= 1)
        {
            return value.TotalHours.ToString("0.#", CultureInfo.InvariantCulture) + " hours";
        }

        if (value.TotalMinutes >= 1)
        {
            return value.TotalMinutes.ToString("0.#", CultureInfo.InvariantCulture) + " minutes";
        }

        return value.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " seconds";
    }
}
