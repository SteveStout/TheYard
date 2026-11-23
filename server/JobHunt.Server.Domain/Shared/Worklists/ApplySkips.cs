using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Worklists;

/// <summary>
/// The apply lane's memory of the postings it read and would not submit. A form it skipped (a signature page, an essay it
/// has no banked text for, a forced onsite question, a posting that is gone) is as unsubmittable tomorrow as it was this
/// morning, and left on the list it takes one of the day's few rows again, so a run could spend its whole room on the same
/// ten forms and never reach the postings behind them. A skip is kept for a week, by the posting's address, in one table
/// of three cells (address, day, reason); a skip that is the run's own trouble (a browser that froze, a run that ended
/// before the row) is not kept, because the form was never read. A skip whose reason is the company's and not the posting's
/// (see <see cref="Boards"/>) holds the company's whole board for the week. Taking a line out of the table, or naming the
/// address in apply-reopen.tsv, puts the posting back on the list.
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
    public static HashSet<string> Remembered(string table, DateOnly day) => [.. Recent(table, day).Select(cells => Key(cells[0]))];

    /// <summary>
    /// The company boards skipped within the last <see cref="Days"/> days for a reason that is the company's, as
    /// <see cref="Board"/> reads them: a signature page, a question no bank answers, an account wall, or a board that
    /// sends the applicant to the company's own site. A company serves one form, one set of questions and one board to
    /// all its postings, so its next posting stops on the same thing (heal-server6am-apply-20261123: 1Password four
    /// mornings on one question, Esri three on one redirect, Anduril two on its export-control questions, each under an
    /// address the table had not seen). A skip about the posting itself (gone, closed, its age, its band, its title, where
    /// it sits, an essay) holds that posting alone, and a LinkedIn posting names no board.
    /// </summary>
    /// <param name="table">The table's text.</param>
    /// <param name="day">The Central day.</param>
    public static HashSet<string> Boards(string table, DateOnly day) =>
        [.. Recent(table, day).Where(cells => cells.Length >= 3 && CompanyWide().IsMatch(cells[2])).Select(cells => Board(cells[0])).OfType<string>()];

    /// <summary>
    /// The company's board an address sits on, "greenhouse/esri", "lever/acme" or "ashby/1password", lower case and the
    /// same for both Greenhouse hosts; null for any other address and for Greenhouse's embed pages, which name no company.
    /// </summary>
    /// <param name="link">The address.</param>
    public static string? Board(string link)
    {
        Match match = BoardToken().Match(link.Trim());
        if (!match.Success || match.Groups["token"].Value.Equals("embed", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        string host = match.Groups["host"].Value.ToLowerInvariant();
        string board = host.Contains("greenhouse", StringComparison.Ordinal) ? "greenhouse" : host.Contains("lever", StringComparison.Ordinal) ? "lever" : "ashby";
        return $"{board}/{match.Groups["token"].Value.ToLowerInvariant()}";
    }

    private static IEnumerable<string[]> Recent(string table, DateOnly day) =>
        table.Split('\n').Select(line => line.TrimEnd('\r').Split('\t'))
            .Where(cells => cells.Length >= 2 && DateOnly.TryParseExact(cells[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly written) && day.DayNumber - written.DayNumber is >= 0 and < Days);

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

    [GeneratedRegex(@"^(?:skipped:\s*)?(?:signature page|no banked answer|account wall|company site)\b", RegexOptions.IgnoreCase)]
    private static partial Regex CompanyWide();

    [GeneratedRegex(@"^https?://(?<host>job-boards\.greenhouse\.io|boards\.greenhouse\.io|jobs\.lever\.co|jobs\.ashbyhq\.com)/(?<token>[^/?#\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex BoardToken();

    [GeneratedRegex(@"browser|renderer|froze|frozen|timed? ?out|run ended|not opened|not reached|tab group|could not be (?:read|opened)", RegexOptions.IgnoreCase)]
    private static partial Regex Trouble();
}
