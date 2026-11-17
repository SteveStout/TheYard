using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TheYard.Api.Features.Docs;

/// <summary>
/// The changelog as the site serves it carries each measured version's quality reading at the end of its line:
/// how many of its quality numbers held against the version before, how many got worse and how many better, read
/// from data/quality-history.json when the page is opened (ADR-130). "Held" means what it means on the landing and
/// the gate badge (src/lib/qualityHeld.ts): no worse than the version before, where a number named better-higher
/// may not fall. A version the history did not measure keeps its line as written. The file on GitHub stays as
/// written.
/// </summary>
public static partial class ChangelogQuality
{
    /// <summary>The changelog the catalogue serves.</summary>
    public const string File = "docs/CHANGELOG.md";

    /// <summary><paramref name="markdown"/> with each measured version's reading added, when it is the changelog.</summary>
    public static string Append(string file, string markdown, string repoRoot)
    {
        if (!string.Equals(file, File, StringComparison.Ordinal))
        {
            return markdown;
        }
        var readings = Readings(repoRoot);
        return VersionLine().Replace(markdown, line =>
            readings.TryGetValue(line.Groups["version"].Value, out string? reading) ? $"{line.Value.TrimEnd()} *Quality: {reading}*." : line.Value);
    }

    /// <summary>Each measured version's reading, in words, against the measured version before it.</summary>
    private static Dictionary<string, string> Readings(string repoRoot)
    {
        using var document = JsonDocument.Parse(System.IO.File.ReadAllText(Path.Combine(repoRoot, "data", "quality-history.json")));
        var rows = document.RootElement.GetProperty("rows").EnumerateArray()
            .Where(r => r.GetProperty("source").GetString() == "quality.py")
            .Select(r => (Version: r.GetProperty("version").GetString() ?? "", Numbers: Numbers(r.GetProperty("numbers"))))
            .ToList();
        var readings = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < rows.Count; i++)
        {
            var now = rows[i].Numbers;
            var before = i > 0 ? rows[i - 1].Numbers : null;
            int of = now.Count;
            int worse = before is null ? 0 : now.Count(n => before.TryGetValue(n.Key, out double b) && Worse(n.Key, b, n.Value));
            int better = before is null ? 0 : now.Count(n => before.TryGetValue(n.Key, out double b) && Worse(n.Key, n.Value, b));
            string reading = $"{(of - worse).ToString(CultureInfo.InvariantCulture)} of {of.ToString(CultureInfo.InvariantCulture)} held";
            if (worse > 0)
            {
                reading += $", {worse.ToString(CultureInfo.InvariantCulture)} worse";
            }
            if (better > 0)
            {
                reading += $", {better.ToString(CultureInfo.InvariantCulture)} better";
            }
            readings[rows[i].Version] = reading;
        }
        return readings;
    }

    /// <summary>The numbers a reading measured; a number the scanner did not read is left out, never zero.</summary>
    private static Dictionary<string, double> Numbers(JsonElement numbers) =>
        numbers.EnumerateObject().Where(n => n.Value.ValueKind == JsonValueKind.Number)
            .ToDictionary(n => n.Name, n => n.Value.GetDouble(), StringComparer.Ordinal);

    /// <summary>Whether a number moved the wrong way from <paramref name="before"/> to <paramref name="after"/>.</summary>
    private static bool Worse(string key, double before, double after) =>
        key.EndsWith("_higher_is_better", StringComparison.Ordinal) ? after < before : after > before;

    [GeneratedRegex(@"^- \*\*(?<version>\d+\.\d+\.\d+\.\d+)\*\* \(\d{4}-\d\d-\d\d\): .*$", RegexOptions.Multiline)]
    private static partial Regex VersionLine();
}
