// The box's card's eight rows (ADR: The box reports itself): each core, the memory and swap, the network, the
// disks and the array, the GPUs, the temperatures, the services the job hunt runs on, and whether the box is
// reporting at all. Each row is read from the newest snapshot this process holds and says it is waiting until
// one lands; every word from the box is scrubbed of addresses and ports before it reaches the wire.
using System.Globalization;
using TheYard.Data;

namespace TheYard.Api.Features.Admin.Cloud;

// #region box-rows
/// <summary>The box's card's rows.</summary>
public static class BoxRows
{
    /// <summary>How long the box may be silent before its reporting row turns red.</summary>
    public static readonly TimeSpan Silent = TimeSpan.FromMinutes(3);

    /// <summary>The services that must run: a stopped one reads red at once.</summary>
    public static readonly IReadOnlyList<string> MustRun = ["claude-remote-control.service", "jdrive.service", "tailscaled.service", "ssh.service", "jobhunt-6am.timer"];

    /// <summary>The service whose last result reads amber when it failed: the job hunt's morning run.</summary>
    public const string MorningRun = "jobhunt-6am.service";

    /// <summary>The eight rows; every one waits in info before the first snapshot.</summary>
    public static IReadOnlyList<CloudRow> All(BoxSnapshot? snapshot, DateTimeOffset? newestKept, DateTimeOffset started, DateTimeOffset now, int keptLastHour)
    {
        var reporting = Reporting(snapshot, newestKept, started, now, keptLastHour);
        if (snapshot is null)
        {
            string[] keys = ["cores", "memory", "network", "disks", "gpus", "temps", "services"];
            string[] questions = ["What is each core doing?", "Is the memory and swap fine?", "Is the network moving?", "Are the disks and file systems healthy?", "What are the GPUs doing?", "How hot is it?", "Are the job-hunt services up?"];
            return [.. keys.Select((key, i) => new CloudRow(key, questions[i], BoxSource.Waiting, [], "info")), reporting];
        }
        return [Cores(snapshot), Memory(snapshot), Network(snapshot), Disks(snapshot), Gpus(snapshot), Temps(snapshot), Services(snapshot, now), reporting];
    }

    /// <summary>Each core's load, clock and temperature.</summary>
    public static CloudRow Cores(BoxSnapshot snapshot)
    {
        var loads = snapshot.Cores.Select(core => core.Load).DefaultIfEmpty(0).ToList();
        string spread = $"{loads.Min().ToString("0", CultureInfo.InvariantCulture)} to {loads.Max().ToString("0", CultureInfo.InvariantCulture)} %";
        return new CloudRow("cores", "What is each core doing?", $"{snapshot.Cores.Count} cores",
            [new($"all {snapshot.Cores.Count} at {spread}; the box idles under 10 % and the local gate takes every core to 100 % for about 8 minutes", null)], "good")
        {
            Table = Table(["Core", "Load", "Clock", "Temp"], snapshot.Cores.Select(core => new[] { $"CPU{core.Index + 1}", BoxWords.Percent(core.Load), $"{(core.Mhz / 1000).ToString("0.00", CultureInfo.InvariantCulture)} GHz", BoxWords.Degrees(core.Temp) })),
        };
    }

    /// <summary>Memory, cache and swap; amber when swap is in use or memory passes its amber line.</summary>
    public static CloudRow Memory(BoxSnapshot snapshot)
    {
        double share = snapshot.Memory.TotalB > 0 ? 100.0 * snapshot.Memory.UsedB / snapshot.Memory.TotalB : 0;
        double swapShare = snapshot.Swap.TotalB > 0 ? 100.0 * snapshot.Swap.UsedB / snapshot.Swap.TotalB : 0;
        var (amber, red) = CloudThresholds.BoxMemory;
        string tone = share >= red ? "alarm" : share >= amber || snapshot.Swap.UsedB > 0 ? "warning" : "good";
        return new CloudRow("memory", "Is the memory and swap fine?", $"{BoxWords.Bytes(snapshot.Memory.UsedB)} of {BoxWords.Bytes(snapshot.Memory.TotalB)}",
            [new($"cache {BoxWords.Bytes(snapshot.Memory.CacheB)}, free {BoxWords.Bytes(snapshot.Memory.FreeB)} · swap {BoxWords.Bytes(snapshot.Swap.UsedB)} of {BoxWords.Bytes(snapshot.Swap.TotalB)} · amber {amber} %, red {red} %, swap in use is amber", null)], tone)
        {
            Table = Table(["Pool", "Used", "Of", "Share"],
            [
                ["Memory", BoxWords.Bytes(snapshot.Memory.UsedB), BoxWords.Bytes(snapshot.Memory.TotalB), BoxWords.Percent(Math.Round(share, 1))],
                ["Cache", BoxWords.Bytes(snapshot.Memory.CacheB), "", BoxWords.Percent(snapshot.Memory.TotalB > 0 ? Math.Round(100.0 * snapshot.Memory.CacheB / snapshot.Memory.TotalB, 1) : null)],
                ["Swap", BoxWords.Bytes(snapshot.Swap.UsedB), BoxWords.Bytes(snapshot.Swap.TotalB), BoxWords.Percent(Math.Round(swapShare, 1))],
            ]),
        };
    }

