using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Worklists;

/// <summary>One posting a sweep or the scored leads put forward.</summary>
/// <param name="Company">The company.</param>
/// <param name="Role">The role's title.</param>
/// <param name="Link">The posting's link.</param>
/// <param name="Board">What the source says about the board.</param>
/// <param name="Source">file:line.</param>
/// <param name="Posted">The posted date the source read off the board, empty when it gives none.</param>
public sealed record Posting(string Company, string Role, string Link, string Board, string Source, string Posted = "");

/// <summary>
/// Reads the postings the apply lane may work: the CONFIRMED LIVE and READY TO APPLY tables of the newest sweeps, and
/// the leads scored 8 or above. Then sorts them by whether a form can be reached at all. Only LinkedIn Easy Apply and
/// the Greenhouse, Lever and Ashby forms take an application with no sign-in; anything else, or a posting a source
/// marks with an account wall, goes to the day's blockers list and never reaches the model.
/// </summary>
public static partial class ApplyBoards
{
    /// <summary>The newest a lead may be scored and still be read: Rule 58's limit on a posting's age.</summary>
    public const int NewestDays = 21;

    /// <summary>The score a lead needs.</summary>
    public const int Score = 8;

    /// <summary>The rows of a sweep's confirmed tables.</summary>
    /// <param name="name">The file's name.</param>
    /// <param name="text">The file's text.</param>
    public static IReadOnlyList<Posting> Confirmed(string name, string text)
    {
        string[] lines = text.Split('\n');
        var postings = new List<Posting>();
        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith('#') || !Heading().IsMatch(lines[i]))
            {
                continue;
            }
            int header = Array.FindIndex(lines, i + 1, line => line.StartsWith('|') || line.StartsWith('#'));
            if (header < 0 || lines[header].StartsWith('#'))
            {
                continue;
            }
            string[] names = Cells(lines[header]);
            int company = Array.IndexOf(names, "Company"), role = Array.IndexOf(names, "Role"), board = Array.IndexOf(names, "Board"), link = Array.IndexOf(names, "Link"), posted = Array.IndexOf(names, "Posted");
            if (company < 0 || role < 0 || link < 0)
            {
                continue;
            }
            for (int j = header + 2; j < lines.Length && lines[j].StartsWith('|'); j++)
            {
                string[] cells = Cells(lines[j]);
                if (cells.Length > link && !lines[j].Contains("DROPPED", StringComparison.OrdinalIgnoreCase))
                {
                    postings.Add(new Posting(Parenthesis().Replace(cells[company], string.Empty).Trim(), cells[role], cells[link], board >= 0 && cells.Length > board ? cells[board] : string.Empty, $"{name}:{j + 1}", posted >= 0 && cells.Length > posted ? cells[posted] : string.Empty));
                }
            }
        }
        return postings;
    }

    /// <summary>The scored leads at <see cref="Score"/> or above and under <see cref="NewestDays"/> days, named from LEADS.md.</summary>
    /// <param name="scored">leads/scored.tsv.</param>
    /// <param name="leads">nightly/LEADS.md.</param>
    /// <param name="today">The Central day.</param>
    public static IReadOnlyList<Posting> Scored(string scored, string leads, DateOnly today)
    {
        var named = LeadLine().Matches(leads).ToDictionary(m => m.Groups["link"].Value.Trim(), m => m, StringComparer.Ordinal);
        var postings = new List<Posting>();
        string[] lines = scored.Replace("\r", string.Empty).Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string[] cells = lines[i].Split('\t');
            if (cells.Length < 4 || !int.TryParse(cells[1], out int score) || score < Score
                || !DateOnly.TryParse(cells[3].Split(' ')[0], out var seen) || today.DayNumber - seen.DayNumber > NewestDays
                || !named.TryGetValue(cells[0].Trim(), out var lead))
            {
                continue;
            }
            postings.Add(new Posting(lead.Groups["company"].Value.Trim(), lead.Groups["role"].Value.Trim(), cells[0].Trim(), string.Empty, $"leads/scored.tsv:{i + 1}"));
        }
        return postings;
    }

    /// <summary>The posting's age in days on the given day, or null when its source gives no date it can read.</summary>
    /// <param name="posting">The posting.</param>
    /// <param name="today">The Central day.</param>
    public static int? Age(Posting posting, DateOnly today) =>
        posting.Posted.Length >= 10 && DateOnly.TryParseExact(posting.Posted[..10], "yyyy-MM-dd", out DateOnly posted) ? today.DayNumber - posted.DayNumber : null;

    /// <summary>True when the posting takes an application with no account.</summary>
    /// <param name="posting">The posting.</param>
    public static bool Reachable(Posting posting) => !Wall().IsMatch(posting.Board) && NoAccountForm().IsMatch(posting.Link);

    /// <summary>True when the posting is on linkedin.com, which makes it a LinkedIn drive like the other two lanes.</summary>
    /// <param name="posting">The posting.</param>
    public static bool OnLinkedIn(Posting posting) => posting.Link.Contains("linkedin.com", StringComparison.OrdinalIgnoreCase);

    private static string[] Cells(string line) => [.. line.Trim().Trim('|').Split('|').Select(cell => cell.Trim())];

    [GeneratedRegex(@"CONFIRMED LIVE|READY TO APPLY")]
    private static partial Regex Heading();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Parenthesis();

    [GeneratedRegex(@"(?m)^\d+\.\s+(?<company>[^|]+)\|(?<role>[^|]+)\|.*\|\s*(?<link>https://\S+)\s*$")]
    private static partial Regex LeadLine();

    [GeneratedRegex(@"account wall|workday|successfactors|icims|taleo", RegexOptions.IgnoreCase)]
    private static partial Regex Wall();

    [GeneratedRegex(@"^https://(www\.linkedin\.com/jobs/view/|job-boards\.greenhouse\.io/|boards\.greenhouse\.io/|jobs\.lever\.co/|jobs\.ashbyhq\.com/)", RegexOptions.IgnoreCase)]
    private static partial Regex NoAccountForm();
}
