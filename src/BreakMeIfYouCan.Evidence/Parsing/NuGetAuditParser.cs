using System.Text.Json;
using BreakMeIfYouCan.Evidence.Manifest;

namespace BreakMeIfYouCan.Evidence.Parsing;

/// <summary>
/// Parses the JSON produced by:
///   dotnet list package --vulnerable --include-transitive --format json
/// The command's exit code is part of the evidence: a failed audit is not a clean audit.
/// </summary>
public static class NuGetAuditParser
{
    public const string AuditCommand = "dotnet list package --vulnerable --include-transitive --format json";

    public static DependencyAuditEvidence Parse(string? path, int exitCode)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return new DependencyAuditEvidence
            {
                Status = AuditStatus.Missing,
                Command = AuditCommand,
                Note = "No audit report was found. A missing audit is reported as unknown, never as clean.",
            };
        }

        if (exitCode != 0)
        {
            return new DependencyAuditEvidence
            {
                Status = AuditStatus.Failed,
                Command = AuditCommand,
                Note = "The audit command exited with code " + exitCode + ", so its output cannot be treated as a clean result.",
            };
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            return new DependencyAuditEvidence
            {
                Status = AuditStatus.Failed,
                Command = AuditCommand,
                Note = "The audit report could not be parsed as JSON.",
            };
        }

        using (document)
        {
            var packages = new List<VulnerablePackage>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (document.RootElement.TryGetProperty("projects", out var projects) && projects.ValueKind == JsonValueKind.Array)
            {
                foreach (var project in projects.EnumerateArray())
                {
                    if (!project.TryGetProperty("frameworks", out var frameworks) || frameworks.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var framework in frameworks.EnumerateArray())
                    {
                        Collect(framework, "topLevelPackages", transitive: false, packages, seen);
                        Collect(framework, "transitivePackages", transitive: true, packages, seen);
                    }
                }
            }

            var bySeverity = packages
                .GroupBy(p => NormalizeSeverity(p.Severity))
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            var direct = packages.Count(p => !p.Transitive);
            var transitive = packages.Count(p => p.Transitive);

            return new DependencyAuditEvidence
            {
                Status = packages.Count == 0 ? AuditStatus.Clean : AuditStatus.Vulnerable,
                DirectCount = direct,
                TransitiveCount = transitive,
                BySeverity = bySeverity,
                Packages = packages
                    .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.Version, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Command = AuditCommand,
                Note = packages.Count == 0
                    ? "No advisory was reported for the declared and transitive NuGet packages. This covers known advisories only; it is not a security assessment."
                    : "Advisories were reported. The release is blocked until each advisory is resolved or explicitly risk-accepted.",
            };
        }
    }

    private static void Collect(
        JsonElement framework,
        string property,
        bool transitive,
        List<VulnerablePackage> packages,
        HashSet<string> seen)
    {
        if (!framework.TryGetProperty(property, out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("vulnerabilities", out var vulnerabilities) || vulnerabilities.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var id = entry.TryGetProperty("id", out var idValue) ? idValue.GetString() ?? "unknown" : "unknown";
            var version = entry.TryGetProperty("resolvedVersion", out var versionValue) ? versionValue.GetString() ?? "unknown" : "unknown";

            foreach (var vulnerability in vulnerabilities.EnumerateArray())
            {
                var severity = vulnerability.TryGetProperty("severity", out var severityValue) ? severityValue.GetString() ?? "unknown" : "unknown";
                var url = vulnerability.TryGetProperty("advisoryurl", out var urlValue) ? urlValue.GetString() : null;
                var key = id + "|" + version + "|" + severity + "|" + (url ?? string.Empty);

                if (!seen.Add(key))
                {
                    continue;
                }

                packages.Add(new VulnerablePackage
                {
                    Id = id,
                    Version = version,
                    Severity = NormalizeSeverity(severity),
                    AdvisoryUrl = url,
                    Transitive = transitive,
                });
            }
        }
    }

    private static string NormalizeSeverity(string value) => value.Trim().ToLowerInvariant() switch
    {
        "critical" => "critical",
        "high" => "high",
        "moderate" or "medium" => "moderate",
        "low" => "low",
        var other => other,
    };
}
