using TheYard.Api.Features.Docs;

namespace TheYard.Tests;

/// <summary>
/// The Quality page's six cards count each group's tests from the gate's results (QualityCounts, rule-groups): a
/// class two rules both name ran once and counts once; a rule about the job hunt Function is not counted from the
/// site's runs of a class that shares its name; and a rule held by a suite the file does not record is not counted as
/// checked by the gate. Held on a small repository written for the test, not the real one.
/// </summary>
public sealed class RuleGroupCountsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"rule-groups-{Guid.NewGuid():N}");

    public RuleGroupCountsTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "docs", "decisions"));
        Directory.CreateDirectory(Path.Combine(root, "data"));
        Directory.CreateDirectory(Path.Combine(root, "functions", "jobhunt.Tests"));
        File.WriteAllText(Path.Combine(root, "functions", "jobhunt.Tests", "SliceTests.cs"), "class SliceTests {}");
        File.WriteAllText(Path.Combine(root, "docs", "decisions", "ADR-075-the-rules.md"), string.Join('\n',
            "# ADR: The rules a change has to pass",
            "",
            "| The rule | Where it was decided | What holds it |",
            "| --- | --- | --- |",
            "| Styles come from the tokens | ADR: Style | StyleRulesTests |",
            "| Corners come from the tokens | ADR: Style | StyleRulesTests |",
            "| The job hunt Function's slices stay apart | ADR: Slices | SliceTests |",
            "| A lane's run is read back | ADR: Runs | RunTests |",
            ""));
        File.WriteAllText(Path.Combine(root, "data", "test-results.json"), """
            {"suites": [{"id": "xunit", "tests": [["StyleRulesTests", "a", "p", 1], ["StyleRulesTests", "b", "p", 1], ["SliceTests", "c", "p", 1]]}]}
            """);
        File.WriteAllText(Path.Combine(root, "data", "rule-groups.json"), """
            {"groups": [
              {"key": "shape", "title": "The architecture keeps its shape", "promise": "P.", "example": "E.",
               "rules": ["Styles come from the tokens", "Corners come from the tokens", "The job hunt Function's slices stay apart", "A lane's run is read back"]}
            ]}
            """);
    }

    public void Dispose() => Directory.Delete(root, recursive: true);

    [Fact]
    public void A_shared_class_counts_once_a_Function_rule_is_not_counted_from_the_site_and_the_rest_are_named_plainly()
    {
        string card = QualityCounts.Measure("rule-groups", null, root);

        // Two rules name StyleRulesTests, whose two runs count once; the site's SliceTests run is not the Function's.
        // One fence line: the title, the promise, the example, 2 checked by the gate of 4 rules, 2 runs passed of 2.
        Assert.Equal("The architecture keeps its shape | P. | E. | 2 | 4 | 2 | 2", card);
    }

    [Fact]
    public void The_full_table_does_not_count_the_site_s_run_for_a_Function_rule()
    {
        string table = QualityCounts.Measure("quality-rules", null, root);

        Assert.Contains("| The job hunt Function's slices stay apart | ADR: Slices | SliceTests | in its own suite, not this file |", table, StringComparison.Ordinal);
        Assert.Contains("| Styles come from the tokens | ADR: Style | StyleRulesTests | 2 of 2 passed |", table, StringComparison.Ordinal);
    }
}
