using System.Globalization;

namespace TheYard.Application;

// The App Service plan, kept (ADR: The App Service plan, read every minute). Azure Monitor holds
// the plan's own readings for a while, but reading a month back from it on every open of the
// Admin tab would be slow and would spend its allowance on the same answer. One minute is read
// once, kept here for a month, and every window is drawn from the kept copy.

// #region plan-minute
/// <summary>One web app on the plan as Azure Monitor read it for one minute; a figure not read is null, never zero.</summary>
/// <param name="Name">The web app's resource name.</param>
/// <param name="CpuSeconds">CPU time the app used in the minute, in seconds.</param>
/// <param name="WorkingSetMb">The app's average memory working set, in megabytes.</param>
/// <param name="Requests">Requests the app answered in the minute.</param>
/// <param name="Http5xx">Requests it answered with a 5xx status.</param>
/// <param name="Connections">Its average open connections; null on a Linux plan, which does not report them.</param>
/// <param name="FileSystemMb">The plan storage the app is using, in megabytes.</param>
public sealed record PlanAppMinute(string Name, double? CpuSeconds, double? WorkingSetMb, double? Requests, double? Http5xx, double? Connections, double? FileSystemMb = null);

/// <summary>One minute of the plan, read from Azure Monitor; a figure Azure did not report is null, never zero.</summary>
/// <param name="At">The start of the minute, UTC.</param>
/// <param name="CpuPercent">The plan's CPU, averaged over its instances, in percent.</param>
/// <param name="MemoryPercent">The plan's memory, averaged over its instances, in percent.</param>
/// <param name="DiskQueue">The plan's disk queue length.</param>
/// <param name="HttpQueue">The plan's HTTP queue length.</param>
/// <param name="BytesSent">Bytes the plan sent in the minute.</param>
/// <param name="BytesReceived">Bytes the plan received in the minute.</param>
/// <param name="Sockets">Outbound sockets open on the plan.</param>
/// <param name="Version">The build the recording site was running, so a deploy shows as the minute it changed.</param>
/// <param name="Apps">The web apps on the plan, one entry each.</param>
public sealed record PlanMinute(
    DateTimeOffset At,
    double? CpuPercent,
    double? MemoryPercent,
    double? DiskQueue,
    double? HttpQueue,
    double? BytesSent,
    double? BytesReceived,
    double? Sockets,
    string Version,
    IReadOnlyList<PlanAppMinute> Apps);

/// <summary>One point of a windowed chart: the minutes in the bucket averaged, and the peaks kept as peaks.</summary>
/// <param name="At">The start of the bucket, UTC.</param>
/// <param name="Minutes">How many kept minutes it averages.</param>
/// <param name="CpuPercent">The mean CPU, in percent; null when not read.</param>
/// <param name="CpuMaxPercent">The highest minute's CPU, in percent; null when not read.</param>
/// <param name="MemoryPercent">The mean memory, in percent; null when not read.</param>
/// <param name="MemoryMaxPercent">The highest minute's memory, in percent; null when not read.</param>
/// <param name="HttpQueueMax">The longest HTTP queue in the bucket; null when not read.</param>
/// <param name="Version">The newest build seen in the bucket.</param>
public sealed record PlanBucket(DateTimeOffset At, int Minutes, double? CpuPercent, double? CpuMaxPercent, double? MemoryPercent, double? MemoryMaxPercent, double? HttpQueueMax, string? Version);
// #endregion plan-minute

// #region plan-hour
/// <summary>One web app's state as Azure Resource Manager reported it, read once an hour.</summary>
/// <param name="Name">The web app's resource name.</param>
/// <param name="ResourceId">Its full resource id, which the card links to.</param>
/// <param name="State">"Running", "Stopped" or what Azure says.</param>
/// <param name="AlwaysOn">Whether Always On is set; null when not read.</param>
/// <param name="LastModifiedAt">When the app's configuration last changed, which a deploy changes; null when not read.</param>
public sealed record PlanAppState(string Name, string ResourceId, string State, bool? AlwaysOn, DateTimeOffset? LastModifiedAt);

