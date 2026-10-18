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
                [new("sends to ", null), new(ResourceView.Fact(inputs, "workspace_name"), ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("workspace_href"))], "good")
            { Table = CloudTable.FromFact(ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("reporter_rows"), "App", "Type", "Items, last day") },
            new("dropped", "Is anything dropped?", refused ? "the workspace refused the query" : inputs.Buckets.Count == 0 ? "not read yet" : cappedHours > 0 ? $"yes, the cap stopped collection in {cappedHours:0} hours" : "no hour hit the cap",
                [new(refused ? "the site's identity holds Reader, which cannot run a log query; Log Analytics Reader on the workspace is what it needs" : $"sampling keeps {ResourceView.Number(sampling, "0")} % of requests · the component's own setting is {ResourceView.Fact(inputs, "sampling_setting")} %", null)],
                refused ? "warning" : cappedHours > 0 ? "alarm" : "good"),
            new("cost", "What is it costing?", month is null ? "not read yet" : billable is > 0 ? $"${(billable.Value * InsightsSource.PerGb).ToString("0.00", CultureInfo.InvariantCulture)} past the free 5 GB" : $"nothing; {month.Value.ToString("0.00", CultureInfo.InvariantCulture)} of the free 5 GB",
                [new($"{InsightsSource.PerGb.ToString("0.00", CultureInfo.InvariantCulture)} $/GB past the allowance · retention {ResourceView.Fact(inputs, "retention_days")} days", null)],
                billable is > 0 ? "warning" : "good"),
            new("cap", "Is the cap still right?", cap is null ? "no daily cap is set" : ingestTone == PlanTone.Alarm ? "no, today is close to it" : "yes",
                [new($"cap {(cap is { } c2 ? c2.ToString("0.###", CultureInfo.InvariantCulture) + " GB a day" : "none")} from {ResourceView.Fact(inputs, "cap_from", "neither")} · 30-day p95 {ResourceView.Number(ResourceFolding.Spread(inputs.Month, "ingest_today_gb").P95, "0.000")} GB", null)],
                cap is null ? "warning" : ingestTone == PlanTone.Alarm ? "alarm" : "good"),
        };
        var readout = new CloudReadout(
            "Application Insights",
            ResourceView.Uptime(inputs, 60),
            $"read, {CloudTones.WindowWords(inputs.Window)}",
            [
                new("Today", $"{ResourceView.Number(today, "0.000")} GB", CloudTones.Of(ingestTone)),
                new("Daily cap", cap is { } c3 ? $"{c3.ToString("0.###", CultureInfo.InvariantCulture)} GB" : "none", cap is null ? "warning" : "info"),
                new("This month", $"{ResourceView.Number(month, "0.00")} GB", "info"),
                new("Free allowance", $"{InsightsSource.FreeGbPerMonth:0} GB", "info"),
                new("Sampling kept", sampling is { } s2 ? $"{s2.ToString("0", CultureInfo.InvariantCulture)} %" : "not read", CloudTones.Of(droppedTone)),
                new("Sampling setting", $"{ResourceView.Fact(inputs, "sampling_setting")} %", "info"),
                new("Capped hours", cappedHours.ToString("0", CultureInfo.InvariantCulture), cappedHours > 0 ? "alarm" : "info"),
                new("Apps reporting", ResourceView.Fact(inputs, "reporter_count"), "info"),
                new("Retention", $"{ResourceView.Fact(inputs, "retention_days")} days", "info"),
                new("Workspace", ResourceView.Fact(inputs, "workspace_name"), "info"),
                ResourceView.Cost(inputs),
                ResourceView.LiveRead(inputs, hourly: true),
            ],
            $"{inputs.Availability.Reason}, one document an hour from the workspace's Usage table");
        return new CloudReport(
            inputs.Window, PlanView.Windows, inputs.Availability.Available && inputs.LastHour.Count > 0,
            ResourceView.Note(inputs, "no hour kept yet; the first is read a minute after the SQL site starts"),
            inputs.Now.ToUnixTimeMilliseconds(), ResourceView.Age(inputs),
            "Application Insights",
            ResourceView.Latest(inputs)?.Facts.GetValueOrDefault("spec"),
            ResourceView.Resource(inputs),
            dials,
            [ResourceView.Spark(inputs, "ingest_today_gb", "Today, 60 h", cap), ResourceView.Spark(inputs, "sampling", "Sampling, 60 h", 100, percentReading: true)],
            $"Normal: ingestion amber {Ingestion.Amber} %, red {Ingestion.Red} % of the cap; sampling drops amber {Dropped.Amber} %, red {Dropped.Red} %; any capped hour is red.",
            ResourceView.Chart(inputs, "Ingestion and sampling", ("ingest_today_gb", "Ingested", cap, false), ("sampling", "Sampling kept", 100, true), Ingestion.Red, "teal ingestion · green sampling · gold deploys · red capped hour", marks),
            rows,
            readout);
    }
}
