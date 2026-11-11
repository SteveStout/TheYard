using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Campaign;

/// <summary>
/// One posting as a public board answers it, before it is a row: the board (Source and BoardToken), the board's own
/// id for it, the title and its level, the location and the remote stance read from it, the pay the board published
/// (null when it published none, never read out of the text), the address and the day the board says it went up.
/// </summary>
/// <param name="Source">greenhouse, lever or ashby.</param>
/// <param name="BoardToken">The board's token, as the scan reads it.</param>
/// <param name="PostingId">The board's own id for the posting.</param>
/// <param name="Title">The title as written.</param>
/// <param name="Location">The location as written.</param>
/// <param name="Url">The posting's public address.</param>
/// <param name="Posted">The day the board says it was published, or null.</param>
public sealed record Posting(string Source, string BoardToken, string PostingId, string Title, string Location, string Url, DateOnly? Posted)
{
    /// <summary>The level the title names (PostingLevel).</summary>
    public string Level => PostingLevel.Of(Title);

    /// <summary>Remote, Hybrid, Onsite or Unknown, read from the board's workplace field and the location.</summary>
    public string RemoteStance { get; init; } = "Unknown";

    /// <summary>The published band's low end, whole currency units a year, or null.</summary>
    public int? CompMin { get; init; }

    /// <summary>The published band's high end, or null.</summary>
    public int? CompMax { get; init; }

    /// <summary>ISO currency, or null.</summary>
    public string? CompCurrency { get; init; }

    /// <summary>The band's interval as the board wrote it ("year", "1 YEAR", "per-year-salary"), or null.</summary>
    public string? CompInterval { get; init; }

    /// <summary>
    /// The instant the board says it went up, when the board gives one: Greenhouse's first_published, Lever's createdAt,
    /// Ashby's publishedAt. Minutes to first sight are counted from it, which the day in <see cref="Posted"/> cannot do.
    /// </summary>
    public DateTimeOffset? PublishedAt { get; init; }

    /// <summary>The description as plain text; hashed for ContentHash and stored only when the hash is new.</summary>
    public string Text { get; init; } = "";

    /// <summary>"ats" when the band came from the board's own pay field, null when there is no band.</summary>
    public string? CompSource => CompMin is null && CompMax is null ? null : "ats";

    /// <summary>Lower-case hex SHA-256 of the description's UTF-8 bytes.</summary>
    public string ContentHash => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Text)));

    /// <summary>A row's identity as one string, its board and the board's id, the way the table's unique key reads it.</summary>
    public static string Known(string source, string boardToken, string postingId) => $"{source}|{boardToken}|{postingId}";
}

/// <summary>A posting row as the store holds it, for the scan to compare against.</summary>
/// <param name="Key">The row's id, the PostingKey the change and note tables carry.</param>
/// <param name="PostingId">The board's own id.</param>
/// <param name="Title">The stored title.</param>
/// <param name="Level">The stored level.</param>
/// <param name="RemoteStance">The stored stance.</param>
/// <param name="CompMin">The stored band's low end.</param>
/// <param name="CompMax">The stored band's high end.</param>
/// <param name="ContentHash">The stored description hash.</param>
/// <param name="ClosedAt">When a scan last found it gone, or null while open.</param>
public sealed record StoredPosting(long Key, string PostingId, string Title, string Level, string RemoteStance, int? CompMin, int? CompMax, string ContentHash, DateTimeOffset? ClosedAt);

/// <summary>
/// The posting rules the scan applies, no I/O: the remote stance from what a board writes, the fields a re-scan
/// compares (Title, Level, CompMin, CompMax, RemoteStance, one change row each), and the re-post rule: a new
/// posting at the same board whose title, its level words taken out, matches a posting closed in the last 30 days
/// and whose level is within one rung of it.
/// </summary>
public static partial class PostingRules
{
    /// <summary>How long a closed posting can be the original of a re-post.</summary>
    public static readonly TimeSpan RepostWindow = TimeSpan.FromDays(30);

    /// <summary>The remote stance from a board's workplace field (remote, hybrid, onsite) and the location text.</summary>
    public static string Stance(string? workplace, string? location, bool? isRemote = null)
    {
        string w = (workplace ?? string.Empty).ToLowerInvariant().Replace("-", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal);
        string where = location ?? string.Empty;
        if (w == "hybrid" || Hybrid().IsMatch(where)) { return "Hybrid"; }
        if (w == "remote" || isRemote == true || RemoteWord().IsMatch(where)) { return "Remote"; }
        if (w is "onsite" or "inoffice" or "office") { return "Onsite"; }
        return "Unknown";
    }

    /// <summary>Each compared field that differs, as (field, old, new) with numbers in invariant text.</summary>
    public static IReadOnlyList<(string Field, string? Old, string? New)> Changes(StoredPosting stored, Posting seen)
    {
        var changes = new List<(string, string?, string?)>();
        void Compare(string field, string? old, string? now)
        {
            if (!string.Equals(old, now, StringComparison.Ordinal))
            {
                changes.Add((field, old, now));
            }
        }
        Compare("Title", stored.Title, seen.Title);
        Compare("Level", stored.Level, seen.Level);
        Compare("CompMin", Number(stored.CompMin), Number(seen.CompMin));
        Compare("CompMax", Number(stored.CompMax), Number(seen.CompMax));
        Compare("RemoteStance", stored.RemoteStance, seen.RemoteStance);
        return changes;
    }

    /// <summary>The closed posting a new one re-posts, or null: same title once the level words are out, closed within 30 days, level within one rung.</summary>
    public static StoredPosting? RepostOf(Posting seen, IEnumerable<StoredPosting> board, DateTimeOffset now) =>
        board.Where(p => p.ClosedAt is { } closed && now - closed <= RepostWindow && p.PostingId != seen.PostingId)
            .Where(p => PostingLevel.Base(p.Title) == PostingLevel.Base(seen.Title) && PostingLevel.Base(seen.Title).Length > 0)
            .Where(p => Math.Abs(PostingLevel.Rank(p.Level) - PostingLevel.Rank(seen.Level)) <= 1)
            .OrderByDescending(p => p.ClosedAt).FirstOrDefault();

    /// <summary>Markup out of a board's description: tags removed, entities decoded, runs of space folded.</summary>
    public static string Plain(string? html) =>
        Spaces().Replace(System.Net.WebUtility.HtmlDecode(Tags().Replace(System.Net.WebUtility.HtmlDecode(html ?? string.Empty), " ")), " ").Trim();

    private static string? Number(int? value) => value?.ToString(CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\bhybrid\b", RegexOptions.IgnoreCase)]
    private static partial Regex Hybrid();

    [GeneratedRegex(@"\b(remote|anywhere)\b", RegexOptions.IgnoreCase)]
    private static partial Regex RemoteWord();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
