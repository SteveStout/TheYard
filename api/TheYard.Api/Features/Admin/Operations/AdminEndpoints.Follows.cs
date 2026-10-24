// The memory-against-traffic card's read: the window's points, a minute or a kept bucket each, and
// the rule's verdict on them. Its own file because it is one card's read with its own wire; the
// route that points at it is mapped in AdminEndpoints.cs (ADR: Memory follows traffic).


namespace TheYard.Api.Features.Admin.Operations;

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


    /// <summary>Answers the memory-against-traffic card over the last hour or a kept window.</summary>
    private static async Task<IResult> Follows(string? window, ReadFollowsHandler handler, CancellationToken cancellation) =>
        Answer(await handler.HandleAsync(new ReadFollowsQuery(window), cancellation));
}
