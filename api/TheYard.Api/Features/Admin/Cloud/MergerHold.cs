// The merger's reading between two asks of the Function's health route (ADR-109, the addendum on what the grant pays
// for). The recorder reads five minutes back each minute and writes again any minute whose values changed, so a reading
// put on one minute and missing from the next read of that same minute was written over without it, and the nightly
// card's merger row read "not read" all day. The reading is now put back on the minute it landed on at every read
// until the next ask.
namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>Asks the health route once in <see cref="MergerReading.Every"/> and keeps its reading on the minute it landed on.</summary>
public sealed class MergerHold
{
    private readonly object _gate = new();
    private DateTimeOffset _asked = DateTimeOffset.MinValue;
    private (DateTimeOffset At, IReadOnlyDictionary<string, double?> Values)? _held;

    /// <summary>True when the route has not been asked in the last <see cref="MergerReading.Every"/>.</summary>
    /// <param name="to">The read's end.</param>
    public bool Due(DateTimeOffset to)
    {
        lock (_gate)
        {
            return to - _asked >= MergerReading.Every;
        }
    }

    /// <summary>Asks the route, and counts the ask whatever it answers, so a cold Function is not asked every minute.</summary>
    /// <param name="host">The Function app's default host name.</param>
    /// <param name="to">The read's end.</param>
    /// <param name="cancellation">Stops the read.</param>
    public Task<IReadOnlyDictionary<string, double?>?> ReadAsync(string host, DateTimeOffset to, CancellationToken cancellation)
    {
        lock (_gate)
        {
            _asked = to;
        }
        return MergerReading.ReadAsync(host, cancellation);
    }

    /// <summary>
    /// The minutes with a fresh reading folded on as <see cref="MergerReading.Fold"/> puts it, remembered by the minute it
    /// landed on; with no fresh reading, the minutes with the remembered one put back on that same minute when it is there.
    /// </summary>
    /// <param name="minutes">The Function's metric minutes, oldest first.</param>
    /// <param name="merger">This read's five readings, or null when the route was not asked or did not answer.</param>
    /// <param name="to">The read's end.</param>
    /// <param name="version">The build that read them.</param>
    public IReadOnlyList<ResourceMinute> Fold(IReadOnlyList<ResourceMinute> minutes, IReadOnlyDictionary<string, double?>? merger, DateTimeOffset to, string version)
    {
        if (merger is not null)
        {
            var folded = MergerReading.Fold(minutes, merger, to, version);
            lock (_gate)
            {
                _held = (folded[^1].At, merger);
            }
            return folded;
        }
        (DateTimeOffset At, IReadOnlyDictionary<string, double?> Values)? held;
        lock (_gate)
        {
            held = _held;
        }
        return held is not { } kept ? minutes : [.. minutes.Select(minute => minute.At == kept.At ? minute with { Values = With(minute.Values, kept.Values) } : minute)];
    }

    private static Dictionary<string, double?> With(IReadOnlyDictionary<string, double?> values, IReadOnlyDictionary<string, double?> merger)
    {
        var merged = new Dictionary<string, double?>(values, StringComparer.Ordinal);
        foreach (var (key, value) in merger)
        {
            merged[key] = value;
        }
        return merged;
    }
}
