using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Worklists;

/// <summary>
/// Which of the reply lane's held lines on the messages worklist are settled for good and which are still open (the
/// nightly-jobs fix, R6 item 2). A permanent reason (a blacklist hit, a closed process or thread, a send already made with
/// the ball theirs, an answered thread, nothing owed) is listed once and then retired: it is kept in
/// nightly/messages-retired.tsv beside the tracker row it already cites, and is never carried to another morning. Every
/// other reason (no tracker row names the invite, a paused role, no accepted invitee, company unread) stays open and is
/// carried. Nobody is retired because a read was missing.
/// </summary>
public static partial class LeftOut
{
    /// <summary>True when a held line's reason is settled for good.</summary>
    /// <param name="held">The reason as the reply lane wrote it.</param>
    public static bool Permanent(string held) => Settled().IsMatch(held ?? string.Empty) && !Open().IsMatch(held ?? string.Empty);

    /// <summary>One line of the retired table: name, day, reason.</summary>
    /// <param name="name">The person or company.</param>
    /// <param name="held">The reason.</param>
    /// <param name="day">The Central day.</param>
    public static string Line(string name, string held, DateOnly day) =>
        $"{Key(name)}\t{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}\t{Space().Replace(held, " ").Trim()}\n";

    /// <summary>The names the table has retired.</summary>
    /// <param name="table">nightly/messages-retired.tsv.</param>
    public static HashSet<string> Retired(string table) =>
        [.. (table ?? string.Empty).Split('\n').Select(line => line.Split('\t')[0].Trim()).Where(name => name.Length > 0)];

    /// <summary>A name as the table keys it: lower case, one space.</summary>
    /// <param name="name">The person or company.</param>
    public static string Key(string name) => Space().Replace(name ?? string.Empty, " ").Trim().ToLowerInvariant();

    [GeneratedRegex(@"\bBLACKLIST HIT\b|\bclosed process\b|\bthread closed\b|^closed\b|\bclosed by silence\b|\bthe ball is \w+(?:'s|s')|\banswered in (?:his|her|their) own thread\b|\bnothing owed\b", RegexOptions.IgnoreCase)]
    private static partial Regex Settled();

    [GeneratedRegex(@"\bno tracker row names\b|\bcompany unread\b|\bpaused\b|\bno accepted invitee\b|\bsection [A-E]'s item\b", RegexOptions.IgnoreCase)]
    private static partial Regex Open();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();
}
