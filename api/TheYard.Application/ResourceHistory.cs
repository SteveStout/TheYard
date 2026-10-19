// Every Azure resource under Cloud infrastructure past the plan, kept the same way (ADR: One frame
// for every resource): one minute of named readings a minute, one hour of slower facts an hour, a
// month of each, read back in buckets. A resource names its own readings; the keep, the folding
// and the windows are written once here.

namespace TheYard.Application;

// #region resource-minute
/// <summary>One minute of one resource's readings, by name; a reading the source did not report is absent, never zero.</summary>
/// <param name="At">The start of the minute, UTC.</param>
/// <param name="Values">The readings, by the names the resource's card defines.</param>
/// <param name="Version">The build the recording site was running, so a deploy shows as the minute it changed.</param>
public sealed record ResourceMinute(DateTimeOffset At, IReadOnlyDictionary<string, double?> Values, string Version);

/// <summary>One point of a windowed chart: each reading's mean over the minutes in the bucket, and its peak.</summary>
/// <param name="At">The start of the bucket, UTC.</param>
/// <param name="Minutes">How many kept minutes it averages.</param>
/// <param name="Means">Each reading's mean, by name; absent when no minute carried it.</param>
/// <param name="Peaks">Each reading's highest minute, by name.</param>
/// <param name="Sums">Each reading summed over the bucket, for counts.</param>
/// <param name="Version">The newest build seen in the bucket.</param>
public sealed record ResourceBucket(DateTimeOffset At, int Minutes, IReadOnlyDictionary<string, double?> Means, IReadOnlyDictionary<string, double?> Peaks, IReadOnlyDictionary<string, double?> Sums, string? Version);

/// <summary>An hour's slower read of one resource: what it is, facts that change rarely, and its Activity Log.</summary>
/// <param name="At">The start of the hour, UTC.</param>
/// <param name="ResourceId">The resource's id, which the card links to.</param>
/// <param name="TenantId">The tenant, for the portal link.</param>
/// <param name="Facts">Named facts the card prints: a size, a tier, a list of names.</param>
/// <param name="Events">The Activity Log entries in the hour.</param>
public sealed record ResourceHour(DateTimeOffset At, string ResourceId, string? TenantId, IReadOnlyDictionary<string, string?> Facts, IReadOnlyList<PlanEvent> Events);
// #endregion resource-minute

// #region resource-port
/// <summary>Port: where one resource's minutes and hours are kept.</summary>
public interface IResourceHistory
{
    Task<PlanHistoryAvailability> AvailabilityAsync(CancellationToken cancellation);

    Task AppendMinuteAsync(ResourceMinute minute, CancellationToken cancellation);

    Task AppendHourAsync(ResourceHour hour, CancellationToken cancellation);

    /// <summary>The newest minutes kept, newest last.</summary>
    Task<IReadOnlyList<ResourceMinute>> NewestAsync(int count, CancellationToken cancellation);

    /// <summary>The window's buckets for the named readings, oldest first.</summary>
    Task<IReadOnlyList<ResourceBucket>> BucketsAsync(DateTimeOffset since, MachineGrain grain, IReadOnlyList<string> readings, CancellationToken cancellation);

    /// <summary>Every hour kept since <paramref name="since"/>, oldest first.</summary>
    Task<IReadOnlyList<ResourceHour>> HoursAsync(DateTimeOffset since, CancellationToken cancellation);
}
// #endregion resource-port

/// <summary>The arithmetic every resource's keep shares, testable with no store.</summary>
public static class ResourceFolding
{
    /// <summary>Minutes into buckets, oldest first, the way a store with no grouped query would.</summary>
    public static IReadOnlyList<ResourceBucket> Buckets(IEnumerable<ResourceMinute> minutes, MachineGrain grain, IReadOnlyList<string> readings) =>
        minutes
            .GroupBy(minute => MachineWindows.BucketOf(minute.At, grain))
            .OrderBy(group => group.Key)
            .Select(group => new ResourceBucket(
                group.Key,
                group.Count(),
                readings.ToDictionary(name => name, name => MachineFolding.MeanOf(group.Select(m => m.Values.GetValueOrDefault(name)))),
                readings.ToDictionary(name => name, name => MachineFolding.MaxOf(group.Select(m => m.Values.GetValueOrDefault(name)))),
                readings.ToDictionary(name => name, name => Sum(group.Select(m => m.Values.GetValueOrDefault(name)))),
                group.OrderBy(m => m.At).Last().Version))
            .ToList();

    /// <summary>The 95th of the five-minute means and the peak of one reading over a window's minutes.</summary>
    public static (double? P95, double? Peak) Spread(IReadOnlyList<ResourceBucket> fiveMinuteBuckets, string reading) =>
        (PlanFolding.P95(fiveMinuteBuckets.Select(b => b.Means.GetValueOrDefault(reading))),
         MachineFolding.MaxOf(fiveMinuteBuckets.Select(b => b.Peaks.GetValueOrDefault(reading))));

    private static double? Sum(IEnumerable<double?> values)
    {
        var read = values.Where(v => v is not null).Select(v => v!.Value).ToList();
        return read.Count == 0 ? null : Math.Round(read.Sum(), 2);
    }
}

/// <summary>A resource's keep in this process's memory: the tests, and a run with no document store.</summary>
public sealed class MemoryResourceHistory(string reason) : IResourceHistory
{
    private readonly object _gate = new();
    private readonly List<ResourceMinute> _minutes = [];
    private readonly List<ResourceHour> _hours = [];

    public Task<PlanHistoryAvailability> AvailabilityAsync(CancellationToken cancellation) => Task.FromResult(new PlanHistoryAvailability(true, reason));

    public Task AppendMinuteAsync(ResourceMinute minute, CancellationToken cancellation)
    {
        lock (_gate)
        {
            _minutes.RemoveAll(kept => kept.At == minute.At);
            _minutes.Add(minute);
            _minutes.RemoveAll(kept => kept.At < minute.At.AddDays(-31));
        }
        return Task.CompletedTask;
    }

    public Task AppendHourAsync(ResourceHour hour, CancellationToken cancellation)
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

    public Task<IReadOnlyList<ResourceMinute>> NewestAsync(int count, CancellationToken cancellation)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ResourceMinute>>(_minutes.OrderBy(m => m.At).TakeLast(count).ToList());
        }
    }

    public Task<IReadOnlyList<ResourceBucket>> BucketsAsync(DateTimeOffset since, MachineGrain grain, IReadOnlyList<string> readings, CancellationToken cancellation)
    {
        lock (_gate)
        {
            return Task.FromResult(ResourceFolding.Buckets(_minutes.Where(m => m.At >= since).ToList(), grain, readings));
        }
    }

    public Task<IReadOnlyList<ResourceHour>> HoursAsync(DateTimeOffset since, CancellationToken cancellation)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<ResourceHour>>(_hours.Where(h => h.At >= since).OrderBy(h => h.At).ToList());
        }
    }
}
