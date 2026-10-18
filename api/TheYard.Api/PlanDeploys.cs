// The plan's deploys as spans, read from what is already kept: each roll writes the site's
// configuration and then its image, which Azure's Activity Log records as config writes on the
// app, and the first minute kept after it carries the new build's version. A span runs from the
// first write to the new container answering, so a CPU or memory spike inside one reads as the
// roll it was: App Service starts the new container beside the old one, and for a minute or two
// the plan runs both.
using System.Globalization;
using TheYard.Application;

namespace TheYard.Api;

// #region plan-deploys
/// <summary>The plan's deploys as chart spans, and the spikes that fell inside them.</summary>
public static class PlanDeploys
{
    /// <summary>Writes this close together are one roll: the settings, then the image, then the next app.</summary>
    public static readonly TimeSpan SameRoll = TimeSpan.FromMinutes(10);

    /// <summary>How long after its last write a roll is still counted: the new container starting and answering.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromMinutes(3);

    /// <summary>The window's rolls as spans, each named for the build the minutes after it carry.</summary>
    public static IReadOnlyList<CloudBand> Rolls(IReadOnlyList<PlanEvent> events, IReadOnlyList<PlanBucket> buckets)
    {
        var writes = events.Where(e => e.Kind == "config").Select(e => e.At).Distinct().Order().ToList();
        var bands = new List<CloudBand>();
        int at = 0;
        while (at < writes.Count)
        {
            var start = writes[at];
            var last = start;
            while (at + 1 < writes.Count && writes[at + 1] - last <= SameRoll)
            {
                last = writes[++at];
            }
            at++;
            var end = last + Settle;
            string? version = buckets.FirstOrDefault(b => b.At >= start && b.Version is not null)?.Version;
            string? before = buckets.LastOrDefault(b => b.At < start && b.Version is not null)?.Version;
            string title = version is not null && before is not null && version != before
                ? $"Deploy {version}: a new container beside the old, then the swap"
                : "A configuration change on the plan's apps";
            bands.Add(new CloudBand(start.ToUnixTimeMilliseconds(), end.ToUnixTimeMilliseconds(), title));
        }
        return bands;
    }

    /// <summary>
    /// Each roll in which CPU or memory went over its amber line, as one sentence: what peaked,
    /// when, and that it was the roll, held under the fifteen-minute rule so it never alarmed.
    /// </summary>
    public static IReadOnlyList<string> Blips(IReadOnlyList<CloudBand> bands, IReadOnlyList<PlanBucket> buckets)
    {
        var said = new List<string>();
        foreach (var band in bands)
        {
            var inside = buckets.Where(b => b.At.ToUnixTimeMilliseconds() >= band.StartMs - 300_000 && b.At.ToUnixTimeMilliseconds() <= band.EndMs).ToList();
            double? cpu = inside.Max(b => b.CpuMaxPercent ?? b.CpuPercent);
            double? memory = inside.Max(b => b.MemoryMaxPercent ?? b.MemoryPercent);
            string when = DateTimeOffset.FromUnixTimeMilliseconds(band.StartMs).UtcDateTime.ToString("MMM d HH:mm", CultureInfo.InvariantCulture);
            if (cpu >= PlanThresholds.Cpu.Amber)
            {
                said.Add($"CPU reached {cpu.Value.ToString("0", CultureInfo.InvariantCulture)} % in the roll at {when} UTC, a blip: the two containers overlap for a minute or two");
            }
            else if (memory >= PlanThresholds.Memory.Amber)
            {
                said.Add($"memory reached {memory.Value.ToString("0", CultureInfo.InvariantCulture)} % in the roll at {when} UTC, a blip: the two containers overlap for a minute or two");
            }
        }
        return said;
    }

    /// <summary>The legend's note when the keep is younger than the window: where the lines begin.</summary>
    public static string? KeptSince(IReadOnlyList<PlanBucket> buckets, DateTimeOffset windowStart, TimeSpan grain) =>
        buckets.Count > 0 && buckets[0].At - windowStart > grain
            ? $"kept since {buckets[0].At.UtcDateTime.ToString("MMM d HH:mm", CultureInfo.InvariantCulture)} UTC"
            : null;
}
// #endregion plan-deploys
