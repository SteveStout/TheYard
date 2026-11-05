using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Campaign;

/// <summary>
/// The watchlist's inbox: a lane that cannot write SQL lists the public boards it found in a tab-separated file under
/// campaign/boards-inbox/ (CompanyKey, Company, BoardKind, BoardToken, Source), and the Function, the one writer of
/// campaign.Companies, adds them. A row the scan cannot read (BoardKind other: Workday, iCIMS and the rest) is
/// counted and skipped, as is a row whose key or token does not read as one; nothing is guessed. The watchlist
/// stops at <see cref="Cap"/> companies with a board.
/// </summary>
public static partial class BoardsInbox
{
    /// <summary>The folder on the share.</summary>
    public const string Folder = "campaign/boards-inbox";

    /// <summary>The import's log, one line a file.</summary>
    public const string Log = "campaign/boards-inbox/import.log";

    /// <summary>
    /// The most companies with a board the daily scan reads. One keyless request each, once a day: about 2 % of the
    /// Function's free grant at two hundred companies, so about 4 % at four hundred.
    /// </summary>
    public const int Cap = 400;

    /// <summary>The kinds the scan reads.</summary>
    public static readonly IReadOnlyList<string> Kinds = ["greenhouse", "lever", "ashby"];

    /// <summary>The file's rows the scan can read, and how many it skipped as another kind or as unreadable.</summary>
    public static (IReadOnlyList<BoardRow> Rows, int Other, int Unread) Parse(string text)
    {
        var rows = new List<BoardRow>();
        int other = 0, unread = 0;
        foreach (string raw in (text ?? string.Empty).Replace("\r", "", StringComparison.Ordinal).Split('\n'))
        {
            string[] cells = raw.Split('\t').Select(c => c.Trim()).ToArray();
            if (raw.Trim().Length == 0 || cells[0] == "CompanyKey")
            {
                continue;
            }
            if (cells.Length < 5)
            {
                unread++;
                continue;
            }
            string kind = cells[2].ToLowerInvariant();
            if (!Kinds.Contains(kind))
            {
                other++;
                continue;
            }
            string key = cells[0].ToLowerInvariant();
            if (!Key().IsMatch(key) || !Token().IsMatch(cells[3]) || cells[1].Length is 0 or > 200)
            {
                unread++;
                continue;
            }
            rows.Add(new BoardRow(key, cells[1], kind, cells[3], cells[4]));
        }
        return ([.. rows.DistinctBy(r => (r.BoardKind, r.BoardToken.ToLowerInvariant()))], other, unread);
    }

    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$")]
    private static partial Regex Key();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]{0,99}$")]
    private static partial Regex Token();
}

/// <summary>One board the inbox lists.</summary>
/// <param name="CompanyKey">The registrable domain, the campaign's key.</param>
/// <param name="Company">The company's name.</param>
/// <param name="BoardKind">greenhouse, lever or ashby.</param>
/// <param name="BoardToken">The board's token.</param>
/// <param name="Source">The file it was found in.</param>
public sealed record BoardRow(string CompanyKey, string Company, string BoardKind, string BoardToken, string Source);
