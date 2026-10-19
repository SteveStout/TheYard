// The plan card's four questions and its readout, in the shared frame's shapes. Their own file
// because they are the card's judgement: which reading answers which question, and when a row
// turns amber or red (the levels themselves are PlanThresholds, in Application).
using System.Globalization;
using TheYard.Application;

namespace TheYard.Api;

// #region plan-rows
/// <summary>The plan's four question rows and its readout lines, from the kept minutes and hours.</summary>
public static class PlanRows
{
    /// <summary>What it powers, whether anything queues, what Azure did to it, and whether its size still fits.</summary>
    public static IReadOnlyList<CloudRow> Questions(IReadOnlyList<PlanMinute> lastHour, PlanHour? hour, IReadOnlyList<PlanHour> hours, PlanSpread month, IReadOnlyList<PlanBucket> buckets)
    {
        var newest = lastHour.Count == 0 ? null : lastHour[^1];
        var apps = hour?.Apps ?? [];
        int running = apps.Count(app => string.Equals(app.State, "Running", StringComparison.OrdinalIgnoreCase));
        var power = new CloudRow(
            "power",
            "What does it power?",
            apps.Count == 0 ? "not read yet" : running == apps.Count ? $"{apps.Count} apps, running" : $"{apps.Count - running} of {apps.Count} apps stopped",
            [],
            CloudTones.Of(PlanThresholds.AppsTone(apps)))
        {
            // The apps are a list, so they are a table: each name a link to its blade, with what each
            // holds and burns now, and a last row for what the plan holds outside every app.
            Table = apps.Count == 0 ? CloudTable.None : new CloudTable(
                ["App", "Type", "Runs", "Talks to", "State", "Always on", "Memory now", "CPU now"],
                [.. apps.Select(app => (IReadOnlyList<CloudLink>)[
                    new CloudLink(app.Name, PlanView.Portal(hour!.TenantId, app.ResourceId)),
                    new CloudLink("Web app (container)", null),
                    new CloudLink(Runtime(app.Name), null),
                    new CloudLink(Store(app.Name), null),
                    new CloudLink(app.State, null),
                    new CloudLink(app.AlwaysOn is { } on ? (on ? "yes" : "no") : "not read", null),
                    new CloudLink(Megabytes(PlanChildren.MemoryMb(lastHour, app.Name)), null),
                    new CloudLink(Percent(PlanChildren.CpuPercent(lastHour, app.Name, hour.Cores)), null)]),
                    (IReadOnlyList<CloudLink>)[
                        new CloudLink("the plan, outside the apps", null),
                        // Derived, not read: no Azure metric names this share. It is the plan's
                        // memory in use minus every app's own reading (PlanChildren.OutsideMb): the
                        // host, the platform agent, the image layers and one Kudu sidecar per app
                        // (ADR: The App Service plan, read every minute, the addendum of 19 October).
                        new CloudLink(DerivedPlatform, null),
                        new CloudLink("App Service", null),
                        new CloudLink("every app on it", null),
                        new CloudLink(hour!.Linux == true ? "Linux" : "", null),
                        new CloudLink("", null),
                        new CloudLink(Megabytes(PlanChildren.OutsideMb(lastHour, hour.MemoryGb)), null),
                        new CloudLink("", null)]]),
        };

        double? used = Sum(newest?.Apps.Select(app => app.FileSystemMb) ?? []);
        double? storage = PlanSizes.StorageGbOf(hour?.Sku);
        var queueTone = CloudTones.Worse(
            PlanThresholds.ToneOf(lastHour.Select(m => m.HttpQueue).ToList(), PlanThresholds.HttpQueue),
            PlanThresholds.SocketsTone(lastHour.Select(m => m.Sockets).ToList()));
        var queue = new CloudRow(
            "queue",
            "Is anything queueing?",
            newest?.HttpQueue is { } waiting ? $"{waiting.ToString("0", CultureInfo.InvariantCulture)} waiting" : "not read yet",
            [new CloudLink(string.Join(" · ", new[]
            {
                $"sockets {Count(newest?.Sockets)} of {PlanThresholds.SocketLimit:N0}",
                used is { } mb && storage is { } gb ? $"disk {(mb / 1024).ToString("0.0", CultureInfo.InvariantCulture)} of {gb} GB" : "disk not read",
                $"data out {Bytes(Sum(lastHour.Select(m => m.BytesSent)))} this hour",
            }), null)],
            CloudTones.Of(lastHour.Count == 0 ? PlanTone.Info : queueTone));

        var events = hours.SelectMany(h => h.Events).GroupBy(e => (e.At, e.Title, e.Resource)).Select(g => g.First()).OrderBy(e => e.At).ToList();
        var restarts = events.Where(e => e.Kind == "restart").ToList();
        int deploys = PlanView.Deploys(buckets).Count();
        string days = hours.Count == 0 ? "" : $", {Math.Max(1, (int)Math.Round((hours[^1].At - hours[0].At).TotalDays))} d";
        var azure = new CloudRow(
            "azure",
            "What did Azure do to it?",
            hours.Count == 0 ? "not read yet" : restarts.Count > 0 ? $"{restarts.Count} restart{(restarts.Count == 1 ? "" : "s")}{days}" : $"{events.Count} events{days}",
            [new CloudLink(restarts.Count > 0
                ? $"{restarts[^1].At.ToString("d MMM HH:mm", CultureInfo.InvariantCulture)} UTC, {(restarts[^1].Planned ? "planned" : "by Azure, unplanned")} · {deploys} deploys in the window"
                : $"no restart, no scale, no maintenance · {deploys} deploys in the window", null),
                .. PlanDeploys.Blips(PlanDeploys.Rolls(events, buckets), buckets).Select(blip => new CloudLink($" · {blip}", null))],
            CloudTones.Of(PlanThresholds.EventsTone(events)))
        {
            // The window's rolls, newest first: each a planned swap, with how long the two containers overlapped.
            Table = PlanDeploys.Rolls(events, buckets) is { Count: > 0 } rolls
                ? new CloudTable(
                    ["When", "Type", "What", "Took"],
                    [.. rolls.OrderByDescending(r => r.StartMs).Take(8).Select(r => (IReadOnlyList<CloudLink>)[
                        new CloudLink(DateTimeOffset.FromUnixTimeMilliseconds(r.StartMs).UtcDateTime.ToString("MMM d HH:mm", CultureInfo.InvariantCulture) + " UTC", null),
                        new CloudLink(r.Title.StartsWith("Deploy", StringComparison.Ordinal) ? "Deploy, planned" : "Configuration, planned", null),
                        new CloudLink(r.Title, null),
                        new CloudLink($"{Math.Max(1, (int)Math.Round((r.EndMs - r.StartMs) / 60_000.0))} min", null)])])
                : CloudTable.None,
        };

        double cpuShare = (month.CpuP95 ?? 0) / PlanThresholds.Cpu.Red, memoryShare = (month.MemoryP95 ?? 0) / PlanThresholds.Memory.Red;
        bool memoryFirst = memoryShare >= cpuShare;
        var worst = CloudTones.Worse(
            PlanThresholds.ToneOf(lastHour.Select(m => m.CpuPercent).ToList(), PlanThresholds.Cpu),
            PlanThresholds.ToneOf(lastHour.Select(m => m.MemoryPercent).ToList(), PlanThresholds.Memory));
        var size = new CloudRow(
            "size",
            $"Is {hour?.Sku ?? "the size"} still right?",
            month.CpuP95 is null && month.MemoryP95 is null ? "not enough kept yet"
                : worst == PlanTone.Alarm ? $"no, {(memoryFirst ? "memory" : "CPU")} is past its line"
                : $"yes, {(memoryFirst ? "memory" : "CPU")} first",
            [new CloudLink($"{(PlanView.NextSize(hour?.Sku) is { } next ? $"{next.Size} +${next.ExtraPerMonth.ToString("0.00", CultureInfo.InvariantCulture)}/mo" : "no Basic size above it")} · the renderer's 128 MB heap is the $0 lever · red at {(memoryFirst ? "85" : "80")} % p95", null)],
            CloudTones.Of(worst == PlanTone.Info && month.Minutes > 0 ? PlanTone.Good : worst));
        return [power, queue, azure, size];
    }

