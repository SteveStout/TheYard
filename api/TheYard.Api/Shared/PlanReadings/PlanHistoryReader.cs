// The plan card's reads: each window built from the kept minutes, with a live read of Azure only when the newest
// kept minute is more than a minute old, and kept built so the request answers from a view already there (ADR: One
// frame for every resource, the addendum that gave the plan the Cloud cards' keeping). Its own file because the
// keeping and the freshness rule are what keep the card fast and Azure unbothered.
using TheYard.Application.Features.Admin.Plan;

namespace TheYard.Api.Shared.PlanReadings;

// #region plan-history-reader
/// <summary>
/// One window of the plan card, from the keep. The answer is a view already built while it is under two minutes
/// old, and past one minute the next is built behind the request; and when the newest kept minute is older than
/// sixty seconds a build asks the recorder for one read first, on the SQL site where the recorder runs, so an open
/// card never shows a stale dial while Azure has a fresher one.
/// </summary>
public sealed class PlanHistoryReader(IPlanHistory history, PlanRecorder? recorder, Func<CancellationToken, Task<double?>> planCost, KeptViews<CloudReport> views, string recordingSite = "the recording site") : IPlanCard
{
    /// <summary>How old the newest kept minute may be before the card asks Azure itself.</summary>
    public static readonly TimeSpan Fresh = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();

    /// <summary>The thirty-day spread, kept ten minutes: the heaviest query the card asks, and one that moves slowly.</summary>
    private (DateTimeOffset At, PlanSpread Spread)? _month;

    /// <summary>The plan's windows as built, which the one keeper builds again each minute while somebody asks.</summary>
    public KeptViews<CloudReport> Views => views;

    /// <summary>The window's report from <see cref="Views"/>; an unknown window is read as the day.</summary>
    public Task<CloudReport> ReadAsync(string? window, CancellationToken cancellation)
    {
        var (length, grain, name) = MachineWindows.Parse(window) ?? MachineWindows.Parse("24h")!.Value;
        return views.ReadAsync(name, build => BuildAsync(name, length, grain, build), cancellation);
    }

    /// <summary>The window built from the keep now, a live read first when the newest kept minute is stale.</summary>
    private async Task<CloudReport> BuildAsync(string name, TimeSpan length, MachineGrain grain, CancellationToken cancellation)
    {
        var now = views.Clock.GetUtcNow();
        var availability = await history.AvailabilityAsync(cancellation);
        var lastHour = await history.NewestAsync(60, cancellation);
        if (recorder is not null && availability.Available && (lastHour.Count == 0 || now - lastHour[^1].At > Fresh + TimeSpan.FromMinutes(1)))
        {
            // Azure Monitor reports a minute a minute or two after it ends, so "older than sixty
            // seconds" is measured from the end of the newest minute, not its start.
            try
            {
                await recorder.RecordOnceAsync(cancellation);
                lastHour = await history.NewestAsync(60, cancellation);
            }
            catch (HttpRequestException)
            {
                // Azure refused or failed the read: the card answers from what is kept, and its
                // live-read line says how old that is. A refused read is never a 500.
            }
        }

        return CardFrame.Complete(PlanView.Build(
            name,
            length,
            now,
            availability,
            lastHour,
            await history.BucketsAsync(now - length, grain, cancellation),
            await MonthAsync(now, cancellation),
            await history.HoursAsync(now - length, cancellation),
            await planCost(cancellation),
            recordingSite), PlanRows.SpecSource);
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
