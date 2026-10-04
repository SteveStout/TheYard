// The request ring folded a minute at a time, for the Machines card on the Admin tab. It is its
// own file because two readers share it: the hour the card draws, and the minute the recorder in
// MachineRecorder.cs keeps. The rest of the card starts in Machines.cs.
using TheYard.Application;

namespace TheYard.Api;

// #region traffic-minutes
/// <summary>
/// The request ring, a minute at a time: how many requests, their median and
/// ninety-fifth, and how many answered 4xx and 5xx (ADR: The Admin tab, as a
/// product). The ring already leaves out the Admin tab watching itself and the
/// page sweep, so a minute here is a visitor's minute. One function, used
/// twice: the hour on the card is every minute the ring still holds, and the
/// minute the recorder keeps is the same arithmetic over one of them, so the
/// hour and the month are drawn in one unit by construction.
/// </summary>
public static class TrafficMinutes
{
    /// <summary>The given requests grouped by the minute they were answered in, oldest minute first, each with its counts and sorted durations.</summary>
    public static IReadOnlyList<TrafficMinute> From(IReadOnlyList<RequestEntry> requests) =>
        requests
            .GroupBy(request => request.At.UtcMinute)
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                long[] durations = group.Select(request => request.DurationMs).ToArray();
                // Sorted once here, so the page can take any percentile of the
                // hour without sorting five hundred numbers on every refresh.
                Array.Sort(durations);
                return new TrafficMinute(
                    group.Key,
                    durations.Length,
                    Percentiles.Of(durations, 50),
                    Percentiles.Of(durations, 95),
                    group.Count(request => request.Status >= 500),
                    group.Count(request => request.Status is >= 400 and < 500),
                    group.Count(request => request.Status is >= 300 and < 400),
                    group.Count(request => request.Status < 300),
                    durations);
            })
            .ToList();

    /// <summary>
    /// The request ring as the card's hour, with the oldest minute marked when
    /// the ring is full. The ring holds a fixed number of requests, not an hour,
    /// so on a busy hour it reaches back only part of the way: a full ring has
    /// pushed out what came before its oldest request, that minute's counts are
    /// short, and nothing older is held. The mark is how the page knows to say
    /// how many minutes its figures cover instead of calling them the hour.
    /// </summary>
    public static IReadOnlyList<TrafficMinute> OfRing(IReadOnlyList<RequestEntry> requests, int capacity)
    {
        var minutes = From(requests);
        if (minutes.Count == 0 || requests.Count < capacity)
        {
            return minutes;
        }

        return [minutes[0] with { Clipped = true }, .. minutes.Skip(1)];
    }
}

/// <summary>
/// One minute of answered requests. A minute with none is not in the list: there is no median of nothing.
/// The durations ride along, sorted, because the speed tile reads the hour's own median and ninety-fifth
/// over every request in its warm minutes (ADR: The Admin tab, as a product, the addendum on the
/// typical answer): a percentile of an hour cannot be had from sixty minutes' percentiles, and headlining
/// the worst minute's ninety-fifth instead would keep one slow request on the tile for an hour.
/// The ring holds five hundred requests, so this is at most five hundred numbers on an answer the tab
/// reads every thirty seconds.
/// </summary>
/// <param name="At">The start of the minute, UTC.</param>
/// <param name="Requests">How many requests were answered in the minute.</param>
/// <param name="P50Ms">The median duration, in milliseconds.</param>
/// <param name="P95Ms">The 95th percentile duration, in milliseconds.</param>
/// <param name="ServerErrors">How many requests answered with a 5xx status.</param>
/// <param name="ClientErrors">How many requests answered with a 4xx status.</param>
/// <param name="Redirects">How many requests answered with a 3xx status.</param>
/// <param name="Ok">How many requests answered with a status below 300.</param>
/// <param name="DurationsMs">Every request duration in the minute, sorted, in milliseconds.</param>
/// <param name="Clipped">True on the oldest minute of a full ring: requests earlier in it were pushed out, so its counts are short and nothing before it is held.</param>
public sealed record TrafficMinute(
    DateTimeOffset At,
    int Requests,
    long P50Ms,
    long P95Ms,
    int ServerErrors,
    int ClientErrors,
    int Redirects,
    int Ok,
    IReadOnlyList<long> DurationsMs,
    bool Clipped = false);
// #endregion traffic-minutes