/// <summary>One entry of the plan's Activity Log: something Azure or somebody did to it or its apps.</summary>
/// <param name="At">When it happened, UTC.</param>
/// <param name="Kind">"restart", "scale", "maintenance", "config" or "other".</param>
/// <param name="Title">The operation, in Azure's words.</param>
/// <param name="Resource">The resource name it was done to.</param>
/// <param name="Planned">False when nobody on this subscription asked for it, which is what makes a restart red.</param>
public sealed record PlanEvent(DateTimeOffset At, string Kind, string Title, string Resource, bool Planned);

/// <summary>An hour's slower read: the plan's own facts, the apps' states and the Activity Log entries since the last hour.</summary>
/// <param name="At">The start of the hour, UTC.</param>
/// <param name="PlanId">The plan's resource id.</param>
/// <param name="TenantId">The tenant the subscription belongs to, for the portal links.</param>
/// <param name="Sku">The plan's size, as Azure names it (B1).</param>
/// <param name="Instances">How many instances the plan runs.</param>
/// <param name="Cores">vCPUs per instance.</param>
/// <param name="MemoryGb">Memory per instance, in gigabytes.</param>
/// <param name="Apps">The apps on the plan.</param>
/// <param name="Events">The Activity Log entries in the hour.</param>
/// <param name="Location">The region, as Azure names it (West US 3).</param>
/// <param name="Linux">True when the plan runs Linux.</param>
public sealed record PlanHour(DateTimeOffset At, string PlanId, string? TenantId, string? Sku, int? Instances, double? Cores, double? MemoryGb, IReadOnlyList<PlanAppState> Apps, IReadOnlyList<PlanEvent> Events, string? Location = null, bool? Linux = null);

/// <summary>A window's percentiles, read from the five-minute averages and the minutes' peaks.</summary>
/// <param name="Minutes">How many minutes the window kept.</param>
/// <param name="CpuP95">The 95th percentile of the five-minute CPU means.</param>
/// <param name="CpuPeak">The highest minute's CPU.</param>
/// <param name="MemoryP95">The 95th percentile of the five-minute memory means.</param>
/// <param name="MemoryPeak">The highest minute's memory.</param>
public sealed record PlanSpread(int Minutes, double? CpuP95, double? CpuPeak, double? MemoryP95, double? MemoryPeak);
// #endregion plan-hour

/// <summary>Whether a keep is there, and one sentence about it either way.</summary>
/// <param name="Available">True when minutes can be kept and read.</param>
/// <param name="Reason">What the card says about the keep.</param>
public sealed record PlanHistoryAvailability(bool Available, string Reason);

// #region plan-port
/// <summary>
/// Port: where the plan's minutes and hours are kept. One minute a minute from one site, one
/// hour an hour, a month of each, read back in buckets sized to the window.
/// </summary>
public interface IPlanHistory
{
    Task<PlanHistoryAvailability> AvailabilityAsync(CancellationToken cancellation);

    Task AppendMinuteAsync(PlanMinute minute, CancellationToken cancellation);

    Task AppendHourAsync(PlanHour hour, CancellationToken cancellation);

    /// <summary>The newest minutes kept, newest last, at most <paramref name="count"/> of them.</summary>
    Task<IReadOnlyList<PlanMinute>> NewestAsync(int count, CancellationToken cancellation);

    Task<IReadOnlyList<PlanBucket>> BucketsAsync(DateTimeOffset since, MachineGrain grain, CancellationToken cancellation);

    Task<PlanSpread> SpreadAsync(DateTimeOffset since, CancellationToken cancellation);

    /// <summary>Every hour kept since <paramref name="since"/>, oldest first.</summary>
    Task<IReadOnlyList<PlanHour>> HoursAsync(DateTimeOffset since, CancellationToken cancellation);
}
// #endregion plan-port

/// <summary>The arithmetic the stores share: the bucket keys and the 95th percentile, testable with no store at all.</summary>
public static class PlanFolding
{
    /// <summary>The 95th percentile by nearest rank of the readings that exist, or null when none does.</summary>
    public static double? P95(IEnumerable<double?> values)
    {
        var read = values.Where(value => value is not null).Select(value => value!.Value).Order().ToList();
        if (read.Count == 0)
        {
            return null;
        }
        int rank = (int)Math.Ceiling(0.95 * read.Count);
        return Math.Round(read[Math.Clamp(rank, 1, read.Count) - 1], 2);
    }

