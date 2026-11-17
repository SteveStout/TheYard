using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TheYard.Api.Features.Docs;

/// <summary>
/// The Quality page's live numbers (ADR-130, Quality you can watch). Every number the page shows is read here, at
/// request time, from a file the build carries: the quality history (data/quality-history.json), the rules table in
/// ADR-075, the gate's results (data/test-results.json), CodeRabbit's ladder (ADR-129) and its findings log
/// (data/coderabbit-findings.json). None is typed into the page, so the page cannot say a number the data does not.
/// A measure it does not know, or a file this build lacks, renders as a note in words, as every live number does.
/// </summary>
public static partial class QualityCounts
{
    /// <summary>The measures this class answers, by the first word of the placeholder.</summary>
    public static readonly string[] Measures = ["quality-trend", "quality-now", "quality-limit", "quality-first", "quality-peak", "quality-thin", "quality-versions", "coverage-trend", "coverage-now", "quality-rules", "rule-count", "rule-groups", "coderabbit-level", "coderabbit-rows"];

    // #region quality-measures
    /// <summary>One measure, as text; <paramref name="argument"/> is the word after the measure's name, if any.</summary>
    public static string Measure(string kind, string? argument, string repoRoot)
    {
        try
        {
            return kind switch
            {
                "quality-trend" when argument is not null => Trend(repoRoot, argument),
                "quality-now" when argument is not null => Format(Measured(repoRoot).Last().GetProperty("numbers").GetProperty(argument)),
                "quality-limit" when argument is not null => Format(Measured(repoRoot).Last().GetProperty("limits").GetProperty(argument)),
                "quality-first" when argument is not null => Format(Rows(repoRoot).First(r => Has(r.GetProperty("numbers"), argument)).GetProperty("numbers").GetProperty(argument)),
                "quality-peak" when argument is not null => Peak(repoRoot, argument),
                "quality-thin" when argument is not null => Format(Measured(repoRoot).Last().GetProperty("thin").GetProperty(argument)),
                "quality-versions" => Measured(repoRoot).Count().ToString(CultureInfo.InvariantCulture),
                "coverage-trend" when argument is "lines" or "branches" => CoverageTrend(repoRoot, argument),
                "coverage-now" when argument is not null => Format(Measured(repoRoot).Last(r => r.GetProperty("coverage").ValueKind == JsonValueKind.Object).GetProperty("coverage").GetProperty(argument)),
                "quality-rules" => Rules(repoRoot),
                "rule-count" => RuleRows(repoRoot).Count.ToString(CultureInfo.InvariantCulture),
                "rule-groups" => RuleGroups(repoRoot),
                "coderabbit-level" => Ladder(repoRoot),
                "coderabbit-rows" => Findings(repoRoot),
                _ => $"(no live measure named '{kind}{(argument is null ? "" : " " + argument)}')",
            };
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or JsonException)
        {
            return $"(the data for '{kind} {argument}' is not in this build)";
        }
    }
    // #endregion quality-measures

