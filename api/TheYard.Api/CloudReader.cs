// The one reader the resource cards' route answers from (ADR: One frame for every resource): a
// card's window from its keep, the month for the dials cached ten minutes, and a live read first
// only when the newest kept reading is stale and this is the recording site.
using TheYard.Application;

namespace TheYard.Api;

// #region cloud-reader
/// <summary>
/// One card's window from its keep, cached a minute per card and window, with a live read first
/// when the newest kept minute is more than a minute past its end and this is the recording site.
/// </summary>
public sealed class CloudReader(IReadOnlyList<(ResourceCard Card, IResourceHistory History)> cards, CloudRecorder? recorder, Func<string, CancellationToken, Task<double?>> cost, TimeProvider clock)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTimeOffset At, CloudReport View)> _cached = new(StringComparer.Ordinal);

    /// <summary>
    /// The thirty days at five minutes, per card, kept ten minutes: it is the heaviest query a card
    /// asks, its 95th and peak move slowly, and every card shares one document store allowance.
    /// </summary>
    private readonly Dictionary<string, (DateTimeOffset At, IReadOnlyList<ResourceBucket> Month)> _month = new(StringComparer.Ordinal);

    /// <summary>Whether a card by this key exists.</summary>
    public bool Knows(string key) => cards.Any(entry => entry.Card.Key == key);

    public async Task<CloudReport> ReadAsync(string key, string? window, CancellationToken cancellation)
    {
        var (card, history) = cards.Single(entry => entry.Card.Key == key);
        var (length, grain, name) = MachineWindows.Parse(window) ?? MachineWindows.Parse("24h")!.Value;
        var now = clock.GetUtcNow();
        string slot = key + ":" + name;
        lock (_gate)
        {
            if (_cached.TryGetValue(slot, out var hit) && now - hit.At < TimeSpan.FromMinutes(1))
            {
                return hit.View;
            }
        }
        var availability = await history.AvailabilityAsync(cancellation);
        var lastHour = await history.NewestAsync(60, cancellation);
        if (recorder is not null && availability.Available && (lastHour.Count == 0 || now - lastHour[^1].At > card.Stale))
        {
            try
            {
                await recorder.RecordOnceAsync(key, cancellation);
                lastHour = await history.NewestAsync(60, cancellation);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Any failed live read, Azure's or Netlify's, is the recorder's gap, never this route's 500.
                // The card answers from what is kept; the readout's live-read line says how old it is.
            }
        }
        var view = card.View(new ResourceCardInputs(
            name,
            length,
            now,
            availability,
            lastHour,
            await history.BucketsAsync(now - length, grain, card.Readings, cancellation),
            await MonthAsync(key, history, card.Readings, now, cancellation),
            await history.HoursAsync(now - length, cancellation),
            await cost(card.ResourceName, cancellation)));
        lock (_gate)
        {
            _cached[slot] = (now, view);
        }
        return view;
    }

    private async Task<IReadOnlyList<ResourceBucket>> MonthAsync(string key, IResourceHistory history, IReadOnlyList<string> readings, DateTimeOffset now, CancellationToken cancellation)
    {
        lock (_gate)
        {
            if (_month.TryGetValue(key, out var hit) && now - hit.At < TimeSpan.FromMinutes(10))
            {
                return hit.Month;
            }
        }
        var month = await history.BucketsAsync(now - TimeSpan.FromDays(30), MachineGrain.FiveMinutes, readings, cancellation);
        lock (_gate)
        {
            _month[key] = (now, month);
        }
        return month;
    }
}
// #endregion cloud-reader
