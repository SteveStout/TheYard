using System.Text.RegularExpressions;
using TheYard.Api.Features.Docs;

namespace TheYard.Tests;

/// <summary>
/// Every decision record, as the site serves it, ends with what enforces it (MASTER-PLAN-2026-11-17 R13i): the rules
/// of ADR-075's table that cite it with their tests and last-gate results, or, when no rule cites it, that review
/// holds it. Built from the table, so the section and the table cannot disagree.
/// </summary>
public sealed class EnforcedByTests
{
    private static string[] Records() =>
        Directory.GetFiles(Path.Combine(Repo.Root(), "docs", "decisions"), "ADR-*.md").Order(StringComparer.Ordinal).ToArray();

    private static string Served(string path) =>
        EnforcedBy.Append(Path.GetRelativePath(Repo.Root(), path).Replace('\\', '/'), File.ReadAllText(path), Repo.Root());

    [Fact]
    public void Every_record_ends_with_one_enforced_by_section_and_no_record_writes_its_own()
    {
        Assert.All(Records(), path =>
        {
            Assert.DoesNotContain(EnforcedBy.Heading, File.ReadAllText(path), StringComparison.Ordinal);
            string served = Served(path);
            Assert.Single(Regex.Matches(served, $"^{Regex.Escape(EnforcedBy.Heading)}$", RegexOptions.Multiline));
            Assert.True(served.TrimEnd().EndsWith("Held by review.", StringComparison.Ordinal) || served.Contains("| In the last gate |", StringComparison.Ordinal), path);
        });
    }

    [Fact]
    public void A_record_the_rulebook_cites_lists_each_rule_with_its_tests_and_one_no_rule_cites_is_held_by_review()
    {
        string table = File.ReadAllText(Directory.GetFiles(Path.Combine(Repo.Root(), "docs", "decisions"), "ADR-075-*.md").Single());
        string firstRow = table.Split('\n').SkipWhile(l => !l.StartsWith("| The rule |", StringComparison.Ordinal)).Skip(2).First();
        string[] cells = [.. firstRow.Trim().Trim('|').Split(" | ").Select(c => c.Trim())];
        string cited = cells[1]["ADR: ".Length..].Split(", ADR: ")[0].Trim();

        string record = Records().Single(path => File.ReadLines(path).First() == $"# ADR: {cited}");
        string served = Served(record);
        Assert.Contains($"| {cells[0]} | {cells[2]} |", served, StringComparison.Ordinal);

        string? quiet = Records().FirstOrDefault(path => Served(path).TrimEnd().EndsWith("Not machine-checked. Held by review.", StringComparison.Ordinal));
        Assert.NotNull(quiet);
    }

    [Fact]
    public void A_document_that_is_not_a_record_is_served_as_written()
    {
        const string page = "# Quality you can watch\n\nWords.";
        Assert.Equal(page, EnforcedBy.Append("docs/quality/QUALITY.md", page, Repo.Root()));
    }

    [Fact]
    public void A_rule_that_cites_its_record_by_number_or_as_this_record_is_listed_under_it()
    {
        // CodeRabbit on 1.0.5.85: a citation names a record by "ADR: " and its title, by "ADR-" and its number, or,
        // in ADR-075's own table, as "This record".
        string table = File.ReadAllText(Records().Single(path => Path.GetFileName(path).StartsWith("ADR-075-", StringComparison.Ordinal)));
        var rows = table.Split('\n').SkipWhile(l => !l.StartsWith("| The rule |", StringComparison.Ordinal)).Skip(2)
            .TakeWhile(l => l.StartsWith('|')).Select(l => l.Trim().Trim('|').Split(" | ").Select(c => c.Trim()).ToArray()).ToList();

        string[] self = rows.First(cells => cells[1].StartsWith("This record", StringComparison.Ordinal));
        Assert.Contains($"| {self[0]} |", Served(Records().Single(path => Path.GetFileName(path).StartsWith("ADR-075-", StringComparison.Ordinal))), StringComparison.Ordinal);

        string[] numbered = rows.First(cells => System.Text.RegularExpressions.Regex.IsMatch(cells[1], @"^ADR-\d+\b"));
        string number = System.Text.RegularExpressions.Regex.Match(numbered[1], @"^ADR-(\d+)").Groups[1].Value;
        string record = Records().Single(path => Path.GetFileName(path).StartsWith($"ADR-{number}-", StringComparison.Ordinal));
        Assert.Contains($"| {numbered[0]} |", Served(record), StringComparison.Ordinal);
    }

    [Fact]
    public void The_index_marks_count_each_records_rules_the_way_its_own_section_lists_them()
    {
        // ADR-029, the addendum on the marks: the sidebar marks a record held by a test exactly when its section
        // lists a rule, so the mark and the record cannot disagree.
        var marks = EnforcedBy.Marks(Repo.Root());
        Assert.Equal(Records().Length, marks.Count);
        Assert.All(Records(), path =>
        {
            string number = Regex.Match(Path.GetFileName(path), @"^ADR-(\d+)-").Groups[1].Value;
            string section = Served(path)[Served(path).IndexOf(EnforcedBy.Heading, StringComparison.Ordinal)..];
            int listed = section.Split('\n').Count(line => line.StartsWith("| ", StringComparison.Ordinal)
                && !line.StartsWith("| The rule |", StringComparison.Ordinal) && !line.StartsWith("| ---", StringComparison.Ordinal));
            Assert.True(marks[number] == listed, $"{number}: the mark counts {marks[number]}, the section lists {listed}");
        });
        Assert.Contains(marks.Values, rules => rules > 0);
        Assert.Contains(marks.Values, rules => rules == 0);
    }
}
