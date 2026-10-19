// The memory-against-traffic card's read: the window's points, a minute or a kept bucket each, and
// the rule's verdict on them. Its own file because it is one card's read with its own wire; the
// route that points at it is mapped in AdminEndpoints.cs (ADR: Memory follows traffic).

using TheYard.Application;

namespace TheYard.Api;

/// <summary>One point as the card draws it.</summary>
/// <param name="At">The start of the minute or bucket.</param>
/// <param name="RequestsPerMinute">Requests answered a minute.</param>
/// <param name="CpuPercent">The process's processor, in percent; null when not read.</param>
/// <param name="WorkingSetMb">The process's working set, in megabytes.</param>
public sealed record FollowsPoint(DateTimeOffset At, double RequestsPerMinute, double? CpuPercent, double WorkingSetMb);

/// <summary>The card's whole answer: the window, its points, the limit the memory is read against, and the verdict.</summary>
/// <param name="Window">The window, 1h, 24h, 7d or 30d.</param>
/// <param name="AsOf">The instant the window ends at, on the server's clock.</param>
/// <param name="BucketMinutes">How many minutes one point stands for.</param>
/// <param name="MemoryLimitMb">The memory limit the process runs under, in megabytes.</param>
/// <param name="SkippedWarmUp">True when the hour leaves out the first minutes after the process started, when memory climbs for reasons that are not a leak.</param>
/// <param name="Points">The window's points, oldest first.</param>
/// <param name="Verdict">The rule's finding.</param>
public sealed record FollowsView(
    string Window,
    DateTimeOffset AsOf,
    int BucketMinutes,
    double MemoryLimitMb,
    bool SkippedWarmUp,
    IReadOnlyList<FollowsPoint> Points,
    FollowsVerdictWire Verdict);

/// <summary>The verdict as the wire writes it, the tone in the words every Cloud card uses.</summary>
/// <param name="Tone">good, info or alarm.</param>
/// <param name="Words">The finding in one sentence.</param>
/// <param name="MemoryFromMb">The middle working set of the window's first half.</param>
/// <param name="MemoryToMb">The middle working set of the window's second half.</param>
/// <param name="RequestsFrom">The middle requests a minute of the first half.</param>
/// <param name="RequestsTo">The middle requests a minute of the second half.</param>
/// <param name="ClimbThresholdMb">How far memory must rise to count as climbing.</param>
/// <param name="CpuFollowsTraffic">How closely the processor moved with the requests, -1 to 1.</param>
/// <param name="MemoryFollowsTraffic">How closely the memory moved with the requests, -1 to 1.</param>
public sealed record FollowsVerdictWire(
    string Tone,
    string Words,
    double MemoryFromMb,
    double MemoryToMb,
    double RequestsFrom,
    double RequestsTo,
    double ClimbThresholdMb,
    double? CpuFollowsTraffic,
    double? MemoryFollowsTraffic);

/// <summary>The memory-against-traffic handler of AdminEndpoints; the type is described in AdminEndpoints.cs.</summary>
public static partial class AdminEndpoints
{
    /// <summary>The first minutes of a process's life, left out of the hour: the runtime, the caches and a fill all grow memory while nothing is wrong.</summary>
    public static readonly TimeSpan WarmUp = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Answers the window's points and the verdict. The hour is this process's own, a minute at a
    /// time from the sampler and the request ring, less its first fifteen minutes; a wider window is
    /// the kept buckets, the same ones the machines card draws.
    /// </summary>
    private static async Task<IResult> Follows(string? window, MachineSampler sampler, MachineHistoryReader kept, RequestRingBuffer requestLog, HostStart start, CancellationToken cancellation)
    {
        var now = Clocks.UtcNow();
        string chosen = MachineWindows.Parse(window)?.Name ?? MachineWindows.Hour;
        double limit = MachineSampler.MemoryLimitMb;
        List<FollowsPoint> points;
        int bucketMinutes;
        bool skipped = false;
        if (chosen == MachineWindows.Hour)
        {
            bucketMinutes = 1;
            var samples = sampler.Snapshot();
            var requests = requestLog.Snapshot();
            var warm = start.At + WarmUp;
            skipped = now - TimeSpan.FromHours(1) < warm;
            points = [];
            for (var minute = now.UtcMinute.AddMinutes(-60); minute < now.UtcMinute; minute = minute.AddMinutes(1))
            {
                if (minute < warm)
                {
                    continue;
                }
                if (MachineRecorder.Fold(minute, "here", limit, samples, [], [], requests) is { } folded)
                {
                    points.Add(new FollowsPoint(folded.At, folded.Requests, folded.CpuPercent, folded.WorkingSetMb));
                }
            }
        }
        else
        {
            var (buckets, minutes) = await kept.BucketsAsync(chosen, now, cancellation);
            bucketMinutes = minutes;
            points = [.. buckets.Select(bucket => new FollowsPoint(bucket.At, bucket.Minutes == 0 ? 0 : Math.Round((double)bucket.Requests / bucket.Minutes, 2), bucket.CpuPercent, bucket.WorkingSetMb))];
            limit = buckets.Count == 0 ? limit : buckets[^1].MemoryLimitMb;
        }

        var verdict = MemoryTraffic.Judge([.. points.Select(point => new TrafficPoint(point.At, point.RequestsPerMinute, point.CpuPercent, point.WorkingSetMb))], limit);
        return Results.Json(new FollowsView(
            chosen,
            now,
            bucketMinutes,
            limit,
            skipped,
            points,
            new FollowsVerdictWire(CloudTones.Of(verdict.Tone), verdict.Words, verdict.MemoryFromMb, verdict.MemoryToMb, verdict.RequestsFrom, verdict.RequestsTo, verdict.ClimbThresholdMb, verdict.CpuFollowsTraffic, verdict.MemoryFollowsTraffic)));
    }
}
