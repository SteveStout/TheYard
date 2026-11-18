using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TheYard.Api.Features.Docs;

/// <summary>
/// The "Enforced by" section at the end of every decision record as the site serves it (ADR-075): the rules of ADR-075's table that cite the record, each with what holds it and how its tests did in the
/// gate this build shipped from, or, for a record no rule cites, that it is held by review. Built from the table when
/// the record is served and never typed into a record, so a rule shows on its record the version it lands and a
/// record cannot claim a test that is not there. The files on GitHub stay as written.
/// </summary>
public static partial class EnforcedBy
{
    /// <summary>The section's heading, which a record's own text never uses.</summary>
    public const string Heading = "## Enforced by";

    /// <summary>
    /// <paramref name="markdown"/> with the section after it when <paramref name="file"/> is a decision record whose
    /// first line names its title (the first four records wrote "ADR-001:" before the house shape settled), and
    /// unchanged otherwise.
    /// </summary>
    public static string Append(string file, string markdown, string repoRoot)
    {
        Match title = Title().Match(markdown);
        if (!file.StartsWith("docs/decisions/ADR-", StringComparison.Ordinal) || !title.Success)
        {
            return markdown;
        }
        Match numbered = FileNumber().Match(file);
        int number = numbered.Success ? int.Parse(numbered.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        var rules = QualityCounts.RulesCiting(title.Groups[1].Value.Trim(), number, repoRoot);
        var section = new StringBuilder($"\n\n{Heading}\n\n");
        if (rules.Count == 0)
        {
            section.Append("Not machine-checked. Held by review.\n");
            return markdown.TrimEnd() + section;
        }
        section.Append(CultureInfo.InvariantCulture, $"{(rules.Count == 1 ? "One rule in [the rulebook](/?doc=adr-rules) cites" : $"{rules.Count} rules in [the rulebook](/?doc=adr-rules) cite")} this record, each held by a test on every release.\n\n");
        section.Append("| The rule | What holds it | In the last gate |\n| --- | --- | --- |\n");
        foreach (var (rule, holds, verdict) in rules)
        {
            section.Append(CultureInfo.InvariantCulture, $"| {rule} | {holds} | {verdict} |\n");
        }
        return markdown.TrimEnd() + section;
    }

    /// <summary>
    /// How many rules of ADR-075's table cite each decision record, keyed by the record's number as its file writes it
    /// ("029"), for the marks in the sidebar's Decision Records index (ADR-029): above zero a test holds the record,
    /// zero and review does. The same reading as each record's own section (QualityCounts.RulesCiting), so the mark and
    /// the section cannot disagree; counted once a process, because the records and the table are this build's files.
    /// </summary>
    public static IReadOnlyDictionary<string, int> Marks(string repoRoot) =>
        Counted.GetOrAdd(repoRoot, root => Directory.GetFiles(Path.Combine(root, "docs", "decisions"), "ADR-*.md")
            .Select(path => (Number: FileNumber().Match(path.Replace('\\', '/')), Title: Title().Match(File.ReadAllText(path))))
            .Where(record => record.Number.Success && record.Title.Success)
            .OrderBy(record => record.Number.Groups[1].Value, StringComparer.Ordinal)
            .ToDictionary(
                record => record.Number.Groups[1].Value,
                record => QualityCounts.RulesCiting(record.Title.Groups[1].Value.Trim(), int.Parse(record.Number.Groups[1].Value, CultureInfo.InvariantCulture), root).Count,
                StringComparer.Ordinal));

    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, int>> Counted = new(StringComparer.Ordinal);

    [GeneratedRegex(@"\A# ADR(?:-\d+)?: (.+)$", RegexOptions.Multiline)]
    private static partial Regex Title();

    [GeneratedRegex(@"docs/decisions/ADR-(\d+)-")]
    private static partial Regex FileNumber();
}
