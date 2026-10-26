// The Azure SQL Database card (ADR: One frame for every resource): DTU and data space on the dials,
// sessions, workers, blocked and failed connections and deadlocks on the readout, and whether
// Basic is still the right tier. The paused free database beside it is named and never read.
using System.Globalization;

namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>The relational store's card.</summary>
public static class SqlCard
{
    /// <summary>Where each row and readout line of this card is read, by row key or line label (CardFrame.Sourced).</summary>
    private static readonly IReadOnlyDictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["runs"] = "ARM record, and this process's model",
        ["waiting"] = "Azure Monitor",
        ["azure"] = "Activity Log",
        ["tier"] = "kept minutes, Azure Monitor",
        ["DTU now"] = "Azure Monitor",
        ["DTU p95, 30 d"] = CardFrame.KeptMinutes,
        ["Data space"] = "Azure Monitor",
        ["Log space"] = "Azure Monitor",
        ["Sessions"] = "Azure Monitor",
        ["Workers"] = "Azure Monitor",
        ["Blocked"] = "Azure Monitor",
        ["Deadlocks"] = "Azure Monitor",
        ["Failed connections"] = "Azure Monitor",
    };

    /// <summary>The metrics the card keeps, by Azure's name and the card's.</summary>
    public static readonly IReadOnlyList<AzureMetric> Metrics =
    [
        new("dtu_consumption_percent", "average", "dtu"),
        new("storage", "maximum", "storage_gb", 1.0 / 1024 / 1024 / 1024),
        new("log_write_percent", "average", "log_write"),
        new("sessions_count", "average", "sessions"),
        new("workers_percent", "average", "workers"),
        new("blocked_by_firewall", "total", "blocked"),
        new("deadlock", "total", "deadlocks"),
        new("connection_failed", "total", "failed"),
    ];

    /// <summary>DTU amber at 60 and red at 90; data space amber at 70 and red at 85; held fifteen minutes.</summary>
    public static readonly (double Amber, double Red) Dtu = CloudThresholds.SqlDtu;
    public static readonly (double Amber, double Red) Space = CloudThresholds.SqlSpace;

    /// <summary>The card in the shared frame.</summary>
    public static CloudReport View(ResourceCardInputs inputs)
    {
        double? dtuLimit = double.TryParse(ResourceView.Fact(inputs, "dtu", ""), CultureInfo.InvariantCulture, out double d) ? d : null;
        double? spaceLimit = double.TryParse(ResourceView.Fact(inputs, "max_gb", ""), CultureInfo.InvariantCulture, out double g) ? g : null;
        var dials = new[]
        {
            ResourceView.Dial(inputs, "dtu", "DTU, right now", 100, Dtu, v => v is { } p && dtuLimit is { } l ? $"{(p / 100 * l).ToString("0.0", CultureInfo.InvariantCulture)} of {l} DTU" : "size not read yet", percentReading: true),
            ResourceView.Dial(inputs, "storage_gb", "Data space", spaceLimit, Space, v => v is { } used && spaceLimit is { } l ? $"{used.ToString("0.00", CultureInfo.InvariantCulture)} of {l} GB" : "size not read yet"),
        };
        double? sessions = ResourceView.Now(inputs, "sessions");
        double? deadlocks = ResourceView.WindowSum(inputs, "deadlocks"), failed = ResourceView.WindowSum(inputs, "failed"), blocked = ResourceView.WindowSum(inputs, "blocked");
        var events = inputs.Hours.SelectMany(h => h.Events).ToList();
        var dtuTone = PlanThresholds.ToneOf(ResourceView.Hour(inputs, "dtu"), Dtu);
        // The database this card reads, as its own record says.
        var databases = new List<IReadOnlyList<CloudLink>>
        {
            new CloudLink[] { ResourceView.Resource(inputs) ?? new CloudLink("the database, not read yet", null), new("SQL database", null), new(ResourceView.Fact(inputs, "spec"), null), new("yes, a minute", null) },
        };
        var rows = new List<CloudRow>
        {
            new("runs", "What runs on it?", inputs.Hours.Count == 0 ? "not read yet" : ResourceView.Fact(inputs, "served_by", "no site of this process names it"),
                [new($"EF Core · {ResourceView.Fact(inputs, "tables")} tables · migrations at {ResourceView.Fact(inputs, "migration")}", null)], "good")
            {
                Table = inputs.Hours.Count == 0 ? CloudTable.None : new CloudTable(
                    ["Database", "Type", "Size", "Read by this card"],
                    databases),
            },
            new("waiting", "Is it waiting on anything?", blocked is null && deadlocks is null ? "not read yet" : $"{ResourceView.Number(blocked)} blocked",
                [new($"sessions {ResourceView.Number(sessions)} · workers {PlanView.Percent(ResourceView.Now(inputs, "workers"))} · log IO {PlanView.Percent(ResourceView.Now(inputs, "log_write"))} · deadlocks {ResourceView.Number(deadlocks)} in the window", null)],
                deadlocks is > 0 ? "alarm" : CloudTones.Of(dtuTone == PlanTone.Info ? PlanTone.Good : dtuTone)),
            new("azure", "What did Azure do to it?", inputs.Hours.Count == 0 ? "not read yet" : $"{events.Count} events, {CloudTones.WindowWords(inputs.Window)}",
                [new(events.Count == 0 ? "no failover, no maintenance" : string.Join(" · ", events.TakeLast(3).Select(e => $"{e.At.ToString("d MMM HH:mm", CultureInfo.InvariantCulture)} {e.Title}")), null)],
                CloudTones.Of(PlanThresholds.EventsTone(events))),
            new("tier", $"Is {ResourceView.Fact(inputs, "tier", "the tier")} still right?", inputs.Month.Count == 0 ? "not enough kept yet" : dtuTone == PlanTone.Alarm ? "no, DTU is past its line" : "yes, DTU first",
                [new($"DTU p95 {PlanView.Percent(ResourceFolding.Spread(inputs.Month, "dtu").P95)} over 30 d · red at {Dtu.Red} % held {PlanThresholds.Sustained} min", null)],
                CloudTones.Of(dtuTone == PlanTone.Alarm ? PlanTone.Alarm : PlanTone.Good)),
        };
        var readout = new CloudReadout(
            "The database",
            ResourceView.Uptime(inputs),
            $"up, {CloudTones.WindowWords(inputs.Window)}",
            [
                new("DTU now", PlanView.Percent(ResourceView.Now(inputs, "dtu")), CloudTones.Of(dtuTone)),
                new("DTU p95, 30 d", PlanView.Percent(ResourceFolding.Spread(inputs.Month, "dtu").P95), "info"),
                new("Data space", ResourceView.Now(inputs, "storage_gb") is { } used && spaceLimit is { } l ? $"{used.ToString("0.00", CultureInfo.InvariantCulture)} / {l} GB" : "not read", "info"),
                new("Log space", PlanView.Percent(ResourceView.Now(inputs, "log_write")), "info"),
                new("Sessions", ResourceView.Number(sessions), "info"),
                new("Workers", PlanView.Percent(ResourceView.Now(inputs, "workers")), "info"),
                new("Blocked", ResourceView.Number(blocked), blocked is > 0 ? "warning" : "info"),
                new("Deadlocks", ResourceView.Number(deadlocks), deadlocks is > 0 ? "alarm" : "info"),
                new("Failed connections", ResourceView.Number(failed), failed is > 0 ? "warning" : "info"),
                new("Tier", ResourceView.Fact(inputs, "tier"), "info") { Source = "ARM record" },
                ResourceView.Cost(inputs),
                ResourceView.LiveRead(inputs),
            ],
            $"{inputs.Availability.Reason}, one minute each; live every 60 s from Azure Monitor");
        return CardFrame.Sourced(new CloudReport(
            inputs.Window, PlanView.Windows, inputs.Availability.Available && inputs.LastHour.Count > 0,
            ResourceView.Note(inputs, ResourceView.FirstRead(inputs, "minute")),
            inputs.Now.ToUnixTimeMilliseconds(), ResourceView.Age(inputs),
            "Azure SQL Database",
            Latest(inputs, "spec"),
            ResourceView.Resource(inputs),
            dials,
            [ResourceView.Spark(inputs, "dtu", "DTU, 60 min", 100, percentReading: true), ResourceView.Spark(inputs, "workers", "Workers, 60 min", 100, percentReading: true)],
            ResourceView.Chart(inputs, "DTU and data space", ("dtu", "DTU", 100, true), ("storage_gb", "Data space", spaceLimit, false), Dtu.Red, $"teal DTU · green data space · gold deploys · red event · dashed {Dtu.Red} %", firstLines: (Dtu.Amber, Dtu.Red), secondLines: (Space.Amber, Space.Red)),
            rows,
            readout), Sources);
    }

    private static string? Latest(ResourceCardInputs inputs, string key) => ResourceView.Latest(inputs)?.Facts.GetValueOrDefault(key);

    /// <summary>
    /// The hour's facts from the database's own record: its tier and DTU, its size cap, and the
    /// header's one line. The tables and migration come from the host's model, handed in.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> Facts(System.Text.Json.JsonElement record, int tables, string migration)
    {
        string? tier = AzureMetrics.Text(record, "sku.tier") ?? AzureMetrics.Text(record, "sku.name");
        string? dtu = AzureMetrics.Text(record, "sku.capacity");
        double? max = double.TryParse(AzureMetrics.Text(record, "properties.maxSizeBytes"), CultureInfo.InvariantCulture, out double bytes) ? bytes / 1024 / 1024 / 1024 : null;
        return new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["tier"] = tier,
            ["dtu"] = dtu,
            ["max_gb"] = max?.ToString("0.##", CultureInfo.InvariantCulture),
            ["tables"] = tables.ToString(CultureInfo.InvariantCulture),
            ["migration"] = migration,
            ["spec"] = string.Join(' ', new[] { tier, dtu is null ? null : $"{dtu} DTU", max is null ? null : $"{max.Value.ToString("0.##", CultureInfo.InvariantCulture)} GB", AzureMetrics.Text(record, "location") }.Where(p => p is not null)).ToUpperInvariant(),
        };
    }
}