    /// <summary>Minutes into buckets, oldest first, the way a store with no grouped query would.</summary>
    public static IReadOnlyList<PlanBucket> Buckets(IEnumerable<PlanMinute> minutes, MachineGrain grain) =>
        minutes
            .GroupBy(minute => MachineWindows.BucketOf(minute.At, grain))
            .OrderBy(group => group.Key)
            .Select(group => new PlanBucket(
                group.Key,
                group.Count(),
                MachineFolding.MeanOf(group.Select(minute => minute.CpuPercent)),
                MachineFolding.MaxOf(group.Select(minute => minute.CpuPercent)),
                MachineFolding.MeanOf(group.Select(minute => minute.MemoryPercent)),
                MachineFolding.MaxOf(group.Select(minute => minute.MemoryPercent)),
                MachineFolding.MaxOf(group.Select(minute => minute.HttpQueue)),
                group.OrderBy(minute => minute.At).Last().Version))
            .ToList();

    /// <summary>A window's spread from its minutes: the 95th of the five-minute means, and the peaks.</summary>
    public static PlanSpread Spread(IReadOnlyList<PlanMinute> minutes)
    {
        var fives = Buckets(minutes, MachineGrain.FiveMinutes);
        return new PlanSpread(
            minutes.Count,
            P95(fives.Select(bucket => bucket.CpuPercent)),
            MachineFolding.MaxOf(minutes.Select(minute => minute.CpuPercent)),
            P95(fives.Select(bucket => bucket.MemoryPercent)),
            MachineFolding.MaxOf(minutes.Select(minute => minute.MemoryPercent)));
    }

    /// <summary>The key of the UTC hour a moment falls in, the way the store keeps it.</summary>
    public static string HourKey(DateTimeOffset at) => MachineWindows.KeyOf(at, MachineGrain.Hour);

    /// <summary>A day as the partition key: yyyy-MM-dd, UTC.</summary>
    public static string DayOf(DateTimeOffset at) => at.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>
/// The keep held in this process's memory: what the tests use, and what the card reads on a run
/// with no document store, where it says the readings end with the process.
/// </summary>
public sealed class MemoryPlanHistory(string reason) : IPlanHistory
{
    private readonly object _gate = new();
    private readonly List<PlanMinute> _minutes = [];
    private readonly List<PlanHour> _hours = [];

    public Task<PlanHistoryAvailability> AvailabilityAsync(CancellationToken cancellation) =>
        Task.FromResult(new PlanHistoryAvailability(true, reason));

    public Task AppendMinuteAsync(PlanMinute minute, CancellationToken cancellation)
    {
        lock (_gate)
        {
            _minutes.RemoveAll(kept => kept.At == minute.At);
            _minutes.Add(minute);
            _minutes.RemoveAll(kept => kept.At < minute.At.AddDays(-31));
        }
        return Task.CompletedTask;
    }

    public Task AppendHourAsync(PlanHour hour, CancellationToken cancellation)
    {
        lock (_gate)
        {
            _hours.RemoveAll(kept => kept.At == hour.At);
            _hours.Add(hour);
            // The month the store keeps, and no more, so a process that runs on this fake for
            // weeks holds a month like the container's time-to-live does (ADR: The store does the work, rule b).
            _hours.RemoveAll(kept => kept.At < hour.At.AddDays(-31));
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<PlanMinute>> NewestAsync(int count, CancellationToken cancellation)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<PlanMinute>>(_minutes.OrderBy(m => m.At).TakeLast(count).ToList());
        }
    }

    public Task<IReadOnlyList<PlanBucket>> BucketsAsync(DateTimeOffset since, MachineGrain grain, CancellationToken cancellation)
    {
        lock (_gate)
        {
            return Task.FromResult(PlanFolding.Buckets(_minutes.Where(m => m.At >= since).ToList(), grain));
        }
    }

    public Task<PlanSpread> SpreadAsync(DateTimeOffset since, CancellationToken cancellation)
    {
        lock (_gate)
        {
            return Task.FromResult(PlanFolding.Spread(_minutes.Where(m => m.At >= since).ToList()));
        }
    }

    public Task<IReadOnlyList<PlanHour>> HoursAsync(DateTimeOffset since, CancellationToken cancellation)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<PlanHour>>(_hours.Where(h => h.At >= since).OrderBy(h => h.At).ToList());
        }
    }
}
