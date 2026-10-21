// The plan card's answer in the shared frame (ADR: The App Service plan, read every minute), built from what
// is kept: the newest hour of minutes, the window's buckets, the thirty-day spread and the hours'
// slower facts. Pure, so PlanViewTests hand it fixed minutes and read a fixed report back; the
// reads and the cache are PlanHistoryReader.
using System.Globalization;
using TheYard.Application;

namespace TheYard.Api;

// #region plan-view
/// <summary>What one window of the plan card says, from the kept minutes and nothing else.</summary>
public static class PlanView
{
    /// <summary>The windows the card offers, the tab's shared three.</summary>
    public static readonly IReadOnlyList<string> Windows = ["24h", "7d", "30d"];

    /// <summary>Builds the card. Absent is a sentence, never a zero: every figure not kept says "not read".</summary>
    public static CloudReport Build(
        string window,
        TimeSpan length,
        DateTimeOffset now,
        PlanHistoryAvailability availability,
        IReadOnlyList<PlanMinute> lastHour,
        IReadOnlyList<PlanBucket> buckets,
        PlanSpread month,
        IReadOnlyList<PlanHour> hours,
        double? planCostMonth,
        string recordingSite = "the recording site")
    {
        var newest = lastHour.Count == 0 ? null : lastHour[^1];
        var hour = hours.Count == 0 ? null : hours[^1];
        long? age = newest is null ? null : (long)(now - newest.At).TotalSeconds;
        string? note = !availability.Available ? availability.Reason
            : newest is null ? $"no minute has been kept yet; the first is read a minute after {recordingSite} starts"
            : null;

        double? cores = hour?.Cores, memoryGb = hour?.MemoryGb;
        var cpuTone = PlanThresholds.ToneOf(lastHour.Select(m => m.CpuPercent).ToList(), PlanThresholds.Cpu);
        var memoryTone = PlanThresholds.ToneOf(lastHour.Select(m => m.MemoryPercent).ToList(), PlanThresholds.Memory);
        var dials = new List<CloudDial>
        {
            new("CPU, right now", newest?.CpuPercent, Percent(newest?.CpuPercent),
                newest?.CpuPercent is { } cpu && cores is { } c ? $"{(cpu / 100 * c).ToString("0.00", CultureInfo.InvariantCulture)} of {c} vCPU" : "size not read yet",
                PlanThresholds.Cpu.Amber, PlanThresholds.Cpu.Red, month.CpuP95, month.CpuPeak, CloudTones.Dial(newest?.CpuPercent, PlanThresholds.Cpu, cpuTone)),
            new("Memory, right now", newest?.MemoryPercent, Percent(newest?.MemoryPercent),
                newest?.MemoryPercent is { } mem && memoryGb is { } g ? $"{(mem / 100 * g).ToString("0.00", CultureInfo.InvariantCulture)} of {g} GB" : "size not read yet",
                PlanThresholds.Memory.Amber, PlanThresholds.Memory.Red, month.MemoryP95, month.MemoryPeak, CloudTones.Dial(newest?.MemoryPercent, PlanThresholds.Memory, memoryTone)),
        };

        var marks = Deploys(buckets)
            .Concat(hours.SelectMany(h => h.Events).Where(e => e.Kind == "restart" || !e.Planned)
                .GroupBy(e => (e.At, e.Title)).Select(g => g.First())
                .Select(e => new CloudMark(e.At.ToUnixTimeMilliseconds(), "event", $"{e.Title} on {e.Resource}{(e.Planned ? "" : ", by Azure")}")))
            .OrderBy(mark => mark.AtMs)
            .ToList();
        var allEvents = hours.SelectMany(h => h.Events).GroupBy(e => (e.At, e.Title, e.Resource)).Select(g => g.First()).ToList();
        var rolls = PlanDeploys.Rolls(allEvents, buckets);
        string? keptSince = PlanDeploys.KeptSince(buckets, now - length, TimeSpan.FromTicks(length.Ticks / 60));
        var chart = new CloudChart(
            $"CPU and memory, {CloudTones.WindowWords(window)}",
            [
                new("CPU", buckets.Select(b => new CloudPoint(b.At.ToUnixTimeMilliseconds(), b.CpuPercent)).ToList()),
                new("Memory", buckets.Select(b => new CloudPoint(b.At.ToUnixTimeMilliseconds(), b.MemoryPercent)).ToList()),
            ],
            marks,
            PlanThresholds.Cpu.Red,
            $"teal CPU · green memory · gold deploys, shaded while the roll runs · red Azure restart · dashed {PlanThresholds.Cpu.Red:0} %" + (keptSince is null ? "" : $" · {keptSince}"),
            (now - length).ToUnixTimeMilliseconds())
        {
            Bands = rolls,
        };

        return CardFrame.Sourced(new CloudReport(
            window,
            Windows,
            availability.Available && newest is not null,
            note,
            now.ToUnixTimeMilliseconds(),
            age,
            "The App Service plan",
            Spec(hour),
            hour is null ? null : new CloudLink(PlanReader.NameOf(hour.PlanId), Portal(hour.TenantId, hour.PlanId)),
            dials,
            [new("CPU, 60 min", lastHour.Select(m => m.CpuPercent).ToList()), new("Memory, 60 min", lastHour.Select(m => m.MemoryPercent).ToList())],
            chart,
            PlanRows.Questions(lastHour, hour, hours, month, buckets),
            PlanRows.Readout(lastHour, hour, hours, month, planCostMonth, age, availability)), PlanRows.Sources);
    }


    /// <summary>The size line in the header's right side: "B1 LINUX 1 vCPU / 1.75 GB WEST US 3".</summary>
    public static string? Spec(PlanHour? hour) =>
        hour is null ? null
            : string.Join(' ', new[]
            {
                hour.Sku,
                hour.Linux is true ? "Linux" : hour.Linux is false ? "Windows" : null,
                hour.Cores is { } c ? $"{c} vCPU" : null,
                hour.MemoryGb is { } g ? $"/ {g} GB" : null,
                hour.Location,
            }.Where(part => part is not null)).ToUpperInvariant();

    /// <summary>A gold line on the chart wherever a bucket first shows a build the bucket before it did not.</summary>
    public static IEnumerable<CloudMark> Deploys(IReadOnlyList<PlanBucket> buckets)
    {
        string? before = null;
        foreach (var bucket in buckets)
        {
            if (before is not null && bucket.Version is not null && bucket.Version != before)
            {
                yield return new CloudMark(bucket.At.ToUnixTimeMilliseconds(), "deploy", $"Deployed {bucket.Version}");
            }
            before = bucket.Version ?? before;
        }
    }

    /// <summary>
    /// A resource's blade in the Azure portal, from the tenant and resource id Azure returned;
    /// it opens with the viewer's own Azure sign-in. Null when either was not read, never guessed.
    /// </summary>
    public static string? Portal(string? tenant, string? resourceId) =>
        string.IsNullOrWhiteSpace(tenant) || string.IsNullOrWhiteSpace(resourceId)
            ? null
            : $"https://portal.azure.com/#@{tenant}/resource{(resourceId.StartsWith('/') ? "" : "/")}{resourceId}";

    /// <summary>A percentage the way the card prints it.</summary>
    public static string Percent(double? value) => value is { } v ? $"{v.ToString("0", CultureInfo.InvariantCulture)} %" : "not read";
}
// #endregion plan-view
