using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The table of rules in ADR: The rules a change has to pass, held to the tests
/// it names.
///
/// <para>The table exists so that a reader changing something can find the rule
/// that governs it without reading seventy-five records, and so that the answer
/// to "which test holds this" is written down rather than discovered by
/// breaking it. A map like that is worth exactly as much as its accuracy, and
/// an out-of-date map is worse than none: it sends a reader to a test that no
/// longer exists and teaches them the documentation is decorative.</para>
///
/// <para>So the map is checked. Both directions of every row: the test it names
/// has to be a class in this assembly with tests in it, and the record it cites
/// has to be a record that exists by that exact title.</para>
/// </summary>
public class RuleTableTests
{
    private const string Record = "ADR-075-the-rules-a-change-has-to-pass.md";

    // #region table
    /// <summary>
    /// The rows of the one table in that record, as cells. The header row and
    /// the dashes under it are dropped; everything else between the header and
    /// the first line that is not a table row is a rule.
    /// </summary>
    private static List<string[]> Rows()
    {
        string[] lines = File.ReadAllLines(Path.Combine(Repo.Root(), "docs", Record));
        int header = Array.FindIndex(lines, line => line.StartsWith("| The rule |", StringComparison.Ordinal));

        Assert.True(header >= 0, $"{Record} should carry the rules table");

        var rows = new List<string[]>();
        for (int i = header + 2; i < lines.Length && lines[i].StartsWith('|'); i++)
        {
            rows.Add(lines[i]
                .Trim('|')
                .Split('|')
                .Select(cell => cell.Trim())
                .ToArray());
        }

        // A table that empties itself passes every row check there is, so the
        // count is asserted too. Seventeen rows when this was written.
        Assert.True(rows.Count >= 15, $"the table holds {rows.Count} rules, which is fewer than it had");
        return rows;
    }

    /// <summary>Every name in a cell that looks like one of this project's test
    /// classes.</summary>
    private static IEnumerable<string> TestsNamedIn(string cell) =>
        Regex.Matches(cell, @"\b[A-Z][A-Za-z0-9]*Tests\b").Select(match => match.Value).Distinct();

    /// <summary>
    /// A cell may cite more than one record, and a record's title may itself
    /// contain a comma ("Style, enforced"), so a citation runs from its own
    /// marker to the next one rather than to the next comma.
    /// </summary>
    private static IEnumerable<string> RecordsCitedIn(string cell) =>
        Regex.Split(cell, @"(?=\bADR: )")
            .Where(part => part.StartsWith("ADR: ", StringComparison.Ordinal))
            .Select(part => part["ADR: ".Length..].Trim().TrimEnd(',', '.', ' '))
            .Where(title => title.Length > 0);

    [Fact]
    public void Every_rule_in_the_table_names_a_test_that_exists()
    {
        var classes = typeof(RuleTableTests).Assembly
            .GetTypes()
            .Where(type => type.Name.EndsWith("Tests", StringComparison.Ordinal))
            .Where(type => type.GetMethods().Any(method =>
                method.GetCustomAttributes(typeof(FactAttribute), inherit: false).Length > 0
                || method.GetCustomAttributes(typeof(TheoryAttribute), inherit: false).Length > 0))
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        var wrong = new List<string>();

        foreach (string[] cells in Rows())
        {
            string[] named = TestsNamedIn(cells[^1]).ToArray();
            if (named.Length == 0)
            {
                wrong.Add($"'{cells[0]}' names no test");
                continue;
            }

            foreach (string name in named.Where(name => !classes.Contains(name)))
            {
                wrong.Add($"'{cells[0]}' names {name}, and this assembly has no test class by that name");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void Every_record_the_table_cites_is_a_record_that_exists()
    {
        var titles = Directory
            .EnumerateFiles(Path.Combine(Repo.Root(), "docs"), "ADR-*.md")
            .Select(path => File.ReadLines(path).First().Trim())
            .Where(heading => heading.StartsWith("# ADR: ", StringComparison.Ordinal))
            .Select(heading => heading["# ADR: ".Length..].Trim())
            .ToHashSet(StringComparer.Ordinal);

        var wrong = new List<string>();

        foreach (string[] cells in Rows())
        {
            foreach (string cited in RecordsCitedIn(cells[1]).Where(cited => !titles.Contains(cited)))
            {
                wrong.Add($"'{cells[0]}' cites a record called '{cited}' and there is no record by that title");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
    // #endregion table
}
