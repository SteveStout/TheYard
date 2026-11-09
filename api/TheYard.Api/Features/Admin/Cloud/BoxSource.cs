// The box's card's source (ADR: The box reports itself): the box posts, this process holds, and the recorder
// keeps one minute a snapshot and the snapshot's slow facts as its hour, both in the plan container under the
// box- kinds. Nothing here asks the box anything: a card that waits for a push reads what the push left.
using System.Globalization;
using TheYard.Application.Features.Admin.Cloud;
using TheYard.Data;

namespace TheYard.Api.Features.Admin.Cloud;

// #region box-source
/// <summary>What the box's card reads: the snapshots and the live ring this process holds, when it started, and its clock.</summary>
/// <param name="snapshots">The newest snapshot and the ones not yet kept.</param>
/// <param name="live">The live ring.</param>
/// <param name="start">When this process started, so a roll's silence is never read as the box's.</param>
/// <param name="clock">This process's clock.</param>
public sealed class BoxSource(IBoxSnapshots snapshots, IBoxLive live, HostStart start, TimeProvider clock)
{
    /// <summary>The readings a kept minute holds, each a share or a count the chart and the sparks read.</summary>
    public static IReadOnlyList<string> Readings { get; } = ["cpu", "memory", "swap", "gpu0", "gpu1", "disk_root", "temp_cpu", "net_in_bps", "net_out_bps", "load1"];

    /// <summary>The words every row and line says before the first snapshot lands.</summary>
    public const string Waiting = "waiting for the box's first snapshot";

    /// <summary>The newest snapshot this process holds, or null before the first since it started.</summary>
    public BoxSnapshot? Newest => snapshots.Newest;

    /// <summary>When the newest live sample was read, or null.</summary>
    public DateTimeOffset? LiveAt => live.NewestAt;

    /// <summary>When this process started.</summary>
    public DateTimeOffset Started => start.At;

    /// <summary>This process's clock now.</summary>
    public DateTimeOffset Now => clock.GetUtcNow();

    /// <summary>One kept minute for each snapshot posted since the last take, at the snapshot's own time.</summary>
    public Task<IReadOnlyList<ResourceMinute>> MinutesAsync(DateTimeOffset from, DateTimeOffset to, string version, CancellationToken cancellation) =>
        Task.FromResult<IReadOnlyList<ResourceMinute>>([.. snapshots.TakeUnrecorded().Select(snapshot => new ResourceMinute(snapshot.At, Values(snapshot), version))]);

    /// <summary>The newest snapshot's slow facts as the hour: what the box is, and the words before the first.</summary>
    public Task<ResourceHour> HourAsync(DateTimeOffset hour, DateTimeOffset since, CancellationToken cancellation)
    {
        var facts = snapshots.Newest is { } snapshot ? Facts(snapshot) : new Dictionary<string, string?> { ["box"] = Waiting };
        return Task.FromResult(new ResourceHour(hour, "", null, facts, []));
    }

    /// <summary>A snapshot's readings: shares of their limits, the root file system's fill, the package temperature, the rates and the load.</summary>
    public static IReadOnlyDictionary<string, double?> Values(BoxSnapshot snapshot)
    {
        double? Share(long used, long total) => total > 0 ? Math.Round(100.0 * used / total, 2) : null;
        var root = snapshot.Disks.FirstOrDefault(disk => disk.Mount == "/");
        var real = snapshot.Nets.Where(net => net.Name != "lo").ToList();
        return new Dictionary<string, double?>(StringComparer.Ordinal)
        {
            ["cpu"] = snapshot.Cores.Count == 0 ? null : Math.Round(snapshot.Cores.Average(core => core.Load), 2),
            ["memory"] = Share(snapshot.Memory.UsedB, snapshot.Memory.TotalB),
            ["swap"] = Share(snapshot.Swap.UsedB, snapshot.Swap.TotalB),
            ["gpu0"] = snapshot.Gpus.FirstOrDefault(gpu => gpu.Index == 0)?.Load,
            ["gpu1"] = snapshot.Gpus.FirstOrDefault(gpu => gpu.Index == 1)?.Load,
            ["disk_root"] = root is null ? null : Share(root.UsedB, root.SizeB),
            ["temp_cpu"] = Package(snapshot)?.Now,
            ["net_in_bps"] = Math.Round(real.Sum(net => net.InBps), 0),
            ["net_out_bps"] = Math.Round(real.Sum(net => net.OutBps), 0),
            ["load1"] = snapshot.Loadavg.Count > 0 ? snapshot.Loadavg[0] : null,
        };
    }

    /// <summary>The processor's package sensor, or the hottest sensor with a high mark when none is named so.</summary>
    public static BoxTemp? Package(BoxSnapshot snapshot) =>
        snapshot.Temps.FirstOrDefault(temp => temp.Sensor.Contains("Package", StringComparison.OrdinalIgnoreCase))
        ?? snapshot.Temps.Where(temp => temp.High is not null).OrderByDescending(temp => temp.Now).FirstOrDefault();

    /// <summary>The slow facts an hour keeps: the box, the processor, the memory, the GPUs, the array and the agent.</summary>
    public static Dictionary<string, string?> Facts(BoxSnapshot snapshot)
    {
        var array = snapshot.Disks.FirstOrDefault(disk => disk.RaidState is not null);
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["box"] = $"{snapshot.Distro}, kernel {snapshot.Kernel}",
            ["host"] = snapshot.Host,
            ["cores"] = snapshot.Cores.Count.ToString(CultureInfo.InvariantCulture),
            ["ram"] = BoxWords.Bytes(snapshot.Memory.TotalB),
            ["gpus"] = string.Join(", ", snapshot.Gpus.Select(gpu => gpu.Name)),
            ["raid"] = array is null ? "no array" : $"{array.RaidState}, {array.MembersActive} of {array.Members} active",
            ["services"] = string.Join(", ", snapshot.Services.Select(service => $"{service.Unit} {service.ActiveState}")),
            ["agent"] = snapshot.AgentVersion,
        };
    }
}
// #endregion box-source
