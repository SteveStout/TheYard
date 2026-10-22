// The one reader the resource cards' route answers from (ADR: One frame for every resource): a
// card's window from its keep, the month for the dials cached ten minutes, and a live read first
// only when the newest kept reading is stale and this is the recording site. An hourly card whose
// keep is not there yet draws from the hour the recorder holds.
using TheYard.Application;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Api.Features.Admin.Cloud;

// #region cloud-reader
/// <summary>
/// One card's window from its keep, cached a minute per card and window, with a live read first
/// when the newest kept minute is more than a minute past its end and this is the recording site.
/// </summary>
public sealed class CloudReader(IReadOnlyList<(ResourceCard Card, IResourceHistory History)> cards, CloudRecorder? recorder, Func<string, CancellationToken, Task<double?>> cost, TimeProvider clock, string recordingSite = "the recording site") : ICloudCards
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTimeOffset At, CloudReport View)> _cached = new(StringComparer.Ordinal);

    /// <summary>When each unkept card's source was last asked live, so past its hour it is asked once a minute, not on every poll.</summary>
    private readonly Dictionary<string, DateTimeOffset> _asked = new(StringComparer.Ordinal);

    /// <summary>
    /// The thirty days at five minutes, per card, kept ten minutes: it is the heaviest query a card
    /// asks, its 95th and peak move slowly, and every card shares one document store allowance.
    /// </summary>
    private readonly Dictionary<string, (DateTimeOffset At, IReadOnlyList<ResourceBucket> Month)> _month = new(StringComparer.Ordinal);

    /// <summary>True, and the time noted, when an unkept card's source was last asked a minute or more ago.</summary>
    /// <param name="key">The card.</param>
    /// <param name="now">The reader's clock.</param>
    public bool Due(string key, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (_asked.TryGetValue(key, out var asked) && now - asked < TimeSpan.FromMinutes(1))
            {
                return false;
            }
            _asked[key] = now;
            return true;
        }
    }

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
        // A card with no keep yet is read live, minute card or hourly: the recorder holds its newest hour.
        bool stale = availability.Available
            ? lastHour.Count == 0 || now - lastHour[^1].At > card.Stale
            : recorder?.LiveHour(key) is not { } held || now - held.At > TimeSpan.FromMinutes(61);
        if (recorder is not null && stale && (availability.Available || Due(key, now)))
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
        var hours = await history.HoursAsync(now - length, cancellation);
        if (!availability.Available && hours.Count == 0 && recorder?.LiveHour(key) is { } live)
        {
            hours = [live];
        }
        var view = CardFrame.Complete(card.View(new ResourceCardInputs(
            name,
            length,
            now,
            availability,
            lastHour,
            await history.BucketsAsync(now - length, grain, card.Readings, cancellation),
            await MonthAsync(key, history, card.Readings, now, cancellation),
            hours,
            await cost(card.ResourceName, cancellation),
            recordingSite,
            await CostLinesAsync(card, cancellation))), card.SpecSource, card.Hourly);
        lock (_gate)
        {
            _cached[slot] = (now, view);
        }
        return view;
    }

    /// <summary>Each of a card's bill names with its month to date, read through the same cost function; null for a card with one line.</summary>
    private async Task<IReadOnlyDictionary<string, double?>?> CostLinesAsync(ResourceCard card, CancellationToken cancellation)
    {
        if (card.CostNames is not { } names)
        {
            return null;
        }
        var lines = new Dictionary<string, double?>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            lines[name] = await cost(name, cancellation);
        }
        return lines;
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
