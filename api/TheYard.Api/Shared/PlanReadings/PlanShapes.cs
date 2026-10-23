// Azure's answers about the plan, turned into the application's shapes: the metrics response
// into series by minute, and the Activity Log into classified events. Public and static so a test
// can hand them Azure's own JSON, the way the Azure card's shapes are held.
using System.Globalization;
using System.Text.Json;
using TheYard.Application;

namespace TheYard.Api.Shared.PlanReadings;

// #region plan-shapes
/// <summary>Azure Monitor's and the Activity Log's JSON, as series and events.</summary>
public static class PlanShapes
{
    /// <summary>True for an aggregation whose split series add up: a count or a total.</summary>
    /// <param name="aggregation">The aggregation's name as Azure Monitor writes it.</param>
    public static bool Adds(string aggregation) => aggregation is "count" or "total";

    /// <summary>
    /// The metrics answer as metric name to minute to value, each metric read with the aggregation
    /// it was asked with. A point Azure answered with no value is left out, not read as zero.
    /// </summary>
    public static Dictionary<string, Dictionary<DateTimeOffset, double>> Series(JsonElement root, (string Name, string Aggregation)[] metrics)
    {
        var aggregationOf = metrics.ToDictionary(metric => metric.Name, metric => metric.Aggregation, StringComparer.OrdinalIgnoreCase);
        var series = new Dictionary<string, Dictionary<DateTimeOffset, double>>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("value", out var values))
        {
            return series;
        }
        foreach (var metric in values.EnumerateArray())
        {
            string name = metric.GetProperty("name").GetProperty("value").GetString() ?? "";
            if (!aggregationOf.TryGetValue(name, out string? aggregation))
            {
                continue;
            }
            var points = new Dictionary<DateTimeOffset, double>();
            foreach (var timeseries in metric.GetProperty("timeseries").EnumerateArray())
            {
                foreach (var point in timeseries.GetProperty("data").EnumerateArray())
                {
                    if (point.TryGetProperty(aggregation, out var value) && value.ValueKind == JsonValueKind.Number
                        && DateTimeOffset.TryParse(point.GetProperty("timeStamp").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
                    {
                        // A filter split into one series a dimension value: a count or a total is the
                        // sum of the series; any other aggregation keeps one series' reading, as before.
                        at = at.ToUniversalTime();
                        points[at] = Adds(aggregation) && points.TryGetValue(at, out double before) ? before + value.GetDouble() : value.GetDouble();
                    }
                }
            }
            series[name] = points;
        }
        return series;
    }

    /// <summary>
    /// The Activity Log answer as events: a restart, a scale, maintenance, a configuration write,
    /// or other. An entry with no caller was done by Azure itself, which is what makes a restart
    /// unplanned; one with a caller was asked for by somebody on the subscription, a deploy
    /// included.
    /// </summary>
    public static IReadOnlyList<PlanEvent> Events(JsonElement root, string resource)
    {
        var events = new List<PlanEvent>();
        if (!root.TryGetProperty("value", out var values))
        {
            return events;
        }
        foreach (var entry in values.EnumerateArray())
        {
            string operation = entry.TryGetProperty("operationName", out var op) && op.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";
            string title = entry.TryGetProperty("operationName", out var op2) && op2.TryGetProperty("localizedValue", out var lv) ? lv.GetString() ?? operation : operation;
            if (!DateTimeOffset.TryParse(entry.TryGetProperty("eventTimestamp", out var ts) ? ts.GetString() : null, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
            {
                continue;
            }
            // Only the entry that finished, so one operation is one event and not three.
            string status = entry.TryGetProperty("status", out var st) && st.TryGetProperty("value", out var sv) ? sv.GetString() ?? "" : "";
            if (status is not ("Succeeded" or "Failed" or "Resolved" or "Active" or ""))
            {
                continue;
            }
            bool planned = entry.TryGetProperty("caller", out var caller) && !string.IsNullOrWhiteSpace(caller.GetString());
            events.Add(new PlanEvent(at.ToUniversalTime(), KindOf(operation), title, resource, planned));
        }
        return events;
    }

    /// <summary>Which of the five kinds an operation name is.</summary>
    public static string KindOf(string operation)
    {
        string op = operation.ToLowerInvariant();
        return op.Contains("/restart/") ? "restart"
            : op.Contains("serverfarms/write") ? "scale"
            : op.Contains("resourcehealth") || op.Contains("maintenance") ? "maintenance"
            : op.Contains("/config/") || op.EndsWith("sites/write", StringComparison.Ordinal) ? "config"
            : "other";
    }
}

// #endregion plan-shapes