    /// <summary>The readout: the uptime ring and twelve lines, the last saying how fresh the read is.</summary>
    public static CloudReadout Readout(IReadOnlyList<PlanMinute> lastHour, PlanHour? hour, IReadOnlyList<PlanHour> hours, PlanSpread month, double? cost, long? age, PlanHistoryAvailability availability)
    {
        var newest = lastHour.Count == 0 ? null : lastHour[^1];
        int up = hours.Count(h => h.Apps.Count > 0 && h.Apps.All(app => string.Equals(app.State, "Running", StringComparison.OrdinalIgnoreCase)));
        double? uptime = hours.Count == 0 ? null : Math.Round(100.0 * up / hours.Count, 2);
        double? used = Sum(newest?.Apps.Select(app => app.FileSystemMb) ?? []);
        double? storage = PlanSizes.StorageGbOf(hour?.Sku);
        int restarts = hours.SelectMany(h => h.Events).Count(e => e.Kind == "restart");
        bool unplanned = hours.SelectMany(h => h.Events).Any(e => e.Kind == "restart" && !e.Planned);
        string tone(PlanTone t) => CloudTones.Of(t);
        return new CloudReadout(
            "The plan",
            uptime,
            $"up, {(hours.Count == 0 ? "not read yet" : $"{Math.Max(1, (int)Math.Round(hours.Count / 24.0))} days")}",
            [
                new("CPU now", PlanView.Percent(newest?.CpuPercent), tone(PlanThresholds.ToneOf(lastHour.Select(m => m.CpuPercent).ToList(), PlanThresholds.Cpu))),
                new("CPU p95, 30 d", PlanView.Percent(month.CpuP95), "info"),
                new("Memory now", newest?.MemoryPercent is { } m && hour?.MemoryGb is { } g ? $"{(m / 100 * g).ToString("0.00", CultureInfo.InvariantCulture)} GB" : "not read", tone(PlanThresholds.ToneOf(lastHour.Select(x => x.MemoryPercent).ToList(), PlanThresholds.Memory))),
                new("Memory p95, 30 d", PlanView.Percent(month.MemoryP95), "info"),
                new("HTTP queue", Count(newest?.HttpQueue), tone(PlanThresholds.ToneOf(lastHour.Select(x => x.HttpQueue).ToList(), PlanThresholds.HttpQueue))),
                new("Sockets", $"{Count(newest?.Sockets)} / {PlanThresholds.SocketLimit:N0}", tone(PlanThresholds.SocketsTone(lastHour.Select(x => x.Sockets).ToList()))),
                new("Disk", used is { } mb && storage is { } gb ? $"{(mb / 1024).ToString("0.0", CultureInfo.InvariantCulture)} / {gb} GB" : "not read", "info"),
                new("Instances", hour?.Instances is { } n ? $"{n} of {PlanSizes.MaxInstancesOf(hour.Sku)?.ToString(CultureInfo.InvariantCulture) ?? "?"}" : "not read", "info"),
                new("Always on", hour is null ? "not read" : $"{hour.Apps.Count(app => app.AlwaysOn is true)} apps", "info"),
                new("Restarts, 30 d", hours.Count == 0 ? "not read" : restarts.ToString(CultureInfo.InvariantCulture), unplanned ? "alarm" : "info"),
                new("Plan cost", cost is { } dollars ? $"${dollars.ToString("0.00", CultureInfo.InvariantCulture)} / mo" : "not read from the bill yet", "info"),
                new("Live read", age is { } seconds ? $"{seconds} s ago" : "not read yet", age is > 300 ? "warning" : "info"),
            ],
            $"{availability.Reason}, one minute each; live every 60 s from Azure Monitor");
    }

