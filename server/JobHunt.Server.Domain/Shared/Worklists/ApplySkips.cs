using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Worklists;

/// <summary>
/// The apply lane's memory of the postings it read and would not submit. A form it skipped (a signature page, an essay it
/// has no banked text for, a forced onsite question, a posting that is gone) is as unsubmittable tomorrow as it was this
/// morning, and left on the list it takes one of the day's few rows again, so a run could spend its whole room on the same
/// ten forms and never reach the postings behind them. A skip is kept for a week, by the posting's address, in one table
/// of three cells (address, day, reason); a skip that is the run's own trouble (a browser that froze, a run that ended
/// before the row) is not kept, because the form was never read. Taking a line out of the table, or naming the address in
/// apply-reopen.tsv, puts the posting back on the list.
/// </summary>
public static partial class ApplySkips
{
    /// <summary>How many days a skip keeps its posting off the list.</summary>
    public const int Days = 7;

    /// <summary>The table's line for a skipped row, or null when the skip was the run's own trouble.</summary>
    /// <param name="url">The posting's address.</param>
    /// <param name="reason">The reason the lane gave.</param>
    /// <param name="day">The Central day.</param>
    public static string? Line(string url, string reason, DateOnly day)
    {
        string cleaned = Whitespace().Replace(reason, " ").Trim();
        return url.Trim().Length == 0 || cleaned.Length == 0 || Trouble().IsMatch(cleaned)
            ? null
            : $"{url.Trim()}\t{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}\t{cleaned}\n";
    }

    /// <summary>The addresses skipped within the last <see cref="Days"/> days, as <see cref="Key"/> reads them.</summary>
    /// <param name="table">The table's text.</param>
    /// <param name="day">The Central day.</param>
    public static HashSet<string> Remembered(string table, DateOnly day)
    {
        var kept = new HashSet<string>(StringComparer.Ordinal);
        foreach (string[] cells in table.Split('\n').Select(line => line.TrimEnd('\r').Split('\t')).Where(cells => cells.Length >= 2))
        {
            if (DateOnly.TryParseExact(cells[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly written) && day.DayNumber - written.DayNumber is >= 0 and < Days)
            {
                kept.Add(Key(cells[0]));
            }
        }
        return kept;
    }

    /// <summary>
    /// A posting's address reduced to what names it: lower case, no trailing slash, no query or fragment, and no closing
    /// /application, because an Ashby posting is the same page with or without it (the JOBS tables carry the form's
    /// address, the picker the posting's).
    /// </summary>
    /// <param name="link">The address.</param>
    public static string Key(string link)
    {
        string trimmed = link.Trim();
        int end = trimmed.IndexOfAny(['?', '#']);
        string path = (end < 0 ? trimmed : trimmed[..end]).TrimEnd('/').ToLowerInvariant();
        return path.EndsWith("/application", StringComparison.Ordinal) ? path[..^"/application".Length] : path;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"browser|renderer|froze|frozen|timed? ?out|run ended|not opened|not reached|tab group|could not be (?:read|opened)", RegexOptions.IgnoreCase)]
    private static partial Regex Trouble();
}
