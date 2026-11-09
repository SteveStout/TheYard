// The box's card (ADR: The box reports itself): WickedAwsome, the Linux box that supports the job hunt, in the
// one frame every resource gets. The processor and the memory on the dials, an hour of the processor, the memory
// and the first GPU on the sparks, the processor and memory a day at the day's 95th, the eight rows, and the
// readout. Every figure is the box's own snapshot; before the first lands, every row and line says it waits.
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Api.Features.Admin.Cloud;

// #region box-card
/// <summary>The box's card.</summary>
public static class BoxCard
{
    /// <summary>The one portal page the box has: the Tailscale admin console, the owner's own sign-in.</summary>
    public const string Console = "https://login.tailscale.com/admin/machines";

    /// <summary>The card's title, and the rail row's name.</summary>
    public const string Title = "WickedAwsome";

    private static readonly IReadOnlyDictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["cores"] = "/proc/stat and /proc/cpuinfo, 1 s apart, in the box's snapshot",
        ["memory"] = "/proc/meminfo, in the box's snapshot",
        ["network"] = "/proc/net/dev, 1 s apart, in the box's snapshot",
        ["disks"] = "/proc/mdstat and df, and mdadm --detail when it answers without sudo, in the box's snapshot",
        ["gpus"] = "nvidia-smi --query-gpu, in the box's snapshot",
        ["temps"] = "/sys/class/hwmon, /sys/class/thermal and nvidia-smi, in the box's snapshot",
        ["services"] = "systemctl --user show and systemctl show, in the box's snapshot",
        ["reporting"] = "this process: the newest snapshot held or kept, against this process's start",
        ["Box"] = "the box's snapshot",
        ["Uptime"] = "/proc/uptime, in the box's snapshot",
        ["Load"] = "/proc/loadavg, in the box's snapshot",
        ["Live sample"] = CardFrame.ThisProcess,
        ["Snapshot"] = "the plan container, box-minute, thirty-one days",
        ["Silent for"] = CardFrame.ThisProcess,
        ["Agent"] = "the box's snapshot",
        ["Cost"] = "ADR: The box reports itself",
    };

    /// <summary>Draws the card.</summary>
    /// <param name="inputs">The kept minutes and hours.</param>
    /// <param name="box">What this process holds of the box.</param>
    public static CloudReport View(ResourceCardInputs inputs, BoxSource box)
    {
        var snapshot = box.Newest;
        DateTimeOffset? newestKept = inputs.LastHour.Count == 0 ? null : inputs.LastHour[^1].At;
        var rows = BoxRows.All(snapshot, newestKept, box.Started, inputs.Now, inputs.LastHour.Count);
        var dials = new[]
        {
            ResourceView.Dial(inputs, "cpu", "Processor, all cores", 100, CloudThresholds.BoxCpu, value => snapshot is null ? BoxSource.Waiting : $"load {snapshot.Loadavg.FirstOrDefault():0.00}", percentReading: true),
            ResourceView.Dial(inputs, "memory", "Memory", 100, CloudThresholds.BoxMemory, value => snapshot is null ? BoxSource.Waiting : $"{BoxWords.Bytes(snapshot.Memory.UsedB)} of {BoxWords.Bytes(snapshot.Memory.TotalB)}", percentReading: true),
        };
        var chart = ResourceView.Chart(inputs, "Processor and memory, each day", ("cpu", "cpu", 100, true), ("memory", "memory", 100, true), CloudThresholds.BoxCpu.Red,
            $"cpu teal · memory gold · each point the bucket's mean, its peak beside it · amber {CloudThresholds.BoxCpu.Amber} % · red {CloudThresholds.BoxCpu.Red} %",
            firstLines: CloudThresholds.BoxCpu, secondLines: CloudThresholds.BoxMemory);
        var report = new CloudReport(
            inputs.Window, PlanView.Windows, inputs.Availability.Available && (inputs.LastHour.Count > 0 || inputs.Hours.Count > 0 || snapshot is not null),
            snapshot is null ? BoxSource.Waiting : null,
            inputs.Now.ToUnixTimeMilliseconds(), ResourceView.Age(inputs),
            Title,
            snapshot is null ? BoxSource.Waiting : Spec(snapshot),
            new CloudLink(Title, Console),
            dials,
            [ResourceView.Spark(inputs, "cpu", "cpu, 60 min", 100, true), ResourceView.Spark(inputs, "memory", "memory, 60 min", 100, true), ResourceView.Spark(inputs, "gpu0", "gpu 0, 60 min", 100, true)],
            chart,
            rows,
            Readout(inputs, box, snapshot));
        return CardFrame.Sourced(report, Sources);
    }

    /// <summary>The spec line: the distribution, the processor, the memory, the GPUs and the uptime.</summary>
    public static string Spec(Data.BoxSnapshot snapshot)
    {
        string gpus = snapshot.Gpus.Count == 0 ? "no GPU" : $"{snapshot.Gpus.Count} x {snapshot.Gpus[0].Name.Replace("NVIDIA GeForce ", "", StringComparison.Ordinal)} {BoxWords.Bytes(snapshot.Gpus[0].MemTotalB)}";
        return $"{snapshot.Distro} · {snapshot.Cores.Count} cores · {BoxWords.Bytes(snapshot.Memory.TotalB)} · {gpus} · up {BoxWords.Span(TimeSpan.FromSeconds(snapshot.UptimeS))}";
    }

    /// <summary>The readout: the box, its uptime, its load, the live sample, the snapshot, the silence, the agent and the cost.</summary>
    private static CloudReadout Readout(ResourceCardInputs inputs, BoxSource box, Data.BoxSnapshot? snapshot)
    {
        string Ago(DateTimeOffset? at) => at is { } when ? $"{BoxWords.Span(inputs.Now - when)} ago" : "none yet";
        DateTimeOffset? newest = new[] { snapshot?.At, inputs.LastHour.Count == 0 ? null : inputs.LastHour[^1].At }.Where(at => at is not null).Max();
        var since = newest is { } at && at > box.Started ? at : box.Started;
        long? silentLive = box.LiveAt is { } live ? (long)(inputs.Now - live).TotalSeconds : null;
        var lines = new List<CloudLine>
        {
            new("Box", snapshot is null ? BoxSource.Waiting : $"{snapshot.Distro}, kernel {snapshot.Kernel}", snapshot is null ? "info" : "good"),
            new("Uptime", snapshot is null ? BoxSource.Waiting : $"{BoxWords.Span(TimeSpan.FromSeconds(snapshot.UptimeS))}, booted {snapshot.BootedAt:ddd d MMM HH:mm} UTC", "info"),
            new("Load", snapshot is null ? BoxSource.Waiting : $"{string.Join(" · ", snapshot.Loadavg.Select(load => load.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)))} (1, 5, 15 min)", "info"),
            new("Live sample", silentLive is { } seconds ? $"{seconds} s ago, every {ReadBoxLiveHandler.CadenceSeconds} s" : "no live sample yet", silentLive is null or > 10 ? "info" : "good"),
            new("Snapshot", newest is null ? BoxSource.Waiting : $"{Ago(newest)}, every 60 s", "info"),
            new("Silent for", $"{BoxWords.Span(inputs.Now - since)} · blue past 10 s on the live panels, red past {BoxRows.Silent.TotalMinutes:0} min", "info"),
            new("Agent", snapshot is null ? BoxSource.Waiting : $"theyard-box-agent.service, a user unit, {snapshot.AgentVersion}, {snapshot.AgentCpuPercent:0.0} % of a core", "info"),
            new("Cost", "nothing added: one small write a minute on the free tier, and the live samples held in memory", "info"),
        };
        return new CloudReadout(Title, ResourceView.Uptime(inputs), $"snapshots landed, {CloudTones.WindowWords(inputs.Window)}", lines,
            "Every figure is read from the box's own snapshot, posted with the operator's key. No address and no port reaches this card.");
    }
}
// #endregion box-card
