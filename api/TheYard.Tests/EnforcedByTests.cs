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
}