    /// <summary>Every row of the history, pre-scan rows first, in the order the file keeps them.</summary>
    private static IEnumerable<JsonElement> Rows(string repoRoot)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "data", "quality-history.json")));
        return document.RootElement.GetProperty("rows").EnumerateArray().Select(r => r.Clone()).ToList();
    }

    /// <summary>The rows the scanner measured, one per shipped version.</summary>
    private static IEnumerable<JsonElement> Measured(string repoRoot) =>
        Rows(repoRoot).Where(r => r.GetProperty("source").GetString() == "quality.py");

    private static bool Has(JsonElement numbers, string key) =>
        numbers.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.Number;

    private static string Format(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetInt32(out int whole) => whole.ToString("#,0", CultureInfo.InvariantCulture),
        JsonValueKind.Number => value.GetDouble().ToString("0.##", CultureInfo.InvariantCulture),
        _ => "not measured",
    };

    /// <summary>One line a row for a trend fence: the date, the version (blank for a pre-scan row), the value and its source.</summary>
    private static string Trend(string repoRoot, string key) =>
        string.Join("\n", Rows(repoRoot)
            .Where(r => Has(r.GetProperty("numbers"), key))
            .Select(r => $"{r.GetProperty("date").GetString()} | {Version(r)} | {Format(r.GetProperty("numbers").GetProperty(key)).Replace(",", "", StringComparison.Ordinal)} | {r.GetProperty("source").GetString()}"));

    /// <summary>One line a version CI on main measured, in a trend fence's shape: the date, the version, the share and its source.</summary>
    private static string CoverageTrend(string repoRoot, string which) =>
        string.Join("\n", Measured(repoRoot)
            .Where(r => r.GetProperty("coverage").ValueKind == JsonValueKind.Object)
            .Select(r => $"{r.GetProperty("date").GetString()} | {Version(r)} | {Format(r.GetProperty("coverage").GetProperty(which))} | ci"));

    /// <summary>The worst reading of a number, the highest of one that may only fall and the lowest of one that may only rise, and the version it was read at.</summary>
    private static string Peak(string repoRoot, string key)
    {
        var readings = Rows(repoRoot).Where(r => Has(r.GetProperty("numbers"), key)).ToList();
        Func<JsonElement, double> value = r => r.GetProperty("numbers").GetProperty(key).GetDouble();
        JsonElement worst = key.EndsWith("higher_is_better", StringComparison.Ordinal) ? readings.MinBy(value) : readings.MaxBy(value);
        string where = Version(worst) is { Length: > 0 } version ? $"at {version}" : $"on {worst.GetProperty("date").GetString()}";
        return $"{Format(worst.GetProperty("numbers").GetProperty(key))} {where}";
    }

    private static string Version(JsonElement row) =>
        row.TryGetProperty("version", out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    // #region rules-table
    /// <summary>
    /// ADR-075's table, row for row, with each row's tests and how they did in the gate this build shipped from:
    /// every test of the classes the row names, passed of run. The results file records the site's suites test by
    /// test; a row held only by the Function's or the machine lanes' suite, which the gate runs and records by its
    /// exit code alone, says so. Rendered here so the page's table is the record's table and cannot drift from it.
    /// </summary>
    private static string Rules(string repoRoot)
    {
        var results = Results(repoRoot);
        var table = new StringBuilder("| The rule | Where it was decided | What holds it | In the last gate |\n| --- | --- | --- | --- |\n");
        foreach (string[] cells in RuleRows(repoRoot))
        {
            var (run, passed) = Ran(cells, results);
            string verdict = run == 0 ? "in its own suite, not this file" : $"{passed} of {run} passed";
            table.Append(CultureInfo.InvariantCulture, $"| {cells[0]} | {cells[1]} | {cells[2]} | {verdict} |\n");
        }
        return table.ToString().TrimEnd();
    }

    /// <summary>ADR-075's table, one row's three cells at a time, as the record keeps them.</summary>
    private static List<string[]> RuleRows(string repoRoot)
    {
        string record = File.ReadAllText(Directory.GetFiles(Path.Combine(repoRoot, "docs", "decisions"), "ADR-075-*.md").Single());
        int start = record.IndexOf("| The rule | Where it was decided | What holds it |", StringComparison.Ordinal);
        return [.. record[start..].Split('\n').Skip(2).TakeWhile(l => l.StartsWith('|'))
            .Select(l => l.Trim().Trim('|').Split(" | ").Select(c => c.Trim()).ToArray())
            .Where(cells => cells.Length >= 3)];
    }

    /// <summary>A row's tests in the gate this build shipped from: run and passed, across the classes it names.</summary>
    private static (int Run, int Passed) Ran(string[] cells, Dictionary<string, (int Run, int Passed)> results)
    {
        string[] classes = [.. TestClass().Matches(cells[2]).Select(m => m.Value).Distinct()];
        return (classes.Sum(c => results.TryGetValue(c, out var r) ? r.Run : 0), classes.Sum(c => results.TryGetValue(c, out var r) ? r.Passed : 0));
    }

    /// <summary>
    /// The rules of ADR-075's table that cite the record titled <paramref name="title"/>, each with the tests that
    /// hold it and how they did in the gate this build shipped from, for the record's "Enforced by" section
    /// (EnforcedBy.cs). A cell may cite several records, and a title may hold a comma, so a citation runs from its
    /// own "ADR: " to the next, as RuleTableTests reads it.
    /// </summary>
    internal static List<(string Rule, string Holds, string Verdict)> RulesCiting(string title, string repoRoot)
    {
        var results = Results(repoRoot);
        return [.. RuleRows(repoRoot)
            .Where(cells => Regex.Split(cells[1], @"(?=\bADR: )")
                .Where(part => part.StartsWith("ADR: ", StringComparison.Ordinal))
                .Any(part => string.Equals(part["ADR: ".Length..].Trim().TrimEnd(',', '.', ' '), title, StringComparison.Ordinal)))
            .Select(cells =>
            {
                var (run, passed) = Ran(cells, results);
                return (cells[0], cells[2], run == 0 ? "in its own suite, not this file" : $"{passed} of {run} passed");
            })];
    }
    // #endregion rules-table

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
        var rows = RuleRows(repoRoot).ToDictionary(cells => cells[0], StringComparer.Ordinal);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "data", "rule-groups.json")));
        var cards = new StringBuilder();
        foreach (JsonElement group in document.RootElement.GetProperty("groups").EnumerateArray())
        {
            string[] rules = [.. group.GetProperty("rules").EnumerateArray().Select(r => r.GetString() ?? "").Where(rows.ContainsKey)];
            var ran = rules.Select(rule => Ran(rows[rule], results)).ToList();
            int gated = ran.Count(r => r.Run > 0);
            int run = ran.Sum(r => r.Run);
            int passed = ran.Sum(r => r.Passed);
            string status = passed == run ? $"all {run} of their test runs in the last gate passed" : $"{passed} of their {run} test runs in the last gate passed";
            string apart = (rules.Length - gated) switch
            {
                0 => "",
                1 => " One more is held by the job hunt Function's own suite, which its deploy runs.",
                int more => $" {more} more are held by the job hunt Function's own suite, which its deploy runs.",
            };
            cards.Append(CultureInfo.InvariantCulture, $"### {group.GetProperty("title").GetString()}\n\n{group.GetProperty("promise").GetString()}\n\n{group.GetProperty("example").GetString()}\n\n**{RuleWords(rules.Length)}: {(gated == rules.Length ? "" : $"{gated} checked by the gate, ")}{status}.**{apart}\n\n");
        }
        return cards.ToString().TrimEnd();
    }

    /// <summary>A count of rules in words a card can say: one rule, or <paramref name="count"/> rules.</summary>
    private static string RuleWords(int count) => count == 1 ? "1 rule" : $"{count.ToString(CultureInfo.InvariantCulture)} rules";
    // #endregion rule-groups

    /// <summary>Each test class's results in the gate this build shipped from, across every suite, by class name.</summary>
    private static Dictionary<string, (int Run, int Passed)> Results(string repoRoot)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "data", "test-results.json")));
        var counts = new Dictionary<string, (int Run, int Passed)>(StringComparer.Ordinal);
        foreach (JsonElement suite in document.RootElement.GetProperty("suites").EnumerateArray())
        {
            foreach (JsonElement test in suite.GetProperty("tests").EnumerateArray())
            {
                string name = test[0].GetString() ?? "";
                var (run, passed) = counts.GetValueOrDefault(name);
                counts[name] = (run + 1, passed + (test[2].GetString() == "p" ? 1 : 0));
            }
        }
        return counts;
    }

    /// <summary>CodeRabbit's current level, read from the line ADR-129 keeps it on.</summary>
    private static string Ladder(string repoRoot)
    {
        string record = File.ReadAllText(Directory.GetFiles(Path.Combine(repoRoot, "docs", "decisions"), "ADR-129-*.md").Single());
        Match level = CurrentLevel().Match(record);
        return level.Success ? level.Groups[1].Value : "not named";
    }

    /// <summary>The findings log as table rows: version, request, how it was reviewed, the findings by severity in one cell, minutes.</summary>
    private static string Findings(string repoRoot)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "data", "coderabbit-findings.json")));
        var table = new StringBuilder("| Version | Pull request | Reviewed | Findings | Fixed | Wrong | Minutes |\n| --- | --- | --- | --- | ---: | ---: | ---: |\n");
        foreach (JsonElement row in document.RootElement.GetProperty("rows").EnumerateArray())
        {
            JsonElement found = row.GetProperty("findings");
            string pr = row.GetProperty("pr").ValueKind == JsonValueKind.Number ? $"[#{row.GetProperty("pr").GetInt32()}](https://github.com/SteveStout/TheYard/pull/{row.GetProperty("pr").GetInt32()})" : "none";
            string minutes = row.GetProperty("review_seconds").ValueKind == JsonValueKind.Number ? (row.GetProperty("review_seconds").GetInt32() / 60.0).ToString("0.0", CultureInfo.InvariantCulture) : "";
            string[] named = [.. found.EnumerateObject().Where(f => f.Value.GetInt32() > 0).Select(f => $"{f.Value.GetInt32()} {f.Name}")];
            string findings = named.Length == 0 ? "none" : string.Join(", ", named);
            table.Append(CultureInfo.InvariantCulture, $"| {row.GetProperty("version").GetString()} | {pr} | {row.GetProperty("reviewed").GetString()} | {findings} | {Counted(row, "fixed")} | {Counted(row, "answered_as_wrong")} | {minutes} |\n");
        }
        return table.ToString().TrimEnd();
    }

    /// <summary>A disposition count as the log holds it, and an empty cell where it has not been read yet.</summary>
    private static string Counted(JsonElement row, string key) =>
        row.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32().ToString(CultureInfo.InvariantCulture) : "";

    [GeneratedRegex(@"\b[A-Z][A-Za-z]*Tests\b")]
    private static partial Regex TestClass();

    [GeneratedRegex(@"^Current level: (\d+)", RegexOptions.Multiline)]
    private static partial Regex CurrentLevel();
}
