using System.Text.Json;
using System.Text.Json.Nodes;

namespace JobHunt.Server.Domain.Shared.Records;

/// <summary>
/// The run's result line cut the way nightly.yml's "Tokens and wall time" step cuts it, and the one usage document the
/// site's admin endpoint takes. Nothing else from the run leaves the share.
/// </summary>
public static class UsageCut
{
    /// <summary>The lane's name on the usage card.</summary>
    public const string LaneName = "WickedAwsome, 6 AM lanes";

    /// <summary>usage.json: duration_ms, num_turns, usage and total_cost_usd.</summary>
    /// <param name="resultLine">The stream's last result line.</param>
    public static string Json(string resultLine)
    {
        JsonNode? node = JsonNode.Parse(resultLine);
        var cut = new JsonObject
        {
            ["duration_ms"] = node?["duration_ms"]?.DeepClone(),
            ["num_turns"] = node?["num_turns"]?.DeepClone(),
            ["usage"] = node?["usage"]?.DeepClone(),
            ["total_cost_usd"] = node?["total_cost_usd"]?.DeepClone(),
        };
        return cut.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
    }

    /// <summary>The document posted to /api/admin/usage.</summary>
    /// <param name="usageJson">usage.json.</param>
    /// <param name="report">The lane's report.</param>
    /// <param name="runId">The run's id.</param>
    /// <param name="slot">The run's slot: the morning's reads 06:00, and a slot run for one role the Central time it started.</param>
    /// <param name="exit">The lane's exit code as text, or null for none: the post then has no exit field.</param>
    public static string Document(string usageJson, LaneReport report, string runId, string slot = RunSlot.Hour, string? exit = null)
    {
        JsonNode? usage = JsonNode.Parse(usageJson);
        var document = new JsonObject
        {
            ["lane"] = LaneName,
            ["kind"] = "plan",
            ["model"] = report.Model,
            ["tokens_in"] = Long(usage, "input_tokens"),
            ["cache_write"] = Long(usage, "cache_creation_input_tokens"),
            ["cache_read"] = Long(usage, "cache_read_input_tokens"),
            ["tokens_out"] = Long(usage, "output_tokens"),
            ["wall_ms"] = usage?["duration_ms"]?.GetValue<long>() ?? 0,
            ["cost_usd"] = usage?["total_cost_usd"]?.DeepClone(),
            ["run"] = runId,
            ["slot"] = slot == RunSlot.Hour ? $"{RunSlot.Hour[..2]}:{RunSlot.Hour[2..]}" : RunSlot.Local(report.Started).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            ["mode"] = RunSlot.Word(report.Lane) + (report.Dry ? "-dry" : string.Empty),
            ["at"] = report.Started.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
        };
        if (exit is not null)
        {
            document["exit"] = exit;
        }
        return document.ToJsonString();
    }

    private static long Long(JsonNode? usage, string name) => usage?["usage"]?[name]?.GetValue<long>() ?? 0;
}
