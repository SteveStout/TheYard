// The Application Insights card (ADR: One frame for every resource): today's ingestion against
// the daily cap and the effective sampling on the dials, who reports to it, whether the cap ever
// stopped collection (red, because those minutes are data lost), what it costs past the free
// 5 GB, and whether the cap is still set right. The workspace's Usage table moves hourly, so the
// card keeps one document an hour.
using System.Globalization;
using TheYard.Application;

namespace TheYard.Api;

/// <summary>The Application Insights card.</summary>
public static class InsightsCard
{
    /// <summary>Where each row and readout line of this card is read, by row key or line label (CardFrame.Sourced).</summary>
    private static readonly IReadOnlyDictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["reporters"] = "Log Analytics",
        ["dropped"] = "Log Analytics, and the ARM record of the component",
        ["cost"] = "Log Analytics, and the bill",
        ["cap"] = "ARM record of the component",
        ["Today"] = "Log Analytics",
        ["This month"] = "Log Analytics",
        ["Free allowance"] = "Microsoft's Azure Monitor pricing page",
        ["Sampling kept"] = "Log Analytics",
        ["Capped hours"] = "Log Analytics",
        ["Apps reporting"] = "Log Analytics",
        ["Workspace"] = "ARM record of the component",
    };

    public static readonly (double Amber, double Red) Ingestion = CloudThresholds.InsightsIngestion;

    /// <summary>The sampling dial runs the other way: what was dropped by sampling is read against these.</summary>
    public static readonly (double Amber, double Red) Dropped = CloudThresholds.InsightsDropped;

    public static readonly IReadOnlyList<string> Readings = ["ingest_today_gb", "ingest_month_gb", "sampling", "capped"];

    public static CloudReport View(ResourceCardInputs inputs)
    {
        double? cap = InsightsSource.Parse(ResourceView.Fact(inputs, "cap_gb", ""));
        double? today = ResourceView.Now(inputs, "ingest_today_gb"), month = ResourceView.Now(inputs, "ingest_month_gb"), sampling = ResourceView.Now(inputs, "sampling");
        var ingestTone = ResourceView.Tone(ResourceView.Hour(inputs, "ingest_today_gb").Select(v => ResourceView.Share(v, cap)).ToList(), Ingestion, hourly: true);
        var droppedTone = ResourceView.Tone(ResourceView.Hour(inputs, "sampling").Select(v => v is { } s ? 100 - s : (double?)null).ToList(), Dropped, hourly: true);
        double cappedHours = inputs.Buckets.Sum(b => b.Sums.GetValueOrDefault("capped") is > 0 ? 1 : 0);
        double? billable = month is { } m ? Math.Max(0, m - InsightsSource.FreeGbPerMonth) : null;
        string free = $"the free {InsightsSource.FreeGbPerMonth:0} GB";
        var dials = new[]
        {
            ResourceView.Dial(inputs, "ingest_today_gb", "Ingested today", cap, Ingestion, v => v is { } g ? $"{g.ToString("0.000", CultureInfo.InvariantCulture)} of {(cap is { } c ? c.ToString("0.###", CultureInfo.InvariantCulture) + " GB cap" : "no cap")}" : "not read", hourly: true),
            ResourceView.Dial(inputs, "sampling", "Sampling kept", 100, (100 - Dropped.Amber, 100 - Dropped.Red), v => v is { } s ? $"{s.ToString("0", CultureInfo.InvariantCulture)} % of requests kept" : "no requests in the hour", percentReading: true, hourly: true) with { Tone = CloudTones.Of(droppedTone) },
        };
        var marks = inputs.Buckets.Where(b => b.Sums.GetValueOrDefault("capped") is > 0)
            .Select(b => new CloudMark(b.At.ToUnixTimeMilliseconds(), "event", "The daily cap stopped collection"));
        bool refused = ResourceView.Fact(inputs, "query", "") == "refused";
        var rows = new List<CloudRow>
        {
            new("reporters", "Who reports to it?", inputs.Hours.Count == 0 ? "not read yet" : $"{ResourceView.Fact(inputs, "reporter_count")} apps in the last day",
                [new($"sends to {ResourceView.Fact(inputs, "workspace_name")}", ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("workspace_href"))], "good")
            { Table = CloudTable.FromFact(ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("reporter_rows"), "App", "Type", "Items, last day") },
            new("dropped", "Is anything dropped?", refused ? "the workspace refused the query" : inputs.Buckets.Count == 0 ? "not read yet" : cappedHours > 0 ? $"yes, the cap stopped collection in {cappedHours:0} hours" : "no hour hit the cap",
                [new(refused ? "the site's identity holds Reader, which cannot run a log query; Log Analytics Reader on the workspace is what it needs" : $"sampling keeps {ResourceView.Number(sampling, "0")} % of requests · the component's own setting is {Setting(inputs)}", null)],
                // The identity holding no role that runs a query is information here, as on the registry and nightly cards.
                refused ? "info" : cappedHours > 0 ? "alarm" : "good"),
            new("cost", "What is it costing?", month is null ? "not read yet" : billable is > 0 ? $"{billable.Value.ToString("0.00", CultureInfo.InvariantCulture)} GB past {free}" : $"nothing; {month.Value.ToString("0.00", CultureInfo.InvariantCulture)} of {free}",
                [new($"{(inputs.CostMonth is { } bill ? $"${bill.ToString("0.00", CultureInfo.InvariantCulture)} this month on the bill" : "not on the bill yet")} · retention {ResourceView.Fact(inputs, "retention_days")} days", null)],
                // Paying past the free allowance is information on this card, not a fault: the cap row says whether it is too much.
                billable is > 0 ? "info" : "good"),
            new("cap", "Is the cap still right?", cap is null ? "no daily cap is set" : ingestTone == PlanTone.Alarm ? "no, today is close to it" : "yes",
                [new($"cap {(cap is { } c2 ? c2.ToString("0.###", CultureInfo.InvariantCulture) + " GB a day" : "none")} from {ResourceView.Fact(inputs, "cap_from", "neither")} · 30-day p95 {ResourceView.Number(ResourceFolding.Spread(inputs.Month, "ingest_today_gb").P95, "0.000")} GB", null)],
                cap is null ? "info" : ingestTone == PlanTone.Alarm ? "alarm" : "good"),
        };
        var readout = new CloudReadout(
            "Application Insights",
            ResourceView.Uptime(inputs, 60),
            $"read, {CloudTones.WindowWords(inputs.Window)}",
            [
                new("Today", $"{ResourceView.Number(today, "0.000")} GB", CloudTones.Of(ingestTone)),
                new("Daily cap", cap is { } c3 ? $"{c3.ToString("0.###", CultureInfo.InvariantCulture)} GB" : "none", "info") { Source = "ARM record of the component" },
                new("This month", $"{ResourceView.Number(month, "0.00")} GB", "info"),
                new("Free allowance", $"{InsightsSource.FreeGbPerMonth:0} GB", "info"),
                new("Sampling kept", sampling is { } s2 ? $"{s2.ToString("0", CultureInfo.InvariantCulture)} %" : "not read", CloudTones.Of(droppedTone)),
                new("Sampling setting", Setting(inputs), "info") { Source = "ARM record of the component" },
                new("Capped hours", cappedHours.ToString("0", CultureInfo.InvariantCulture), cappedHours > 0 ? "alarm" : "info"),
                new("Apps reporting", ResourceView.Fact(inputs, "reporter_count"), "info"),
                new("Retention", $"{ResourceView.Fact(inputs, "retention_days")} days", "info") { Source = "ARM record of the component" },
                new("Workspace", ResourceView.Fact(inputs, "workspace_name"), "info") { Source = "ARM record of the component" },
                ResourceView.Cost(inputs),
                ResourceView.LiveRead(inputs),
            ],
            $"{inputs.Availability.Reason}, one document an hour from the workspace's Usage table");
        return CardFrame.Sourced(new CloudReport(
            inputs.Window, PlanView.Windows, inputs.Availability.Available && inputs.LastHour.Count > 0,
            ResourceView.Note(inputs, ResourceView.FirstRead(inputs, "hour")),
            inputs.Now.ToUnixTimeMilliseconds(), ResourceView.Age(inputs),
            "Application Insights",
            ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("spec"),
            ResourceView.Resource(inputs),
            dials,
            [ResourceView.Spark(inputs, "ingest_today_gb", "Today, 60 h", cap), ResourceView.Spark(inputs, "sampling", "Sampling, 60 h", 100, percentReading: true)],
            ResourceView.Chart(inputs, "Ingestion and sampling", ("ingest_today_gb", "Ingested", cap, false), ("sampling", "Sampling kept", 100, true), Ingestion.Red, "teal ingestion · green sampling · gold deploys · red capped hour", marks),
            rows,
            readout), Sources);
    }

    /// <summary>The component's own sampling setting as a percentage, or "not read" when its record states none.</summary>
    private static string Setting(ResourceCardInputs inputs) =>
        ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("sampling_setting") is { } setting ? setting + " %" : "not read: the component's record states no sampling share";
}
