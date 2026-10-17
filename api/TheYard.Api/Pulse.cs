using System.Text.Json.Serialization;
using TheYard.Application;

namespace TheYard.Api;

// #region pulse-shape
/// <summary>
/// What the first screen says about the site that is running, in one object: how fast it answers,
/// what it has cost this month, and how long it has been up since the last roll.
///
/// <para>Every figure here is nullable and a null means unread, never zero. A zero in any of these
/// places reads as a claim (answered in no time, cost nothing), and a claim the site cannot back is
/// worse than a gap the page admits to.</para>
/// </summary>
/// <param name="UsualP95Ms24h">The usual five minutes at the ninety-fifth percentile over the last
/// day, in milliseconds, or null when nothing was kept. See <see cref="PulseReader.UsualP95"/> for
/// what this is and what it is not. It carries its own wire name: the naming policy puts no
/// underscore before a digit, so it would otherwise go out as usual_p95_ms24h.</param>
/// <param name="Requests24h">How many requests the kept day holds, so a reader knows how much the
/// figure above rests on. Its own wire name too, for the same reason.</param>
/// <param name="MonthToDate">What Azure has charged this month so far, or null when the bill is unread.</param>
/// <param name="ForecastMonth">What Azure forecasts for the whole month, or null when unread.</param>
/// <param name="Currency">The currency both figures are in.</param>
/// <param name="CostReadAtMs">When the bill was last read from Azure, in milliseconds since the epoch.</param>
/// <param name="UptimeSeconds">How long this container has been answering, in seconds.</param>
/// <param name="Version">The build this container is running.</param>
/// <param name="Commit">The short commit that build came from.</param>
/// <param name="ReadAtMs">When this object was built, in milliseconds since the epoch.</param>
public sealed record PulseReply(
    [property: JsonPropertyName("usual_p95_ms_24h")] long? UsualP95Ms24h,
    [property: JsonPropertyName("requests_24h")] int? Requests24h,
    double? MonthToDate,
    double? ForecastMonth,
    string Currency,
    long? CostReadAtMs,
    long UptimeSeconds,
    string Version,
    string Commit,
    long ReadAtMs);
// #endregion pulse-shape

// #region pulse-reader
/// <summary>
/// The one light read behind the first screen (ADR: One reading for the first screen).
///
/// <para>The figures it carries already exist, on two of the Admin tab's heaviest reads: the kept
/// machine minutes and the hourly cost read. The landing page is the page a stranger opens, so it
/// cannot call either of those on every visit. This asks the same sources, keeps the answer for
/// fifteen seconds, and hands every visitor in that window the same object.</para>
///
/// <para>One read at a time: the gate is held while the first caller builds the answer, so a burst
/// of arrivals costs one query rather than one each.</para>
/// </summary>
public sealed class PulseReader(
    IMachineHistory history,
    string site,
    CostHistoryReader costs,
    HostStart start,
    BuildInfo build,
    TimeProvider clock)
{
    /// <summary>How long an answer is handed out before it is built again.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromSeconds(15);

    /// <summary>The day the latency figure covers.</summary>
    public static readonly TimeSpan Day = TimeSpan.FromHours(24);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private PulseReply? _last;
    // Ticks rather than a DateTimeOffset: a reader outside the gate reads this in one word, so it
    // never sees half of one instant and half of another.
    private long _builtAtTicks;

    /// <summary>Whether the answer in hand is still inside its fifteen seconds.</summary>
    private bool Fresh(DateTimeOffset now) =>
        now - new DateTimeOffset(Volatile.Read(ref _builtAtTicks), TimeSpan.Zero) < Every;

    /// <summary>The pulse, built at most once every <see cref="Every"/>.</summary>
    public async Task<PulseReply> ReadAsync(CancellationToken cancellation)
    {
        var now = clock.GetUtcNow();
        if (_last is { } kept && Fresh(now))
        {
            return kept;
        }

        await _gate.WaitAsync(cancellation);
        try
        {
            now = clock.GetUtcNow();
            if (_last is { } again && Fresh(now))
            {
                return again;
            }

            _last = await BuildAsync(now, cancellation);
            Volatile.Write(ref _builtAtTicks, now.UtcTicks);
            return _last;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<PulseReply> BuildAsync(DateTimeOffset now, CancellationToken cancellation)
    {
        IReadOnlyList<MachineBucket> buckets = [];
        if ((await history.AvailabilityAsync(cancellation)).Available)
        {
            buckets = await history.QueryAsync(site, now - Day, MachineGrain.FiveMinutes, cancellation);
        }

        // The month window is what the cost card calls 30d, which is the one that carries the
        // month to date and the forecast.
        var bill = await costs.ReadAsync("30d", cancellation);
        int requests = buckets.Sum(bucket => bucket.Requests);
        return new PulseReply(
            UsualP95(buckets),
            buckets.Count == 0 ? null : requests,
            bill.Available ? bill.MonthToDate : null,
            bill.Available ? bill.ForecastMonth : null,
            bill.Currency,
            bill.ReadAtMs,
            (long)(now - start.At).TotalSeconds,
            build.Version,
            build.Commit,
            now.ToUnixTimeMilliseconds());
    }

    /// <summary>
    /// The usual five minutes at the ninety-fifth percentile, over the buckets given, weighted by
    /// how many requests each bucket answered; null when no bucket answered one.
    ///
    /// <para>What it is not: the ninety-fifth percentile of the day. That would need every request's
    /// duration for a day, and what is kept is each five minutes already folded to its own counts
    /// (ADR: What the machines are doing), where a bucket's figure is its worst minute's
    /// ninety-fifth. So this takes the middle five minutes of the day by request count, which
    /// answers "how long does this site usually take at its slow end" rather than "how slow was
    /// the worst of the day" - the worst is on the Admin tab, where the card has room to say so.
    /// Taking each five minutes at its worst minute makes the figure cautious rather than
    /// flattering, which is the direction a number on a public page should err in.</para>
    ///
    /// <para>Weighted by requests, so a quiet five minutes with two requests in it does not count
    /// the same as a busy one with two hundred.</para>
    /// </summary>
    public static long? UsualP95(IReadOnlyList<MachineBucket> buckets)
    {
        var measured = buckets
            .Where(bucket => bucket.Requests > 0 && bucket.P95Ms is not null)
            .OrderBy(bucket => bucket.P95Ms!.Value)
            .ToList();
        if (measured.Count == 0)
        {
            return null;
        }

        long half = measured.Sum(bucket => (long)bucket.Requests) / 2;
        long seen = 0;
        foreach (var bucket in measured)
        {
            seen += bucket.Requests;
            if (seen > half)
            {
                return (long)Math.Round(bucket.P95Ms!.Value, MidpointRounding.AwayFromZero);
            }
        }

        return (long)Math.Round(measured[^1].P95Ms!.Value, MidpointRounding.AwayFromZero);
    }
}
// #endregion pulse-reader
