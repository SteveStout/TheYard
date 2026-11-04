using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Registry;

/// <summary>
/// Reads the tail of state.md for a lane still open on LinkedIn. A lane's rows are fragments, each ending in its
/// AID stamp; the newest row for a lane says whether it is open. A row older than Rule 19's four hours no longer
/// holds the gate, because a lane that died without closing would otherwise stop every morning after it. Rows are read
/// failing closed: a lane is open from its first row until a row says CLOSED, whatever marker its rows carry, because a
/// third of the real rows carry none. A note on a closed lane does not reopen it unless it says LIVE again. This
/// process's own lane id is left out: the run lock is what keeps two of its runs apart.
/// </summary>
public static partial class RegistryTail
{
    /// <summary>This process's lane id.</summary>
    public const string OwnLane = "server6am";

    private static readonly TimeSpan Ceiling = TimeSpan.FromHours(4);

    /// <summary>A spool fragment the merger has not reached, in the shape the merger would leave it.</summary>
    /// <param name="stem">The spool file's name with no extension.</param>
    /// <param name="text">The fragment.</param>
    public static string Stamped(string stem, string text) => $"{text}\n<!-- AID:{stem} -->\n";

    /// <summary>The ids of the lanes open on LinkedIn now.</summary>
    /// <param name="tail">The end of state.md.</param>
    /// <param name="now">The instant.</param>
    public static IReadOnlyList<string> LiveOnLinkedIn(string tail, DateTimeOffset now)
    {
        var newest = new Dictionary<string, (bool Open, bool LinkedIn, DateTimeOffset At)>(StringComparer.Ordinal);
        int from = 0;
        foreach (Match stamp in Aid().Matches(tail))
        {
            string fragment = tail[from..stamp.Index];
            from = stamp.Index + stamp.Length;
            Match lane = Lane().Match(fragment);
            if (!lane.Success || !TryStamp(stamp.Groups["at"].Value, out var at))
            {
                continue;
            }
            string id = lane.Groups["id"].Value;
            bool seen = newest.TryGetValue(id, out var known);
            bool open = !Closed().IsMatch(fragment) && (Open().IsMatch(fragment) || !seen || known.Open);
            newest[id] = (open, known.LinkedIn || LinkedIn().IsMatch(fragment), at);
        }
        return [.. newest
            .Where(lane => lane.Value.Open && lane.Value.LinkedIn && now - lane.Value.At < Ceiling && !lane.Key.StartsWith(OwnLane, StringComparison.Ordinal))
            .Select(lane => lane.Key)];
    }

    private static bool TryStamp(string text, out DateTimeOffset at) =>
        DateTimeOffset.TryParseExact(text, "yyyyMMdd-HHmmss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out at);

    [GeneratedRegex(@"<!-- AID:(?<at>\d{8}-\d{6}Z)[^ ]* -->")]
    private static partial Regex Aid();

    [GeneratedRegex(@"## LANE: (?<id>[\w-]+)")]
    private static partial Regex Lane();

    [GeneratedRegex(@"\|\s*(IN PROGRESS|LIVE)\s*\|\s*$|^\s*## LANE: [\w-]+(?: step \d+)? LIVE", RegexOptions.Multiline)]
    private static partial Regex Open();

    [GeneratedRegex(@"\|\s*CLOSED\s*\|\s*$|^\s*## LANE: [\w-]+(?: step \d+)? CLOSED", RegexOptions.Multiline)]
    private static partial Regex Closed();

    [GeneratedRegex(@"linkedin|connection request|invite|claude in chrome", RegexOptions.IgnoreCase)]
    private static partial Regex LinkedIn();
}
