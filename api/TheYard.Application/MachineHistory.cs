using System.Globalization;

namespace TheYard.Application;

// What the machines were doing, kept (ADR: What the machines are doing). The
// machines card draws the hour this process remembers, and a roll empties it.
// This port is where a minute goes to outlive the process: one reading a
// minute from each site, kept for a month, read back in buckets sized to the
// window a chart is asked for.

// #region minute
/// <summary>
/// One minute of one site, folded from what the process already measures: the
/// four samples the sampler took that minute, the relational store's own
/// reading of itself for that minute, what the document store charged in it,
/// and the traffic the request ring holds for it. A figure that was not read is null and is kept as absent, never as a
/// zero: a chart breaks its line over a gap, and an average over a month must
/// not be pulled toward a number nobody measured.
/// </summary>
public sealed record MachineMinute(
    DateTimeOffset At,
    string Site,
    double MemoryLimitMb,
    double WorkingSetMb,
    double WorkingSetMaxMb,
    double ManagedMb,
    double? CpuPercent,
    double? CpuMaxPercent,
    double? SqlCpuPercent,
    double? SqlMemoryPercent,
    double? SqlDataIoPercent,
    double RequestUnits,
    int Operations,
    int Requests = 0,
    double? P50Ms = null,
    double? P95Ms = null,
    int ServerErrors = 0,
    int ClientErrors = 0);

/// <summary>One point on a windowed chart: every minute in the bucket, averaged, with the peaks kept as peaks.</summary>
public sealed record MachineBucket(
    DateTimeOffset At,
    int Minutes,
    double MemoryLimitMb,
    double WorkingSetMb,
    double WorkingSetMaxMb,
    double ManagedMb,
    double? CpuPercent,
    double? CpuMaxPercent,
    double? SqlCpuPercent,
    double? SqlMemoryPercent,
    double? SqlDataIoPercent,
    double RequestUnits,
    int Operations,
    int Requests = 0,
    double? P50Ms = null,
    double? P95Ms = null,
    int ServerErrors = 0,
    int ClientErrors = 0);

/// <summary>Whether minutes can be kept right now, and if not, why, in words the card can show.</summary>
public sealed record MachineHistoryAvailability(bool Available, string Reason);
// #endregion minute

// #region windows
/// <summary>How wide a bucket is. The store keeps a key for each on every minute, so a window is one grouped query.</summary>
public enum MachineGrain
{
    FiveMinutes,
    Hour,
    FourHours,
}

/// <summary>
/// The windows the machine charts offer. The hour is the process's own memory
/// and is not here; these three are read from the store, each in the bucket
/// that puts a few hundred points on a chart: 288 five-minute points in a day,
/// 168 hours in a week, 180 four-hour points in a month.
/// </summary>
public static class MachineWindows
{
    public const string Hour = "1h";

    public static readonly IReadOnlyList<string> Names = [Hour, "24h", "7d", "30d"];

    /// <summary>The kept windows by name, or null for the hour and for anything else, which the endpoint reads as the hour.</summary>
    public static (TimeSpan Length, MachineGrain Grain, string Name)? Parse(string? window) => window switch
    {
        "24h" => (TimeSpan.FromHours(24), MachineGrain.FiveMinutes, "24h"),
        "7d" => (TimeSpan.FromDays(7), MachineGrain.Hour, "7d"),
        "30d" => (TimeSpan.FromDays(30), MachineGrain.FourHours, "30d"),
        _ => null,
    };

    /// <summary>The start of the bucket <paramref name="at"/> falls in, in UTC.</summary>
    public static DateTimeOffset BucketOf(DateTimeOffset at, MachineGrain grain)
    {
        var utc = at.ToUniversalTime();
        return grain switch
        {
            MachineGrain.FiveMinutes => new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute - (utc.Minute % 5), 0, TimeSpan.Zero),
            MachineGrain.Hour => new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero),
            _ => new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour - (utc.Hour % 4), 0, 0, TimeSpan.Zero),
        };
    }

    /// <summary>A bucket's key as the store keeps it: sortable text, the same in every culture.</summary>
    public static string KeyOf(DateTimeOffset at, MachineGrain grain) =>
        BucketOf(at, grain).ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

    /// <summary>The UTC day as text, which is what the container is partitioned on.</summary>
    public static string DayOf(DateTimeOffset at) =>
        at.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
