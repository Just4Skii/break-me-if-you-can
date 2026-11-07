using System.Globalization;
using BreakMeIfYouCan.Evidence.Generation;
using BreakMeIfYouCan.Evidence.Manifest;

namespace BreakMeIfYouCan.EvidenceGenerator;

/// <summary>
/// CI evidence generator. Reads the run metadata written by the workflow plus the real
/// build outputs, writes the canonical ci-evidence.json manifest, and (optionally)
/// enforces release policy with a non-zero exit code.
///
/// The manifest is always written before the policy exit code is returned, so the
/// workflow can upload evidence even when the build failed.
/// </summary>
internal static class Program
{
    private const string Usage = """
        Usage: BreakMeIfYouCan.EvidenceGenerator --context <file> --output <file> [options]

        Required:
          --context <path>              Run metadata JSON written by the workflow.
          --output <path>               Destination for ci-evidence.json.

        Options:
          --test-results <path>         TRX file or directory (recursive). Repeatable.
          --coverage <path>             Cobertura XML file or directory. Repeatable.
          --audit <path>                NuGet audit JSON report.
          --audit-exit-code <int>       Exit code of the audit command (default 0).
          --coverage-threshold <double> Minimum line coverage percentage (default 75).
          --expected-assemblies <list>  Comma separated test assemblies whose TRX is required.
          --enforce                     Exit with code 2 when release policy is violated.
          --help                        Show this message.
        """;

    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine("error: " + exception.Message);
            Console.Error.WriteLine(Usage);
            return 1;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("error: " + exception.Message);
            return 1;
        }
    }

    private static int Run(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.Ordinal))
        {
            Console.WriteLine(Usage);
            return args.Length == 0 ? 1 : 0;
        }

        var options = Parse(args);

        var contextPath = options.GetRequired("--context");
        var outputPath = options.GetRequired("--output");

        if (!File.Exists(contextPath))
        {
            throw new ArgumentException("Context file not found: " + contextPath);
        }

        var context = EvidenceJson.Deserialize(File.ReadAllText(contextPath))
                      ?? throw new InvalidDataException("The context file did not deserialize.");

        var buildContext = new EvidenceBuildContext
        {
            Repository = context.Repository,
            CommitSha = context.CommitSha,
            Branch = context.Branch,
            Workflow = context.Workflow,
            Build = context.Build,
            Image = context.Image,
            Attestations = context.Attestations,
        };

        var testFiles = Expand(options.Values("--test-results"), "*.trx");
        var coverageFiles = Expand(options.Values("--coverage"), "*.xml");
        var auditPath = options.Value("--audit");
        var auditExitCode = int.TryParse(options.Value("--audit-exit-code"), CultureInfo.InvariantCulture, out var code) ? code : 0;
        var threshold = double.TryParse(options.Value("--coverage-threshold"), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedThreshold)
            ? parsedThreshold
            : 75;
        var expected = (options.Value("--expected-assemblies") ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var manifest = ManifestBuilder.Build(new EvidenceBuildRequest
        {
            Context = buildContext,
            TestResultFiles = testFiles,
            CoverageReportFiles = coverageFiles,
            AuditReportPath = auditPath,
            AuditExitCode = auditExitCode,
            CoverageThresholdPercent = threshold,
            ExpectedTestAssemblies = expected,
            GeneratedAt = DateTimeOffset.UtcNow,
        });

        var sha = EvidenceManifestFile.Write(manifest, outputPath);

        Console.WriteLine("ci-evidence.json written to " + outputPath);
        Console.WriteLine("  sha256      : " + sha);
        Console.WriteLine("  commit      : " + manifest.CommitSha);
        Console.WriteLine("  tests       : " + manifest.Tests.Passed + "/" + manifest.Tests.Total + " passed, " + manifest.Tests.Failed + " failed");
        Console.WriteLine("  coverage    : " + Format(manifest.Coverage.LinePercent) + " line / " + Format(manifest.Coverage.BranchPercent) + " branch (threshold " + manifest.Coverage.ThresholdPercent.ToString("0.##", CultureInfo.InvariantCulture) + "%)");
        Console.WriteLine("  audit       : " + manifest.DependencyAudit.Status);
        Console.WriteLine("  image       : " + (manifest.Image.Digest ?? "no digest"));
        Console.WriteLine("  attestation : " + manifest.Attestations.BuildProvenance.Status);

        var violations = EvidencePolicy.Evaluate(manifest);
        if (violations.Count == 0)
        {
            Console.WriteLine("policy      : satisfied");
            return 0;
        }

        Console.Error.WriteLine("policy      : " + violations.Count + " violation(s)");
        foreach (var violation in violations)
        {
            Console.Error.WriteLine("  [" + violation.Id + "] " + violation.Detail);
        }

        return options.Has("--enforce") ? 2 : 0;
    }

    private static string? Format(double? value) =>
        value is null ? "unknown" : value.Value.ToString("0.##", CultureInfo.InvariantCulture) + "%";

    private static List<string> Expand(IEnumerable<string> inputs, string pattern)
    {
        var files = new List<string>();
        foreach (var input in inputs)
        {
            if (File.Exists(input))
            {
                files.Add(input);
            }
            else if (Directory.Exists(input))
            {
                files.AddRange(Directory.EnumerateFiles(input, pattern, SearchOption.AllDirectories));
            }
        }

        return files.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.Ordinal).ToList();
    }

    private static Options Parse(string[] args)
    {
        var options = new Options();
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (!argument.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Unexpected argument: " + argument);
            }

            if (argument is "--enforce" or "--help")
            {
                options.Flags.Add(argument);
                continue;
            }

            if (index + 1 >= args.Length)
            {
                throw new ArgumentException("Missing value for " + argument);
            }

            options.Add(argument, args[++index]);
        }

        return options;
    }

    private sealed class Options
    {
        private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);

        public HashSet<string> Flags { get; } = new(StringComparer.Ordinal);

        public void Add(string key, string value)
        {
            if (!_values.TryGetValue(key, out var list))
            {
                list = [];
                _values[key] = list;
            }

            list.Add(value);
        }

        public bool Has(string flag) => Flags.Contains(flag);

        public string? Value(string key) => _values.TryGetValue(key, out var list) ? list[^1] : null;

        public IEnumerable<string> Values(string key) => _values.TryGetValue(key, out var list) ? list : [];

        public string GetRequired(string key) =>
            Value(key) ?? throw new ArgumentException("Required option missing: " + key);
    }
}
