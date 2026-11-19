using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Card;

/// <summary>One high-value role only Steve can unblock, as the card's first lines print it.</summary>
/// <param name="Company">The company.</param>
/// <param name="Role">The title.</param>
/// <param name="BandTop">The published band's top a year.</param>
/// <param name="Link">The posting.</param>
/// <param name="Control">The one thing he does.</param>
public sealed record UnblockRow(string Company, string Role, int BandTop, string Link, string Control);

/// <summary>
/// RULE 76 check 8, driven to him: the newest jobs file's NEEDS STEVE rows whose published band tops at $200,000 or
/// more and whose control is his alone (a RULE 50 swap, an account, a sign-in, a form only he can fill) lead the card,
/// five at most, and the mail's subject tells his phone how many. A row the tracker already shows applied, or one that
/// belongs to a live process, never prints. Nothing here asks; the line names the role and the one click.
/// </summary>
public static partial class TodayUnblock
{
    /// <summary>The band top that makes a role high value.</summary>
    public const int HighValue = 200_000;

    /// <summary>The most rows the section prints.</summary>
    public const int Most = 5;

    /// <summary>The section's heading.</summary>
    public const string Heading = "## Unblock these (high value)";

    /// <summary>The rows, highest band first.</summary>
    /// <param name="jobs">The newest JOBS-&lt;date&gt;.md text.</param>
    /// <param name="tracker">The tracker's rows.</param>
    public static IReadOnlyList<UnblockRow> Rows(string jobs, IReadOnlyList<string[]> tracker) =>
        [.. NeedsSteve(jobs)
            .Where(cells => cells.Length >= 7 && Only().IsMatch(cells[5]) && !Live().IsMatch(cells[5]))
            .Select(cells => new UnblockRow(cells[1], cells[2], BandTop(cells[5] + " " + cells[6]), cells[3], Control(cells[5])))
            .Where(row => row.BandTop >= HighValue && !Applied(row, tracker))
            .OrderByDescending(row => row.BandTop)
            .Take(Most)];

    /// <summary>The section, or empty when nothing waits.</summary>
    /// <param name="rows">The rows.</param>
    public static string Section(IReadOnlyList<UnblockRow> rows)
    {
        if (rows.Count == 0)
        {
            return string.Empty;
        }
        var text = new StringBuilder(Heading).Append("\n\n");
        foreach (UnblockRow row in rows)
        {
            text.Append(CultureInfo.InvariantCulture, $"- {row.Company}, {row.Role}, band top ${row.BandTop.ToString("N0", CultureInfo.InvariantCulture)}: {row.Control} {row.Link}\n");
        }
        return text.Append('\n').ToString();
    }

    /// <summary>The subject's tail that tells his phone the count, or empty.</summary>
    /// <param name="rows">The rows.</param>
    public static string SubjectTail(IReadOnlyList<UnblockRow> rows) =>
        rows.Count == 0 ? string.Empty : $", {rows.Count} high-value role{(rows.Count == 1 ? "" : "s")} need{(rows.Count == 1 ? "s" : "")} one click each";

    /// <summary>The highest band top a cell's text prints, in whole dollars, or 0.</summary>
    /// <param name="text">The cell.</param>
    public static int BandTop(string text)
    {
        int top = 0;
        foreach (Match band in Band().Matches(text))
        {
            string digits = band.Groups["top"].Value.Replace(",", string.Empty, StringComparison.Ordinal);
            if (int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
            {
                top = Math.Max(top, band.Groups["k"].Success ? value * 1000 : value);
            }
        }
        return top;
    }

    private static IEnumerable<string[]> NeedsSteve(string jobs)
    {
        bool inside = false;
        foreach (string line in jobs.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                inside = line.StartsWith("## NEEDS STEVE", StringComparison.Ordinal);
                continue;
            }
            if (inside && line.StartsWith('|') && !line.StartsWith("| #", StringComparison.Ordinal) && !line.StartsWith("|---", StringComparison.Ordinal))
            {
                yield return [.. line.Trim().Trim('|').Split('|').Select(cell => cell.Trim())];
            }
        }
    }

    private static string Control(string cell)
    {
        string first = cell.Split(". ")[0].Trim().TrimEnd('.');
        return Swap().IsMatch(cell) ? "RULE 50 swap: pick it over a READY row at the same company, or leave it." : first + ".";
    }

    private static bool Applied(UnblockRow row, IReadOnlyList<string[]> tracker) =>
        tracker.Any(r => r.Length >= 7 && ApplicationCap.Same(r[2], row.Company) && TrackerRows.IsApplication(r)
            && (r[6].Contains(row.Role, StringComparison.OrdinalIgnoreCase) || (row.Link.Length > 0 && r[6].Contains(row.Link, StringComparison.OrdinalIgnoreCase))));

    [GeneratedRegex(@"RULE 50|\baccount\b|sign[- ]?in|password|form only|FORM APPLY|swap", RegexOptions.IgnoreCase)]
    private static partial Regex Only();

    [GeneratedRegex(@"RULE 50|swap", RegexOptions.IgnoreCase)]
    private static partial Regex Swap();

    [GeneratedRegex(@"LIVE PROCESS|interview lane", RegexOptions.IgnoreCase)]
    private static partial Regex Live();

    [GeneratedRegex(@"\$\s?\d[\d,]*(?:\.\d+)?\s*(?:K|k)?\s*(?:-|–|to)\s*\$?\s?(?<top>\d[\d,]*)(?<k>\s*[Kk])?")]
    private static partial Regex Band();
}
