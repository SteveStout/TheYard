// The plan card's reads: each window built from the kept minutes and cached for a minute, with a
// live read of Azure only when the newest kept minute is more than a minute old. Its own file
// because the cache and the freshness rule are what keep the card fast and Azure unbothered.
using TheYard.Application;

namespace TheYard.Api;

// #region plan-history-reader
/// <summary>
/// One window of the plan card, from the keep. The answer is cached a minute per window, because
/// a minute is the grain the keep is written at; and when the newest kept minute is older than
/// sixty seconds this asks the recorder for one read before answering, on the SQL site where the
/// recorder runs, so an open card never shows a stale dial while Azure has a fresher one.
/// </summary>
public sealed class PlanHistoryReader(IPlanHistory history, PlanRecorder? recorder, Func<CancellationToken, Task<double?>> planCost, TimeProvider clock)
{
    /// <summary>How old the newest kept minute may be before the card asks Azure itself.</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTimeOffset At, CloudReport View)> _cached = new(StringComparer.Ordinal);

    /// <summary>The thirty-day spread, kept ten minutes: the heaviest query the card asks, and one that moves slowly.</summary>
    private (DateTimeOffset At, PlanSpread Spread)? _month;

    /// <summary>The window's report; an unknown window is read as the day.</summary>
    public async Task<CloudReport> ReadAsync(string? window, CancellationToken cancellation)
    {
        var chosen = MachineWindows.Parse(window) ?? MachineWindows.Parse("24h")!.Value;
        var (length, grain, name) = chosen;
        var now = clock.GetUtcNow();
        lock (_gate)
        {
            if (_cached.TryGetValue(name, out var hit) && now - hit.At < TimeSpan.FromMinutes(1))
            {
                return hit.View;
            }
        }

        var availability = await history.AvailabilityAsync(cancellation);
        var lastHour = await history.NewestAsync(60, cancellation);
        if (recorder is not null && availability.Available && (lastHour.Count == 0 || now - lastHour[^1].At > Fresh + TimeSpan.FromMinutes(1)))
        {
            // Azure Monitor reports a minute a minute or two after it ends, so "older than sixty
            // seconds" is measured from the end of the newest minute, not its start.
            await recorder.RecordOnceAsync(cancellation);
            lastHour = await history.NewestAsync(60, cancellation);
        }

        var view = PlanView.Build(
            name,
            length,
            now,
            availability,
            lastHour,
            await history.BucketsAsync(now - length, grain, cancellation),
            await MonthAsync(now, cancellation),
            await history.HoursAsync(now - length, cancellation),
            await planCost(cancellation));
        lock (_gate)
        {
            _cached[name] = (now, view);
        }
        return view;
    }

    private async Task<PlanSpread> MonthAsync(DateTimeOffset now, CancellationToken cancellation)
    {
        lock (_gate)
        {
            if (_month is { } hit && now - hit.At < TimeSpan.FromMinutes(10))
            {
                return hit.Spread;
            }
        }
        var spread = await history.SpreadAsync(now - TimeSpan.FromDays(30), cancellation);
        lock (_gate)
        {
            _month = (now, spread);
        }
        return spread;
    }
}
// #endregion plan-history-reader
