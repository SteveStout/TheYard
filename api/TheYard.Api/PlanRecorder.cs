// The App Service plan kept a minute at a time: the background service that reads Azure Monitor
// once a minute and the plan's slower facts once an hour, and hands both to the keep. Its own
// file because when to read is a separate question from how to read, which is PlanReader.cs.
using TheYard.Application;

namespace TheYard.Api;

// #region plan-recorder
/// <summary>
/// Reads the last five minutes of the plan every minute and keeps each minute Azure reported,
/// so a minute Azure was late with is picked up by the next read rather than lost; writing a
/// minute twice is one document. Once an hour it reads the plan's size, its apps' states and the
/// Activity Log. Only the site whose default store is SQL runs it, because both sites share the
/// plan and one reading of it is the whole truth (ADR: The App Service plan, read every minute).
/// </summary>
public sealed class PlanRecorder(PlanReader reader, IPlanHistory history, BuildInfo build, TimeProvider clock, ILogger<PlanRecorder> logger) : BackgroundService
{
    /// <summary>How far back each read goes, so a minute Azure reported late still lands.</summary>
    public static readonly TimeSpan Lookback = TimeSpan.FromMinutes(5);

    /// <summary>The newest minute this process kept, which tells the endpoint whether to read live.</summary>
    public DateTimeOffset? Newest { get; private set; }

    /// <summary>The hour this process last read the slow facts for.</summary>
    private DateTimeOffset? _hourRead;

    /// <summary>One read, public so the endpoint can ask for one when the kept minute is stale and the suite can run one.</summary>
    public async Task<int> RecordOnceAsync(CancellationToken cancellation)
    {
        if (!reader.Configured || !(await history.AvailabilityAsync(cancellation)).Available)
        {
            return 0;
        }
        var now = clock.GetUtcNow();
        var minutes = await reader.ReadMinutesAsync(now - Lookback, now, build.Version, cancellation);
        foreach (var minute in minutes)
        {
            await history.AppendMinuteAsync(minute, cancellation);
        }
        if (minutes.Count > 0)
        {
            Newest = minutes.Max(minute => minute.At);
        }

        var hour = MachineWindows.BucketOf(now, MachineGrain.Hour);
        if (_hourRead != hour)
        {
            await history.AppendHourAsync(await reader.ReadHourAsync(hour, _hourRead ?? hour.AddHours(-1), cancellation), cancellation);
            _hourRead = hour;
        }
        return minutes.Count;
    }

    /// <summary>The loop: a read a minute, ten seconds past the minute; with nothing to ask with, a check every ten minutes.</summary>
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                if (!reader.Configured)
                {
                    return;
                }
                await RecordOnceAsync(stopping);
                var now = clock.GetUtcNow();
                await Task.Delay(now.UtcMinute.AddMinutes(1).AddSeconds(10) - now, stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The type, never the message: an answer from Azure names the resource it refused.
                logger.LogWarning("A minute of the plan could not be kept ({Exception})", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromMinutes(1), stopping).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }
}
// #endregion plan-recorder