    /// <summary>Each interface's rates and totals, by name only: no address reaches the card.</summary>
    public static CloudRow Network(BoxSnapshot snapshot)
    {
        var real = snapshot.Nets.Where(net => net.Name != "lo").ToList();
        return new CloudRow("network", "Is the network moving?", $"{BoxWords.Rate(real.Sum(net => net.InBps))} in, {BoxWords.Rate(real.Sum(net => net.OutBps))} out",
            [new($"totals since boot {BoxWords.Bytes(real.Sum(net => (double)net.InTotalB))} received, {BoxWords.Bytes(real.Sum(net => (double)net.OutTotalB))} sent", null)], "good")
        {
            Table = Table(["Interface", "In now", "Out now", "Received", "Sent"], real.Select(net => new[] { BoxWords.Scrub(net.Name), BoxWords.Rate(net.InBps), BoxWords.Rate(net.OutBps), BoxWords.Bytes(net.InTotalB), BoxWords.Bytes(net.OutTotalB) })),
        };
    }

    /// <summary>Each file system and the array: red when the array is not whole or a file system passes its red line.</summary>
    public static CloudRow Disks(BoxSnapshot snapshot)
    {
        var (amber, red) = CloudThresholds.BoxFileSystem;
        double Fill(BoxDisk disk) => disk.SizeB > 0 ? 100.0 * disk.UsedB / disk.SizeB : 0;
        var array = snapshot.Disks.FirstOrDefault(disk => disk.RaidState is not null);
        bool broken = array is not null && (array.MembersActive < array.Members || array.RaidState!.Contains("(F)", StringComparison.Ordinal)
            || array.RaidState.Contains("inactive", StringComparison.OrdinalIgnoreCase) || array.RaidState.Contains("degraded", StringComparison.OrdinalIgnoreCase));
        double fullest = snapshot.Disks.Select(Fill).DefaultIfEmpty(0).Max();
        string tone = broken || fullest >= red ? "alarm" : fullest >= amber ? "warning" : "good";
        string answer = array is null ? "no array" : broken ? $"the array is not whole: {array.MembersActive} of {array.Members} active" : $"{BoxWords.Scrub(array.RaidState)}, {array.MembersActive} of {array.Members} active";
        return new CloudRow("disks", "Are the disks and file systems healthy?", answer,
            [new($"red when the array reports a missing or failed member, or any file system passes {red} %; amber from {amber} %", null)], tone)
        {
            Table = Table(["Mount", "Type", "Size", "Used", "Share", "State"], snapshot.Disks.Select(disk => new[] { $"{disk.Mount} ({BoxWords.Scrub(disk.Device)})", disk.Fstype, BoxWords.Bytes(disk.SizeB), BoxWords.Bytes(disk.UsedB), BoxWords.Percent(Math.Round(Fill(disk), 1)), disk.RaidState is null ? "" : $"{BoxWords.Scrub(disk.RaidState)}, {disk.MembersActive} of {disk.Members} active" })),
        };
    }

    /// <summary>Each GPU: amber at its memory's amber line or ten degrees under its slowdown mark, red at either red.</summary>
    public static CloudRow Gpus(BoxSnapshot snapshot)
    {
        var (amber, red) = CloudThresholds.BoxGpuMemory;
        string ToneOf(BoxGpu gpu)
        {
            double memory = gpu.MemTotalB > 0 ? 100.0 * gpu.MemUsedB / gpu.MemTotalB : 0;
            return memory >= red || gpu.SlowdownTemp is { } hot && gpu.Temp >= hot ? "alarm"
                : memory >= amber || gpu.SlowdownTemp is { } warm && gpu.Temp >= warm - CloudThresholds.BoxTemperatureUnder ? "warning" : "good";
        }
        string tone = Worst(snapshot.Gpus.Select(ToneOf));
        string answer = snapshot.Gpus.Count == 0 ? "no GPU read" : $"{snapshot.Gpus.Count} x {snapshot.Gpus[0].Name.Replace("NVIDIA GeForce ", "", StringComparison.Ordinal)}";
        return new CloudRow("gpus", "What are the GPUs doing?", answer,
            [new($"amber at {amber} % of a GPU's memory or {CloudThresholds.BoxTemperatureUnder} °C under its own slowdown mark, red at {red} % or the mark", null)], tone)
        {
            Table = Table(["GPU", "Load", "Memory", "Temp", "Power", "State"], snapshot.Gpus.Select(gpu => new[]
            {
                $"GPU {gpu.Index} {gpu.Name}", BoxWords.Percent(gpu.Load), $"{BoxWords.Bytes(gpu.MemUsedB)} of {BoxWords.Bytes(gpu.MemTotalB)}",
                gpu.SlowdownTemp is { } mark ? $"{BoxWords.Degrees(gpu.Temp)}, slowdown {BoxWords.Degrees(mark)}" : BoxWords.Degrees(gpu.Temp),
                gpu.PowerW is { } watts ? $"{watts.ToString("0", CultureInfo.InvariantCulture)} W of {gpu.PowerLimitW?.ToString("0", CultureInfo.InvariantCulture) ?? "?"} W" : "not read", gpu.Pstate,
            })),
        };
    }

