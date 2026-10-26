namespace TheYard.Api.Shared.Instance;

// #region pulse-day
/// <summary>
/// The first screen's day of kept five-minute buckets (ADR: One frame for every resource, the addendum on the
/// Cosmos DB card's throttling): read whole once, then from a short tail on, folding the tail into the day it
/// already holds, and read whole again every <see cref="WholeEvery"/>. The whole day is a grouped query over every
/// kept minute of the site, about 450 request units on the document store, and the first screen asks for the pulse
/// every fifteen seconds; read whole each time, it was the largest single draw on the throughput the site's visitors
/// read through. The tail is a few buckets, so the day costs a few request units a read.
/// </summary>
/// <param name="history">Where the machine minutes are kept.</param>
/// <param name="site">The site whose minutes are read.</param>
public sealed class PulseDay(IMachineHistory history, string site)
{
    /// <summary>How often the whole day is read again, so a minute written late into an older bucket is never kept wrong for longer.</summary>
    public static readonly TimeSpan WholeEvery = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The tail read again each time: a minute is written a minute after it ends, so the buckets this far back may
    /// still be filling.
    /// </summary>
    public static readonly TimeSpan Tail = TimeSpan.FromMinutes(10);

    private IReadOnlyList<MachineBucket> _day = [];
    private DateTimeOffset? _wholeAt;

    /// <summary>The day's buckets as of <paramref name="now"/>, oldest first. The caller holds one read at a time.</summary>
    /// <param name="now">The instant the day ends at.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<IReadOnlyList<MachineBucket>> ReadAsync(DateTimeOffset now, CancellationToken cancellation)
    {
        var since = now - PulseReader.Day;
        if (_wholeAt is not { } whole || now - whole >= WholeEvery || _day.Count == 0)
        {
            _day = await history.QueryAsync(site, since, MachineGrain.FiveMinutes, cancellation);
            _wholeAt = now;
            return _day;
        }
        var from = MachineWindows.BucketOf(now - Tail, MachineGrain.FiveMinutes);
        var tail = await history.QueryAsync(site, from, MachineGrain.FiveMinutes, cancellation);
        _day = Fold(_day, tail, from, MachineWindows.BucketOf(since, MachineGrain.FiveMinutes));
        return _day;
    }

    /// <summary>The day kept with its tail replaced by the tail read now, and the buckets older than the day dropped.</summary>
    /// <param name="kept">The day as last read.</param>
    /// <param name="tail">The buckets read from <paramref name="from"/> on.</param>
    /// <param name="from">The start of the first bucket the tail holds.</param>
    /// <param name="oldest">The start of the oldest bucket the day still holds.</param>
    public static IReadOnlyList<MachineBucket> Fold(IReadOnlyList<MachineBucket> kept, IReadOnlyList<MachineBucket> tail, DateTimeOffset from, DateTimeOffset oldest) =>
        [.. kept.Where(bucket => bucket.At < from && bucket.At >= oldest).Concat(tail.Where(bucket => bucket.At >= from)).OrderBy(bucket => bucket.At)];
}
// #endregion pulse-day
