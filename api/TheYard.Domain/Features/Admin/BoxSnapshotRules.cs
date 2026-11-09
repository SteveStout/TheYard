// What the box may say about itself (ADR: The box reports itself). A snapshot and a live sample reach the
// site from the box's agent with the operator's key; the rule of what either may hold is here, so the route
// and anything that keeps them cannot disagree, and a bad body cannot grow a document. Pure: the reading and
// the clock go in, the one sentence that is wrong comes out; nothing here reads the clock, the disk or the network.
using System.Text.RegularExpressions;
using TheYard.Data;

namespace TheYard.Domain.Features.Admin;

// #region box-snapshot-rules
/// <summary>The rules a box snapshot and a live sample pass before either is kept.</summary>
public static partial class BoxSnapshotRules
{
    /// <summary>How far ahead of this process's clock a reading may say it was read.</summary>
    public static readonly TimeSpan Skew = TimeSpan.FromMinutes(5);

    /// <summary>How far back: an agent that could not reach the site posts its backlog after a roll.</summary>
    public static readonly TimeSpan Oldest = TimeSpan.FromMinutes(60);

    /// <summary>The most of each list a reading may carry.</summary>
    public const int MostCores = 64, MostGpus = 16, MostNets = 32, MostDisks = 32, MostTemps = 64, MostServices = 32;

    /// <summary>The one sentence that is wrong with this snapshot, or null when it may be kept.</summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="now">This process's clock.</param>
    public static string? Validate(BoxSnapshot snapshot, DateTimeOffset now) =>
        string.IsNullOrEmpty(snapshot.Host) || snapshot.Host.Length > 40 || !HostWords().IsMatch(snapshot.Host) ? "host is 1 to 40 letters, digits or dashes."
        : When(snapshot.At, now) is { } when ? when
        : snapshot.Loadavg is null || snapshot.Cores is null || snapshot.Nets is null || snapshot.Disks is null || snapshot.Gpus is null || snapshot.Temps is null || snapshot.Services is null || snapshot.Memory is null || snapshot.Swap is null
            ? "every list and part is present, empty when there is none."
        : Lists(snapshot.Cores.Count, snapshot.Gpus.Count, snapshot.Nets.Count) ?? Rest(snapshot);

    /// <summary>The one sentence that is wrong with this live sample, or null when it may be kept.</summary>
    /// <param name="sample">The sample.</param>
    /// <param name="now">This process's clock.</param>
    public static string? Validate(BoxLiveSample sample, DateTimeOffset now) =>
        When(sample.At, now) is { } when ? when
        : sample.Cores is null || sample.Nets is null || sample.Gpus is null ? "every list is present, empty when there is none."
        : Lists(sample.Cores.Count, sample.Gpus.Count, sample.Nets.Count)
            ?? (sample.Cores.Any(load => !Percent(load)) || sample.Gpus.Any(gpu => !Percent(gpu.Load)) ? "every load is 0 to 100."
            : sample.MemoryUsedB < 0 || sample.MemoryTotalB < 0 || sample.SwapUsedB < 0 || sample.SwapTotalB < 0 || sample.Nets.Any(net => net.InBps < 0 || net.OutBps < 0) ? "every byte count and rate is zero or more."
            : null);

    /// <summary>The reading's time against the clock: not ahead by more than the skew, not older than the backlog.</summary>
    private static string? When(DateTimeOffset at, DateTimeOffset now) =>
        at > now + Skew ? "at is more than five minutes ahead of this site's clock."
        : at < now - Oldest ? "at is more than an hour ago."
        : null;

    /// <summary>The three lists a live sample and a snapshot share, against their limits.</summary>
    private static string? Lists(int cores, int gpus, int nets) =>
        cores > MostCores ? $"cores holds at most {MostCores}."
        : gpus > MostGpus ? $"gpus holds at most {MostGpus}."
        : nets > MostNets ? $"nets holds at most {MostNets}."
        : null;

    /// <summary>The snapshot's other lists and every number in it.</summary>
    private static string? Rest(BoxSnapshot snapshot) =>
        snapshot.Disks.Count > MostDisks ? $"disks holds at most {MostDisks}."
        : snapshot.Temps.Count > MostTemps ? $"temps holds at most {MostTemps}."
        : snapshot.Services.Count > MostServices ? $"services holds at most {MostServices}."
        : snapshot.Cores.Any(core => !Percent(core.Load)) || snapshot.Gpus.Any(gpu => !Percent(gpu.Load)) || !Percent(snapshot.AgentCpuPercent) ? "every load is 0 to 100."
        : snapshot.UptimeS < 0 || snapshot.Memory.TotalB < 0 || snapshot.Memory.UsedB < 0 || snapshot.Memory.CacheB < 0 || snapshot.Memory.FreeB < 0
            || snapshot.Swap.TotalB < 0 || snapshot.Swap.UsedB < 0
            || snapshot.Nets.Any(net => net.InBps < 0 || net.OutBps < 0 || net.InTotalB < 0 || net.OutTotalB < 0)
            || snapshot.Disks.Any(disk => disk.SizeB < 0 || disk.UsedB < 0)
            || snapshot.Gpus.Any(gpu => gpu.MemUsedB < 0 || gpu.MemTotalB < 0)
            ? "every byte count and rate is zero or more."
        : null;

    /// <summary>True for a share from 0 to 100.</summary>
    private static bool Percent(double value) => value is >= 0 and <= 100;

    [GeneratedRegex(@"^[A-Za-z0-9-]+$")]
    private static partial Regex HostWords();
}
// #endregion box-snapshot-rules
