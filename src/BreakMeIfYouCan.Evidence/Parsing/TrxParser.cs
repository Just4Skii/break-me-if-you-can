using System.Globalization;
using System.Xml.Linq;
using BreakMeIfYouCan.Evidence.Manifest;

namespace BreakMeIfYouCan.Evidence.Parsing;

/// <summary>
/// Parses VSTest TRX reports into aggregate evidence. Only real result elements are
/// counted; a file that contains no results is reported as such rather than as a pass.
/// </summary>
public static class TrxParser
{
    public static TestEvidence Parse(IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        var assemblies = new Dictionary<string, MutableAssembly>(StringComparer.OrdinalIgnoreCase);
        var reports = new List<string>();
        var anyResults = false;

        foreach (var file in files.Where(File.Exists).OrderBy(f => f, StringComparer.Ordinal))
        {
            var document = Load(file);
            if (document is null)
            {
                continue;
            }

            reports.Add(Path.GetFileName(file));
            var assemblyByTestId = ReadTestDefinitions(document);
            var fileResults = 0;

            foreach (var result in Descendants(document, "UnitTestResult"))
            {
                fileResults++;
                anyResults = true;

                var testId = Attribute(result, "testId");
                var assembly = assemblyByTestId.TryGetValue(testId, out var found)
                    ? found
                    : "unknown";

                if (!assemblies.TryGetValue(assembly, out var aggregate))
                {
                    aggregate = new MutableAssembly(assembly);
                    assemblies[assembly] = aggregate;
                }

                aggregate.Add(Attribute(result, "outcome"), ParseDuration(Attribute(result, "duration")));
            }

            if (fileResults == 0)
            {
                assemblies.TryAdd(Path.GetFileNameWithoutExtension(file), new MutableAssembly(Path.GetFileNameWithoutExtension(file)));
            }
        }

        var perAssembly = assemblies.Values
            .OrderBy(a => a.Assembly, StringComparer.Ordinal)
            .Select(a => new TestAssemblyEvidence
            {
                Assembly = a.Assembly,
                Total = a.Total,
                Passed = a.Passed,
                Failed = a.Failed,
                Skipped = a.Skipped,
                DurationSeconds = Math.Round(a.Duration.TotalSeconds, 3),
            })
            .ToList();

        var total = perAssembly.Sum(a => a.Total);
        var failed = perAssembly.Sum(a => a.Failed);

        var status = !anyResults || reports.Count == 0
            ? PipelineStatus.Missing
            : failed > 0
                ? PipelineStatus.Failure
                : PipelineStatus.Success;

        return new TestEvidence
        {
            Status = status,
            Total = total,
            Passed = perAssembly.Sum(a => a.Passed),
            Failed = failed,
            Skipped = perAssembly.Sum(a => a.Skipped),
            DurationSeconds = Math.Round(perAssembly.Sum(a => a.DurationSeconds), 3),
            Assemblies = perAssembly,
            Reports = reports,
        };
    }

    private static XDocument? Load(string file)
    {
        try
        {
            return XDocument.Load(file, LoadOptions.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> ReadTestDefinitions(XDocument document)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var unitTest in Descendants(document, "UnitTest"))
        {
            var id = Attribute(unitTest, "id");
            var storage = Attribute(unitTest, "storage");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(storage))
            {
                continue;
            }

            // TRX stores Windows-style paths even when the report was produced on Linux,
            // so normalise separators before deriving the assembly name.
            var name = Path.GetFileNameWithoutExtension(storage.Replace('\\', '/'));
            if (!string.IsNullOrWhiteSpace(name))
            {
                map[id] = name;
            }
        }

        return map;
    }

    private static IEnumerable<XElement> Descendants(XDocument document, string localName) =>
        document.Descendants().Where(e => string.Equals(e.Name.LocalName, localName, StringComparison.Ordinal));

    private static string Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, name, StringComparison.Ordinal))?.Value ?? string.Empty;

    private static TimeSpan ParseDuration(string value) =>
        TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed) ? parsed : TimeSpan.Zero;

    private sealed class MutableAssembly(string assembly)
    {
        public string Assembly { get; } = assembly;

        public int Total { get; private set; }

        public int Passed { get; private set; }

        public int Failed { get; private set; }

        public int Skipped { get; private set; }

        public TimeSpan Duration { get; private set; }

        public void Add(string outcome, TimeSpan duration)
        {
            Total++;
            Duration += duration;

            switch (outcome.ToLowerInvariant())
            {
                case "passed":
                case "passedbutrunaborted":
                case "warning":
                    Passed++;
                    break;
                case "notexecuted":
                case "inconclusive":
                case "notrunnable":
                case "pending":
                case "disconnected":
                    Skipped++;
                    break;
                default:
                    Failed++;
                    break;
            }
        }
    }
}