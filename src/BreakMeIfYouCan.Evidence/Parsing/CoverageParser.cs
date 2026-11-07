using System.Globalization;
using System.Xml.Linq;

namespace BreakMeIfYouCan.Evidence.Parsing;

public sealed record CoverageSummary(
    double? LinePercent,
    double? BranchPercent,
    int LinesCovered,
    int LinesValid,
    int BranchesCovered,
    int BranchesValid,
    IReadOnlyList<string> Reports);

/// <summary>
/// Parses Cobertura coverage reports and merges them. Coverlet's collector emits one
/// report per test project, so the totals must be summed deliberately rather than
/// averaged; averaging per-project percentages would silently inflate the number.
/// </summary>
public static class CoverageParser
{
    public static CoverageSummary Parse(IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);

        int linesCovered = 0, linesValid = 0, branchesCovered = 0, branchesValid = 0;
        var reports = new List<string>();

        foreach (var file in files.Where(File.Exists).OrderBy(f => f, StringComparer.Ordinal))
        {
            XDocument document;
            try
            {
                document = XDocument.Load(file, LoadOptions.None);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                continue;
            }

            var root = document.Root;
            if (root is null || !string.Equals(root.Name.LocalName, "coverage", StringComparison.Ordinal))
            {
                continue;
            }

            reports.Add(Path.GetFileName(file));

            var fileLinesValid = ReadInt(root, "lines-valid");
            var fileLinesCovered = ReadInt(root, "lines-covered");

            if (fileLinesValid is null || fileLinesCovered is null)
            {
                var computed = CountLines(root);
                fileLinesValid = computed.Valid;
                fileLinesCovered = computed.Covered;
            }

            linesValid += fileLinesValid.Value;
            linesCovered += fileLinesCovered.Value;

            var fileBranchesValid = ReadInt(root, "branches-valid");
            var fileBranchesCovered = ReadInt(root, "branches-covered");
            if (fileBranchesValid is null || fileBranchesCovered is null)
            {
                var computed = CountBranches(root);
                fileBranchesValid = computed.Valid;
                fileBranchesCovered = computed.Covered;
            }

            branchesValid += fileBranchesValid.Value;
            branchesCovered += fileBranchesCovered.Value;
        }

        return new CoverageSummary(
            linesValid > 0 ? Round(linesCovered * 100.0 / linesValid) : null,
            branchesValid > 0 ? Round(branchesCovered * 100.0 / branchesValid) : null,
            linesCovered,
            linesValid,
            branchesCovered,
            branchesValid,
            reports);
    }

    private static (int Covered, int Valid) CountLines(XElement root)
    {
        // Deduplicate by (file, line) because Cobertura repeats lines across classes.
        var seen = new Dictionary<string, Dictionary<int, bool>>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in root.Descendants().Where(e => string.Equals(e.Name.LocalName, "line", StringComparison.Ordinal)))
        {
            var number = ReadInt(line, "number");
            if (number is null)
            {
                continue;
            }

            var file = line.Ancestors().FirstOrDefault(a => string.Equals(a.Name.LocalName, "class", StringComparison.Ordinal));
            var fileName = file is null ? string.Empty : Attribute(file, "filename");
            var hits = ReadInt(line, "hits") ?? 0;

            if (!seen.TryGetValue(fileName, out var lines))
            {
                lines = [];
                seen[fileName] = lines;
            }

            lines[number.Value] = lines.TryGetValue(number.Value, out var hit) ? hit || hits > 0 : hits > 0;
        }

        var valid = seen.Values.Sum(l => l.Count);
        var covered = seen.Values.Sum(l => l.Count(pair => pair.Value));
        return (covered, valid);
    }

    private static (int Covered, int Valid) CountBranches(XElement root)
    {
        var conditions = root.Descendants()
            .Where(e => string.Equals(e.Name.LocalName, "condition", StringComparison.Ordinal))
            .ToList();

        if (conditions.Count == 0)
        {
            return (0, 0);
        }

        var covered = conditions.Count(c =>
        {
            var raw = Attribute(c, "coverage");
            return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0;
        });

        return (covered, conditions.Count);
    }

    private static int? ReadInt(XElement element, string name)
    {
        var raw = Attribute(element, name);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static string Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(a => string.Equals(a.Name.LocalName, name, StringComparison.Ordinal))?.Value ?? string.Empty;

    private static double Round(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}