    /// <summary>What the plan card's last row is, said on the row: a difference, not a reading.</summary>
    public const string DerivedPlatform = "Linux and the platform (derived: plan memory used minus the apps)";

    /// <summary>What an app on the plan runs: Node.js for the rendering service, .NET 10 for the rest.</summary>
    public static string Runtime(string name) =>
        name.Contains("render", StringComparison.OrdinalIgnoreCase) ? "Node.js" : ".NET 10";

    /// <summary>
    /// What an app on the plan keeps its data in. The stores are resources of their own, each with
    /// its own card under Cloud infrastructure; nothing on the plan holds them.
    /// </summary>
    public static string Store(string name) =>
        name.Contains("-ONE-", StringComparison.OrdinalIgnoreCase) ? "SQL database and Cosmos DB account, each its own resource; The Shed's files in the image"
        : name.Contains("shed", StringComparison.OrdinalIgnoreCase) ? "its own files, on the plan's storage"
        : name.Contains("render", StringComparison.OrdinalIgnoreCase) ? "the two sites' API (web app)"
        : name.Contains("cosmos", StringComparison.OrdinalIgnoreCase) ? "Cosmos DB account, its own resource"
        : "SQL database, its own resource";

    private static double? Sum(IEnumerable<double?> values)
    {
        var read = values.Where(v => v is not null).Select(v => v!.Value).ToList();
        return read.Count == 0 ? null : read.Sum();
    }

    private static string Megabytes(double? value) => value is { } v ? $"{Math.Round(v).ToString("N0", CultureInfo.InvariantCulture)} MB" : "not read";

    private static string Percent(double? value) => value is { } v ? $"{v.ToString("0.0", CultureInfo.InvariantCulture)} %" : "not read";

    private static string Count(double? value) => value is { } v ? Math.Round(v).ToString("N0", CultureInfo.InvariantCulture) : "not read";

    private static string Bytes(double? value) => value switch
    {
        null => "not read",
        >= 1024 * 1024 * 1024 => $"{(value.Value / 1024 / 1024 / 1024).ToString("0.0", CultureInfo.InvariantCulture)} GB",
        >= 1024 * 1024 => $"{(value.Value / 1024 / 1024).ToString("0.0", CultureInfo.InvariantCulture)} MB",
        _ => $"{(value.Value / 1024).ToString("0.0", CultureInfo.InvariantCulture)} KB",
    };
}
// #endregion plan-rows
