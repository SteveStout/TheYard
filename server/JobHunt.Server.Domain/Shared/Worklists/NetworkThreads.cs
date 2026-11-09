using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Worklists;

/// <summary>One LinkedIn thread where both sides wrote, as the network lane read it.</summary>
/// <param name="Person">The other person's name as the thread shows it.</param>
/// <param name="Company">Their company, empty when the thread does not show one.</param>
/// <param name="ProfileUrl">Their profile's address.</param>
/// <param name="ThreadId">The thread's id from its address.</param>
/// <param name="LastInbound">Their newest message.</param>
/// <param name="LastOutbound">Steve's newest message.</param>
/// <param name="FirstTwoWay">The first time both sides had written.</param>
public sealed record NetworkThread(string Person, string Company, string ProfileUrl, string ThreadId, DateTimeOffset LastInbound, DateTimeOffset LastOutbound, DateTimeOffset FirstTwoWay);

/// <summary>
/// The network lane's TSV, the same file the Function reads as its LinkedIn spool (LinkedInSpool, same header, same
/// seven columns): one row a thread where both sides wrote, times in ISO 8601 with an offset. The program holds each
/// row before it is spooled: a profile address on linkedin.com/in/, a thread id that is safe as a file name, both
/// sides' times, and a first two-way time no later than either. A row that fails is named and left out, never fixed.
/// </summary>
public static partial class NetworkThreads
{
    /// <summary>The header, the Function's LinkedInSpool.Header character for character.</summary>
    public const string Header = "Person\tCompany\tProfileUrl\tThreadId\tLastInbound\tLastOutbound\tFirstTwoWay";

    /// <summary>The rows that hold, and each refused line with why.</summary>
    /// <param name="text">The file's text.</param>
    public static (IReadOnlyList<NetworkThread> Rows, IReadOnlyList<string> Refused) Parse(string text)
    {
        var rows = new List<NetworkThread>();
        var refused = new List<string>();
        string[] lines = (text ?? string.Empty).Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        if (lines[0].Trim() != Header)
        {
            return ([], ["line 1: not the network lane's header"]);
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int n = 1; n < lines.Length; n++)
        {
            if (lines[n].Trim().Length == 0)
            {
                continue;
            }
            string[] f = lines[n].Split('\t');
            string? why = f.Length != 7 ? "needs seven tab-separated fields"
                : f[0].Trim().Length == 0 ? "needs the person's name"
                : !Profile().IsMatch(f[2].Trim()) ? "needs a profile address on linkedin.com/in/"
                : !Thread().IsMatch(f[3].Trim()) ? "needs a thread id of letters, digits, -, _, + or ="
                : !When(f[4], out _) || !When(f[5], out _) || !When(f[6], out _) ? "needs both sides' times and the first two-way time, ISO 8601 with an offset"
                : !seen.Add(f[3].Trim()) ? "the same thread twice"
                : null;
            if (why is null)
            {
                When(f[4], out var lastIn);
                When(f[5], out var lastOut);
                When(f[6], out var first);
                why = first > lastIn || first > lastOut ? "the first two-way time is after a later message" : null;
                if (why is null)
                {
                    rows.Add(new NetworkThread(f[0].Trim(), f[1].Trim(), f[2].Trim(), f[3].Trim(), lastIn, lastOut, first));
                    continue;
                }
            }
            refused.Add($"line {n + 1}: {why}");
        }
        return (rows, refused);
    }

    /// <summary>The rows as the spool file: the header and one line each.</summary>
    /// <param name="rows">The rows that hold.</param>
    public static string Render(IEnumerable<NetworkThread> rows)
    {
        var text = new StringBuilder(Header).Append('\n');
        foreach (NetworkThread row in rows)
        {
            text.Append(CultureInfo.InvariantCulture, $"{row.Person}\t{row.Company}\t{row.ProfileUrl}\t{row.ThreadId}\t{Iso(row.LastInbound)}\t{Iso(row.LastOutbound)}\t{Iso(row.FirstTwoWay)}\n");
        }
        return text.ToString();
    }

    /// <summary>The newest exchange in the rows, or the mark already held when it is newer or there are no rows.</summary>
    /// <param name="rows">The rows that hold.</param>
    /// <param name="held">The mark already on the share, null before the first run.</param>
    public static DateTimeOffset? HighWater(IEnumerable<NetworkThread> rows, DateTimeOffset? held)
    {
        DateTimeOffset? newest = held;
        foreach (NetworkThread row in rows)
        {
            DateTimeOffset at = row.LastInbound > row.LastOutbound ? row.LastInbound : row.LastOutbound;
            newest = newest is { } n && n >= at ? n : at;
        }
        return newest;
    }

    /// <summary>The oldest newest-exchange in the rows, or the floor already held when it is older or there are no rows.</summary>
    /// <param name="rows">The rows that hold.</param>
    /// <param name="held">The floor already on the share, null before the backlog began.</param>
    public static DateTimeOffset? Floor(IEnumerable<NetworkThread> rows, DateTimeOffset? held)
    {
        DateTimeOffset? oldest = held;
        foreach (NetworkThread row in rows)
        {
            DateTimeOffset at = row.LastInbound > row.LastOutbound ? row.LastInbound : row.LastOutbound;
            oldest = oldest is { } o && o <= at ? o : at;
        }
        return oldest;
    }

    /// <summary>A mark as the file holds it.</summary>
    /// <param name="at">The mark.</param>
    public static string Iso(DateTimeOffset at) => at.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    /// <summary>A time from the TSV or the mark's file: ISO 8601 with an offset, nothing else.</summary>
    /// <param name="text">The field.</param>
    /// <param name="at">The time.</param>
    public static bool When(string text, out DateTimeOffset at) =>
        Offset().IsMatch(text.Trim()) & DateTimeOffset.TryParse(text.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out at);

    [GeneratedRegex(@"^https://www\.linkedin\.com/in/[A-Za-z0-9_%\-]+/?$")]
    private static partial Regex Profile();

    [GeneratedRegex(@"^[A-Za-z0-9_+=\-]{3,200}$")]
    private static partial Regex Thread();

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d+)?)?(Z|[+\-]\d{2}:\d{2})$")]
    private static partial Regex Offset();
}
