using System.Globalization;

namespace JobHunt.Domain.Features.NetworkLog;

/// <summary>
/// Where an unfinished backfill picks up. The first pass reads the window newest first; when it runs out of time or
/// meets the thread cap, the threads it never reached are older than every thread it did read, so the next pass asks
/// Gmail for the window again before that point, and so on until a read comes back whole. The point lives in one file
/// on the share: a time while there is more to read, "done" once there is not.
/// </summary>
public static class BackfillRule
{
    /// <summary>The resume point's file on the share.</summary>
    public const string Path = "networklog/backfill-before.txt";

    /// <summary>The resume point a file holds, or null when it is empty, says done, or does not parse.</summary>
    /// <param name="text">The file's text.</param>
    public static DateTimeOffset? Read(string text)
    {
        string first = text.Trim().Split(' ', '\n', '\r')[0];
        return DateTimeOffset.TryParse(first, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : null;
    }

    /// <summary>The file's text: the resume point, or "done" and when.</summary>
    /// <param name="before">The resume point, null when the backfill is whole.</param>
    /// <param name="now">The pass's time.</param>
    public static string Text(DateTimeOffset? before, DateTimeOffset now) =>
        before is { } at ? at.ToString("o", CultureInfo.InvariantCulture) + "\n" : "done " + now.ToString("o", CultureInfo.InvariantCulture) + "\n";

    /// <summary>
    /// The next resume point after a backfill read: null when the read was whole (not cut and under the cap), else one
    /// second past the oldest newest-message among the threads read. A read that got no further than the last point
    /// keeps that point when it was cut, so a slow morning tries again, and ends the backfill when it was only capped,
    /// so a window that cannot move never loops.
    /// </summary>
    /// <param name="cut">The pass ran out of time before reading every thread listed.</param>
    /// <param name="capped">The list came back at the thread cap, so Gmail may hold more.</param>
    /// <param name="read">The backfill's threads that were read.</param>
    /// <param name="previous">The point this read started from, null on the first pass.</param>
    public static DateTimeOffset? Next(bool cut, bool capped, IEnumerable<ThreadSummary> read, DateTimeOffset? previous)
    {
        if (!cut && !capped)
        {
            return null;
        }
        var newest = read.Where(t => t.Messages.Count > 0).Select(t => t.Messages.Max(m => m.At)).ToList();
        DateTimeOffset? next = newest.Count > 0 ? newest.Min().AddSeconds(1) : null;
        if (next is { } at && (previous is not { } before || at < before))
        {
            return at;
        }
        return cut ? previous : null;
    }
}
