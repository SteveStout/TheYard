using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Registry;

/// <summary>
/// Reads the tail of state.md for a lane that holds LinkedIn or a Chrome now. A lane's rows are fragments, each ending
/// in its AID stamp. A lane is open from its first row until a row says CLOSED, whatever marker its rows carry, because
/// a third of the real rows carry none; a note on a closed lane does not reopen it unless it says LIVE again. An open
/// lane holds the gate when it says so: a "Holds:" clause naming LinkedIn or a Chrome, standing until a later clause
/// says none or released. The dispatcher reads the same clause, so both sides keep one rule. A lane that never wrote
/// such a clause holds only when a row names the browser extension. A row that only mentions LinkedIn holds nothing:
/// read as a driver, a finished lane with no close row held a whole morning's notes on one word. A row older than
/// Rule 19's four hours no longer holds the gate, because a lane that died without closing would otherwise stop every
/// morning after it. This process's own lane id is left out: the run lock is what keeps two of its runs apart.
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

    /// <summary>The ids of the lanes that hold LinkedIn or a Chrome now.</summary>
    /// <param name="tail">The end of state.md.</param>
    /// <param name="now">The instant.</param>
    public static IReadOnlyList<string> Holders(string tail, DateTimeOffset now)
    {
        var newest = new Dictionary<string, (bool Open, bool? Holds, bool Driver, DateTimeOffset At)>(StringComparer.Ordinal);
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
            newest[id] = (open, Said(fragment, known.Holds), known.Driver || Extension().IsMatch(fragment), at);
        }
        return [.. newest
            .Where(lane => lane.Value.Open && (lane.Value.Holds ?? lane.Value.Driver) && now - lane.Value.At < Ceiling && !lane.Key.StartsWith(OwnLane, StringComparison.Ordinal))
            .Select(lane => lane.Key)];
    }

    // What the lane holds after this row. A clause naming LinkedIn or a Chrome sets the hold, a clause saying none or
    // released lifts it, and a clause naming neither leaves a standing hold alone, because a lane that restates part
    // of its list has not let the browser go. Clauses are read in order, so a later one in the same row has the last
    // word. Null until the lane's first clause.
    private static bool? Said(string fragment, bool? standing)
    {
        foreach (Match clause in Clause().Matches(fragment))
        {
            string list = clause.Groups["list"].Value;
            bool lifted = clause.Groups["released"].Success || Lifted().IsMatch(list);
            standing = !lifted && (Browser().IsMatch(list) || (standing ?? false));
        }
        return standing;
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

    [GeneratedRegex(@"\bholds(?<released>\s+released)?:\s*(?<list>[^|;\n]*?)(?=\.(?:\s|$)|[|;\n]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex Clause();

    [GeneratedRegex(@"^\s*(?:none|nothing)\b|\breleased\b", RegexOptions.IgnoreCase)]
    private static partial Regex Lifted();

    [GeneratedRegex(@"linkedin|chrome", RegexOptions.IgnoreCase)]
    private static partial Regex Browser();

    [GeneratedRegex(@"claude in chrome", RegexOptions.IgnoreCase)]
    private static partial Regex Extension();
}