    /// <summary>Every sensor against its own marks: amber ten degrees under its high mark, red at it; no mark, no tone.</summary>
    public static CloudRow Temps(BoxSnapshot snapshot)
    {
        static string ToneOf(BoxTemp temp) =>
            temp.High is not { } high ? "good" : temp.Now >= high ? "alarm" : temp.Now >= high - CloudThresholds.BoxTemperatureUnder ? "warning" : "good";
        var package = BoxSource.Package(snapshot);
        return new CloudRow("temps", "How hot is it?", package is null ? "no sensor read" : $"{BoxWords.Degrees(package.Now)} {BoxWords.Scrub(package.Sensor)}",
            [new($"amber at {CloudThresholds.BoxTemperatureUnder} °C under each sensor's own high mark, red at the mark; the marks come from the sensors, never typed here", null)], Worst(snapshot.Temps.Select(ToneOf)))
        {
            Table = Table(["Sensor", "Now", "Marks"], snapshot.Temps.Select(temp => new[]
            {
                BoxWords.Scrub(temp.Sensor), BoxWords.Degrees(temp.Now),
                string.Join(", ", new[] { temp.High is { } h ? $"high {BoxWords.Degrees(h)}" : null, temp.Critical is { } c ? $"critical {BoxWords.Degrees(c)}" : null }.OfType<string>()),
            })),
        };
    }

    /// <summary>The services: red when one that must run is not active, amber when the morning run's last result failed.</summary>
    public static CloudRow Services(BoxSnapshot snapshot, DateTimeOffset now)
    {
        bool Active(BoxService service) => service.ActiveState == "active";
        var stopped = MustRun.Where(unit => snapshot.Services.FirstOrDefault(service => service.Unit == unit) is not { } found || !Active(found)).ToList();
        bool morningFailed = snapshot.Services.FirstOrDefault(service => service.Unit == MorningRun)?.Note is { } note && note.Contains("last result", StringComparison.Ordinal) && !note.Contains("success", StringComparison.Ordinal);
        string tone = stopped.Count > 0 ? "alarm" : morningFailed ? "warning" : "good";
        int running = snapshot.Services.Count(Active);
        return new CloudRow("services", "Are the job-hunt services up?", $"{running} of {snapshot.Services.Count} active",
            [new(stopped.Count > 0 ? $"not running: {string.Join(", ", stopped)}" : $"red when {string.Join(", ", MustRun)} is not running; amber when the morning run's last result failed", null)], tone)
        {
            Table = Table(["Service", "State", "Up for", "Note"], snapshot.Services.Select(service => new[]
            {
                $"{service.Unit} ({service.Scope})", $"{service.ActiveState}, {service.SubState}",
                service.Since is { } since && Active(service) ? BoxWords.Span(now - since) : "", BoxWords.Scrub(service.Note),
            })),
        };
    }

    /// <summary>
    /// Whether the box is reporting: red after three minutes with no snapshot, measured from the later of the newest
    /// snapshot (held or kept) and this process's start, so a roll never reads as the box being down.
    /// </summary>
    public static CloudRow Reporting(BoxSnapshot? snapshot, DateTimeOffset? newestKept, DateTimeOffset started, DateTimeOffset now, int keptLastHour)
    {
        DateTimeOffset? newest = new[] { snapshot?.At, newestKept }.Where(at => at is not null).Max();
        var from = newest is { } at && at > started ? at : started;
        var silence = now - from;
        string count = $"{keptLastHour} snapshots kept in the last hour; the agent adds nothing to the bill, one small write a minute on the free tier";
        if (newest is null)
        {
            return silence > Silent
                ? new CloudRow("reporting", "Is the box reporting?", $"no: no snapshot in the {BoxWords.Span(silence)} since this site started", [new(count, null)], "alarm")
                : new CloudRow("reporting", "Is the box reporting?", BoxSource.Waiting, [new(count, null)], "info");
        }
        return silence > Silent
            ? new CloudRow("reporting", "Is the box reporting?", $"no: silent for {BoxWords.Span(silence)}", [new(count, null)], "alarm")
            : new CloudRow("reporting", "Is the box reporting?", $"yes, the newest snapshot {BoxWords.Span(now - newest.Value)} ago", [new(count, null)], "good");
    }

    /// <summary>The worst of a set of tones; good when there are none.</summary>
    public static string Worst(IEnumerable<string> tones) =>
        tones.OrderByDescending(tone => tone switch { "alarm" => 3, "warning" => 2, "info" => 1, _ => 0 }).FirstOrDefault() ?? "good";

    /// <summary>A table from rows of words.</summary>
    private static CloudTable Table(IReadOnlyList<string> columns, IEnumerable<IEnumerable<string>> rows) =>
        new(columns, [.. rows.Select(row => (IReadOnlyList<CloudLink>)[.. row.Select(cell => new CloudLink(cell, null))])]);
}
// #endregion box-rows
