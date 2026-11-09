// The one reader the resource cards' route answers from (ADR: One frame for every resource): a
// card's window from its keep, the month for the dials cached ten minutes, and a live read first
// only when the newest kept reading is stale and this is the recording site. An hourly card whose
// keep is not there yet draws from the hour the recorder holds. A view already built answers the
// request, and a newer one is built behind it, so Azure is asked off the request's path; only the
// first read of a window, with nothing built yet, waits for its build.
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Api.Features.Admin.Cloud;

// #region cloud-reader
/// <summary>
/// One card's window from its keep, cached a minute per card and window, with a live read first
/// when the newest kept minute is more than a minute past its end and this is the recording site.
/// </summary>
public sealed class CloudReader(IReadOnlyList<(ResourceCard Card, IResourceHistory History)> cards, CloudRecorder? recorder, Func<string, CancellationToken, Task<double?>> cost, TimeProvider clock, string recordingSite = "the recording site", NightlyProcesses? processes = null, CancellationToken stopping = default) : ICloudCards
{
    /// <summary>How old a built view may be and still answer while its next is built: under the three minutes a minute card reads unread after.</summary>
    public static readonly TimeSpan Served = TimeSpan.FromMinutes(2);

    /// <summary>How long a window nobody has asked for is still kept built by <see cref="KeepWatchedAsync"/>.</summary>
    public static readonly TimeSpan Watched = TimeSpan.FromHours(1);

    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTimeOffset At, CloudReport View)> _cached = new(StringComparer.Ordinal);

    /// <summary>Each window asked for, by slot, with when it was last asked.</summary>
    private readonly Dictionary<string, (string Key, string? Window, DateTimeOffset Asked)> _watched = new(StringComparer.Ordinal);

    /// <summary>The builds running behind a request, one a slot.</summary>
    private readonly Dictionary<string, Task> _building = new(StringComparer.Ordinal);

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

    /// <summary>The window's view: a built one under two minutes old at once, with its next built behind it past a minute; else a build the request waits on.</summary>
    public async Task<CloudReport> ReadAsync(string key, string? window, CancellationToken cancellation)
    {
        _ = cards.Single(entry => entry.Card.Key == key);
        var now = clock.GetUtcNow();
        string slot = Slot(key, window);
        lock (_gate)
        {
            _watched[slot] = (key, window, now);
            if (_cached.TryGetValue(slot, out var hit) && now - hit.At < Served)
            {
                if (now - hit.At >= TimeSpan.FromMinutes(1) && !_building.ContainsKey(slot))
                {
                    _building[slot] = Task.Run(() => BuildBehindAsync(slot, key, window, stopping), CancellationToken.None);
                }
                return hit.View;
            }
        }
        return await BuildAsync(key, window, cancellation);
    }

    /// <summary>Builds again every window asked for in the last hour whose view is a minute old, so the next request finds it built; drops the rest.</summary>
    /// <param name="cancellation">The keeper's stopping.</param>
    public async Task KeepWatchedAsync(CancellationToken cancellation)
    {
        var now = clock.GetUtcNow();
        List<(string Key, string? Window)> due;
        lock (_gate)
        {
            foreach (string gone in _watched.Where(entry => now - entry.Value.Asked > Watched).Select(entry => entry.Key).ToList())
            {
                _watched.Remove(gone);
            }
            due = [.. _watched.Where(entry => !_cached.TryGetValue(entry.Key, out var hit) || now - hit.At >= TimeSpan.FromMinutes(1))
                .Select(entry => (entry.Value.Key, entry.Value.Window))];
        }
        foreach (var (key, window) in due)
        {
            try
            {
                await BuildAsync(key, window, cancellation);
            }
            catch (Exception) when (!cancellation.IsCancellationRequested)
            {
                // A window that cannot be built now keeps the view it has; the next minute asks again.
            }
        }
    }

    /// <summary>Every build running behind a request, finished: the tests' way to read what a build behind left.</summary>
    public Task BuiltBehind()
    {
        lock (_gate)
        {
            return Task.WhenAll(_building.Values.ToList());
        }
    }

    private static string Slot(string key, string? window) => key + ":" + (MachineWindows.Parse(window) ?? MachineWindows.Parse("24h")!.Value).Name;

    /// <summary>One build behind a request, under two minutes and the host's stopping; a failure keeps the view that answered.</summary>
    private async Task BuildBehindAsync(string slot, string key, string? window, CancellationToken cancellation)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        limit.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await BuildAsync(key, window, limit.Token);
        }
        catch (Exception) when (!cancellation.IsCancellationRequested)
        {
            // Nobody waits on this build: the view that answered stays until one succeeds.
        }
        finally
        {
            lock (_gate)
            {
                _building.Remove(slot);
            }
        }
    }

    /// <summary>The window built from the keep now, a live read first when the kept minute is stale, and held for the next request.</summary>
    private async Task<CloudReport> BuildAsync(string key, string? window, CancellationToken cancellation)
    {
        var (card, history) = cards.Single(entry => entry.Card.Key == key);
        var (length, grain, name) = MachineWindows.Parse(window) ?? MachineWindows.Parse("24h")!.Value;
        var now = clock.GetUtcNow();
        string slot = key + ":" + name;
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
            catch (Exception) when (!cancellation.IsCancellationRequested)
            {
                // Any failed live read, Azure's or Netlify's, is the recorder's gap, never this route's 500: a client
                // that timed out throws an OperationCanceledException too, so only the request's own cancellation passes.
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
        if (processes is not null && key == "nightly")
        {
            try
            {
                view = await processes.EnrichAsync(view, cancellation);
            }
            catch (Exception) when (!cancellation.IsCancellationRequested)
            {
                // A workspace or a health route that fails costs the card its two added slots, never the card.
            }
        }
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
