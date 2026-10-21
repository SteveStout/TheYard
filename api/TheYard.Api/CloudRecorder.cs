// The one recorder every resource card under Cloud infrastructure shares (ADR: One frame for every
// resource): each card's minute once a minute and its hour once an hour, into its own keep, on the
// SQL site alone. A card is a definition in Cloud/ResourceCatalog.cs, not a recorder of its own.
// A card whose keep is not there yet still has its hour read once an hour and held here, so the
// card reads live until the keep exists; its minutes are not read, since nothing could keep them.
using TheYard.Application;

namespace TheYard.Api;

// #region cloud-recorder
/// <summary>
/// Reads every resource card's minutes once a minute and its hour once an hour, each into its own
/// keep, on the SQL site alone like the plan's recorder. One card's failure is logged by type and
/// leaves the others reading: a minute that could not be kept is a gap on one chart.
/// </summary>
public sealed class CloudRecorder(IReadOnlyList<(ResourceCard Card, IResourceHistory History)> cards, BuildInfo build, TimeProvider clock, ILogger<CloudRecorder> logger) : BackgroundService
{
    private readonly Dictionary<string, DateTimeOffset> _hourRead = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ResourceHour> _liveHour = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>The newest hour read for a card, whether or not its keep took it; null before the first.</summary>
    /// <param name="key">The card's key.</param>
    public ResourceHour? LiveHour(string key)
    {
        lock (_gate)
        {
            return _liveHour.GetValueOrDefault(key);
        }
    }

    /// <summary>One read of one card, public so the reader can ask for one when the kept minute is stale.</summary>
    public async Task RecordOnceAsync(string key, CancellationToken cancellation)
    {
        var (card, history) = cards.Single(entry => entry.Card.Key == key);
        bool kept = (await history.AvailabilityAsync(cancellation)).Available;
        var now = clock.GetUtcNow();
        var hour = MachineWindows.BucketOf(now, MachineGrain.Hour);
        DateTimeOffset? last;
        lock (_gate)
        {
            last = _hourRead.TryGetValue(key, out var asked) ? asked : null;
        }
        // An hourly source is asked once in its hour, and its hour first: an hourly minute may carry
        // what the hour just sampled (the edge's cache share), and asked after, it carried the sample
        // before. A minute's source is asked for its minutes first, as it always was.
        // A failed hour still lets the minutes be kept, as it did when the minutes came first; the
        // failure is thrown after them, for the loop to log.
        Exception? failed = null;
        if (card.Hourly)
        {
            try
            {
                await HourOnceAsync(key, card, history, kept, hour, last, cancellation);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed = ex;
            }
        }
        if (kept && (!card.Hourly || last != hour))
        {
            foreach (var minute in await card.Minutes(now - PlanRecorder.Lookback, now, build.Version, cancellation))
            {
                await history.AppendMinuteAsync(minute, cancellation);
            }
        }
        if (failed is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failed);
        }
        if (!card.Hourly)
        {
            await HourOnceAsync(key, card, history, kept, hour, last, cancellation);
        }
    }

    /// <summary>Reads the card's hour once in its hour, keeps it when there is a keep, and holds it for the reader either way.</summary>
    private async Task HourOnceAsync(string key, ResourceCard card, IResourceHistory history, bool kept, DateTimeOffset hour, DateTimeOffset? last, CancellationToken cancellation)
    {
        if (last == hour)
        {
            return;
        }
        var read = await card.Hour(hour, last ?? hour.AddHours(-1), cancellation);
        if (kept)
        {
            await history.AppendHourAsync(read, cancellation);
        }
        lock (_gate)
        {
            _liveHour[key] = read;
            _hourRead[key] = hour;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            foreach (var (card, _) in cards)
            {
                try
                {
                    await RecordOnceAsync(card.Key, stopping);
                }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning("A minute of {Card} could not be kept ({Exception} {Status})", card.Key, ex.GetType().Name, (ex as HttpRequestException)?.StatusCode);
                }
            }
            var now = clock.GetUtcNow();
            await Task.Delay(now.UtcMinute.AddMinutes(1).AddSeconds(20) - now, stopping).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }
}
// #endregion cloud-recorder
