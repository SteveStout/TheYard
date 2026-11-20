using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TheYard.Api.Features.Docs;

/// <summary>
/// The Quality page's six rulebook cards (ADR-130), the part of <see cref="QualityCounts"/> that reads
/// data/rule-groups.json: each group's promise and example, and its rules counted from ADR-075's table and the gate's
/// results, as the lines of the fence the page draws them from. Its own file so the class stays under the line cap.
/// </summary>
public static partial class QualityCounts
{
    // #region rule-groups
    /// <summary>
    /// The rulebook's six cards for a reader who is not an engineer (ADR-130, and its addendum on the page as its
    /// picture): each group of data/rule-groups.json as one line of a quality-groups fence, its title, its promise,
    /// its example, the rules the gate checks, the group's rules, and the test runs passed of run in the last gate,
    /// counted from ADR-075's table and the gate's results, never typed. A rule held only by the job hunt Function's
    /// suite, which its own deploy runs and this results file does not record test by test, is not counted as checked
    /// by the gate; the page says so once, under the cards.
    /// </summary>
    private static string RuleGroups(string repoRoot)
    {
        var results = Results(repoRoot);
        var elsewhere = FunctionClasses(repoRoot);
        var rows = RuleRows(repoRoot).ToDictionary(cells => cells[0], StringComparer.Ordinal);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "data", "rule-groups.json")));
        var lines = new StringBuilder();
        foreach (JsonElement group in document.RootElement.GetProperty("groups").EnumerateArray())
        {
            string[] rules = [.. group.GetProperty("rules").EnumerateArray().Select(r => r.GetString() ?? "").Where(rows.ContainsKey)];
            var counted = rules.Select(rule => Counted(rows[rule], results, elsewhere)).ToList();
            int gated = counted.Count(classes => classes.Length > 0);
            // A class two rules both name ran once, so the group counts it once.
            var (run, passed) = Ran(counted.SelectMany(classes => classes), results);
            lines.Append(CultureInfo.InvariantCulture, $"{group.GetProperty("title").GetString()} | {group.GetProperty("promise").GetString()} | {group.GetProperty("example").GetString()} | {gated} | {rules.Length} | {passed} | {run}\n");
        }
        return lines.ToString().TrimEnd();
    }
    // #endregion rule-groups
}
