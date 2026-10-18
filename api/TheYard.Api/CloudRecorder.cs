// The one recorder every resource card under Cloud infrastructure shares (ADR: One frame for every
// resource): each card's minute once a minute and its hour once an hour, into its own keep, on the
// SQL site alone. A card is a definition in Cloud/ResourceCatalog.cs, not a recorder of its own.
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

    /// <summary>One read of one card, public so the reader can ask for one when the kept minute is stale.</summary>
    public async Task RecordOnceAsync(string key, CancellationToken cancellation)
    {
        var (card, history) = cards.Single(entry => entry.Card.Key == key);
        if (!(await history.AvailabilityAsync(cancellation)).Available)
        {
            return;
        }
        var now = clock.GetUtcNow();
        var hour = MachineWindows.BucketOf(now, MachineGrain.Hour);
        // An hourly source is asked once in its hour: asking every minute would return the same figure.
        if (!card.Hourly || !_hourRead.TryGetValue(key, out var asked) || asked != hour)
        {
            foreach (var minute in await card.Minutes(now - PlanRecorder.Lookback, now, build.Version, cancellation))
            {
                await history.AppendMinuteAsync(minute, cancellation);
            }
        }
        if (!_hourRead.TryGetValue(key, out var last) || last != hour)
        {
            await history.AppendHourAsync(await card.Hour(hour, _hourRead.ContainsKey(key) ? last : hour.AddHours(-1), cancellation), cancellation);
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