// #endregion windows

// #region folding
/// <summary>
/// The arithmetic, on its own so it is testable without a store or a clock:
/// samples into a minute, and minutes into buckets. The second is what a store
/// with no grouped query would do, and what the tests hold the document
/// store's grouped query to.
/// </summary>
public static class MachineFolding
{
    /// <summary>The minute <paramref name="at"/> falls in, in UTC.</summary>
    public static DateTimeOffset MinuteOf(DateTimeOffset at)
    {
        var utc = at.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero);
    }

    /// <summary>The mean of the readings that exist, or null when none does.</summary>
    public static double? MeanOf(IEnumerable<double?> values)
    {
        var read = values.Where(value => value is not null).Select(value => value!.Value).ToList();
        return read.Count == 0 ? null : Math.Round(read.Average(), 2);
    }

    /// <summary>The highest of the readings that exist, or null when none does.</summary>
    public static double? MaxOf(IEnumerable<double?> values)
    {
        var read = values.Where(value => value is not null).Select(value => value!.Value).ToList();
        return read.Count == 0 ? null : Math.Round(read.Max(), 2);
    }

    /// <summary>Minutes into buckets, oldest first. Averages are of the minutes that carry the figure; peaks stay peaks; what was charged adds up.</summary>
    public static IReadOnlyList<MachineBucket> Buckets(IEnumerable<MachineMinute> minutes, MachineGrain grain) =>
        minutes
            .GroupBy(minute => MachineWindows.BucketOf(minute.At, grain))
            .OrderBy(group => group.Key)
            .Select(group => new MachineBucket(
                group.Key,
                group.Count(),
                Math.Round(group.Max(minute => minute.MemoryLimitMb), 1),
                Math.Round(group.Average(minute => minute.WorkingSetMb), 1),
                Math.Round(group.Max(minute => minute.WorkingSetMaxMb), 1),
                Math.Round(group.Average(minute => minute.ManagedMb), 1),
                MeanOf(group.Select(minute => minute.CpuPercent)),
                MaxOf(group.Select(minute => minute.CpuMaxPercent)),
                MeanOf(group.Select(minute => minute.SqlCpuPercent)),
                MeanOf(group.Select(minute => minute.SqlMemoryPercent)),
                MeanOf(group.Select(minute => minute.SqlDataIoPercent)),
                Math.Round(group.Sum(minute => minute.RequestUnits), 2),
                group.Sum(minute => minute.Operations),
                group.Sum(minute => minute.Requests),
                // A median of medians is not a median, and the chart does not
                // call it one: a bucket's typical answer is the mean of its
                // minutes' medians, and its slow answer is the worst minute's
                // ninety-fifth, which is the one somebody felt.
                MeanOf(group.Select(minute => minute.P50Ms)),
                MaxOf(group.Select(minute => minute.P95Ms)),
                group.Sum(minute => minute.ServerErrors),
                group.Sum(minute => minute.ClientErrors)))
            .ToList();
}
// #endregion folding

// #region port
/// <summary>Port: where a site's minutes go and come back from. One write a minute, never from a request thread.</summary>
public interface IMachineHistory
{
    Task<MachineHistoryAvailability> AvailabilityAsync(CancellationToken cancellation);

    Task AppendAsync(MachineMinute minute, CancellationToken cancellation);

    /// <summary>One site's buckets since <paramref name="since"/>, oldest first.</summary>
    Task<IReadOnlyList<MachineBucket>> QueryAsync(string site, DateTimeOffset since, MachineGrain grain, CancellationToken cancellation);
}

/// <summary>The port wired to nothing: a process with no document store keeps the hour it remembers and says so.</summary>
public sealed class NullMachineHistory(string reason) : IMachineHistory
{
    public static readonly NullMachineHistory Instance = new("no document store is configured, so only the hour this process remembers is kept");

    public Task<MachineHistoryAvailability> AvailabilityAsync(CancellationToken cancellation) =>
        Task.FromResult(new MachineHistoryAvailability(false, reason));

    public Task AppendAsync(MachineMinute minute, CancellationToken cancellation) => Task.CompletedTask;

    public Task<IReadOnlyList<MachineBucket>> QueryAsync(string site, DateTimeOffset since, MachineGrain grain, CancellationToken cancellation) =>
        Task.FromResult<IReadOnlyList<MachineBucket>>([]);
}
// #endregion port
