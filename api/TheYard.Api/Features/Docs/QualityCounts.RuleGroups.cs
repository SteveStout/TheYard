using System.Globalization;
using System.Text;
using System.Text.Json;

namespace TheYard.Api.Features.Docs;

/// <summary>
/// The Quality page's six rulebook cards (ADR-130), the part of <see cref="QualityCounts"/> that reads
/// data/rule-groups.json: each group's promise and example, and its rules counted from ADR-075's table and the gate's
/// results. Its own file so the class stays under the line cap.
/// </summary>
public static partial class QualityCounts
{
    // #region rule-groups
    /// <summary>
    /// The rulebook's six cards for a reader who is not an engineer (ADR-130): each group of
    /// data/rule-groups.json as its title, its promise, its example, and a line counted from ADR-075's table and the
    /// gate's results, never typed. A rule held only by the job hunt Function's suite, which its own deploy runs and
    /// this results file does not record test by test, is counted apart rather than called passing.
    /// </summary>
    private static string RuleGroups(string repoRoot)
    {
        var results = Results(repoRoot);
        var elsewhere = FunctionClasses(repoRoot);
        var rows = RuleRows(repoRoot).ToDictionary(cells => cells[0], StringComparer.Ordinal);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "data", "rule-groups.json")));
        var cards = new StringBuilder();
        foreach (JsonElement group in document.RootElement.GetProperty("groups").EnumerateArray())
        {
            string[] rules = [.. group.GetProperty("rules").EnumerateArray().Select(r => r.GetString() ?? "").Where(rows.ContainsKey)];
            var counted = rules.Select(rule => Counted(rows[rule], results, elsewhere)).ToList();
            int gated = counted.Count(classes => classes.Length > 0);
            // A class two rules both name ran once, so the group counts it once.
            var (run, passed) = Ran(counted.SelectMany(classes => classes), results);
            string status = passed == run ? $"all {Number(run)} of their test runs in the last gate passed" : $"{Number(passed)} of their {Number(run)} test runs in the last gate passed";
            string apart = (rules.Length - gated) switch
            {
                0 => "",
                1 => " One more is held by a suite this file does not record test by test, the job hunt Function's or the machine lanes'.",
                int more => $" {more} more are held by suites this file does not record test by test, the job hunt Function's and the machine lanes'.",
            };
            cards.Append(CultureInfo.InvariantCulture, $"### {group.GetProperty("title").GetString()}\n\n{group.GetProperty("promise").GetString()}\n\n{group.GetProperty("example").GetString()}\n\n**{RuleWords(rules.Length)}: {(gated == rules.Length ? "" : $"{gated} checked by the gate, ")}{status}.**{apart}\n\n");
        }
        return cards.ToString().TrimEnd();
    }

    /// <summary>A count of rules in words a card can say: one rule, or <paramref name="count"/> rules.</summary>
    private static string RuleWords(int count) => count == 1 ? "1 rule" : $"{Number(count)} rules";

    /// <summary>A count as the site writes one: 1,516.</summary>
    private static string Number(int count) => count.ToString("#,0", CultureInfo.InvariantCulture);
    // #endregion rule-groups
}
