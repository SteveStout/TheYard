// The plan's deploys as spans, read from what is already kept: each roll writes the site's
// configuration and then its image, which Azure's Activity Log records as config writes on the
// app, and the first minute kept after it carries the new build's version. A span runs from the
// first write to the new container answering, so a CPU or memory spike inside one reads as the
// roll it was: App Service starts the new container beside the old one, and for a minute or two
// the plan runs both.
using System.Globalization;
using TheYard.Application;

namespace TheYard.Api.Shared.PlanReadings;

// #region plan-deploys
/// <summary>The plan's deploys as chart spans, and the spikes that fell inside them.</summary>
public static class PlanDeploys
{
    /// <summary>Writes this close together are one roll: the settings, then the image, then the next app.</summary>
    public static readonly TimeSpan SameRoll = TimeSpan.FromMinutes(10);

    /// <summary>How long after its last write a roll is still counted: the new container starting and answering.</summary>
    public static readonly TimeSpan Settle = TimeSpan.FromMinutes(3);

    /// <summary>
    /// How long after a roll's first write the new build may take to be the one recording: the old
    /// container keeps writing minutes under its own version until the swap, eight to twelve minutes
    /// after the image push on this plan, and Deploy's Verify allows twenty-five. On a window of
    /// minutes the first bucket after the write still carries the old build, so the band's name is
    /// the first build inside this span that differs from the one before it.
    /// </summary>
    public static readonly TimeSpan NewBuildWithin = TimeSpan.FromMinutes(30);

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
            string? before = buckets.LastOrDefault(b => b.At < start && b.Version is not null)?.Version;
            string? version = buckets.Where(b => b.At >= start && b.At <= start + NewBuildWithin)
                .Select(b => b.Version)
                .FirstOrDefault(v => v is not null && v != before)
                ?? buckets.FirstOrDefault(b => b.At >= start && b.Version is not null)?.Version;
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
                said.Add($"CPU reached {cpu.Value.ToString("0", CultureInfo.InvariantCulture)} % in the roll at {when} UTC, a blip: the two containers overlapped for {Overlap(band)}");
            }
            else if (memory >= PlanThresholds.Memory.Amber)
            {
                said.Add($"memory reached {memory.Value.ToString("0", CultureInfo.InvariantCulture)} % in the roll at {when} UTC, a blip: the two containers overlapped for {Overlap(band)}");
            }
        }
        return said;
    }

    /// <summary>How long a roll's two containers ran side by side, as measured by the roll's own band.</summary>
    public static string Overlap(CloudBand band)
    {
        int minutes = Math.Max(1, (int)Math.Round((band.EndMs - band.StartMs) / 60_000.0));
        return minutes == 1 ? "1 minute" : $"{minutes} minutes";
    }

    /// <summary>The legend's note when the keep is younger than the window: where the lines begin.</summary>
    public static string? KeptSince(IReadOnlyList<PlanBucket> buckets, DateTimeOffset windowStart, TimeSpan grain) =>
        buckets.Count > 0 && buckets[0].At - windowStart > grain
            ? $"kept since {buckets[0].At.UtcDateTime.ToString("MMM d HH:mm", CultureInfo.InvariantCulture)} UTC"
            : null;
}
// #endregion plan-deploys
