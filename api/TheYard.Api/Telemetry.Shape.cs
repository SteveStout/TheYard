// How TelemetryReader turns Application Insights' answer into the Admin card: one table of
// columns and rows becomes the summary, the slowest routes, the exceptions and the browser errors.
// It is its own file because reading a Kusto table by column name is a job of its own, separate
// from asking the question. The reader starts in Telemetry.cs.
using System.Text.Json;

namespace TheYard.Api;

/// <summary>The shaping of query results in TelemetryReader; the type and what it is for are described in Telemetry.cs.</summary>
public sealed partial class TelemetryReader
{
    // #region shape
    /// <summary>
    /// Kusto answers with columns and rows, not objects. This turns the one
    /// table into the pieces the card renders, reading each row by the
    /// column's name rather than its position, because a query edit that adds
    /// a column would otherwise shift every value silently. A union pads the
    /// columns a row does not have, so each branch reads only its own. The
    /// site and the scope sentence say whose requests the figures are.
    /// </summary>
    private static object Shape(JsonDocument body, string? siteName)
    {
        var table = body.RootElement.GetProperty("tables")[0];
        var columns = table.GetProperty("columns").EnumerateArray()
            .Select((c, i) => (Name: c.GetProperty("name").GetString() ?? "", Index: i))
            .ToDictionary(c => c.Name, c => c.Index, StringComparer.Ordinal);

        string? Text(JsonElement row, string column) =>
            columns.TryGetValue(column, out int i) && row[i].ValueKind is not JsonValueKind.Null
                ? row[i].ToString()
                : null;

        double? Number(JsonElement row, string column) =>
            columns.TryGetValue(column, out int i) && row[i].ValueKind is JsonValueKind.Number
                ? row[i].GetDouble()
                : null;

        object? summary = null;
        object? browser = null;
        string? newest = null;
        var slowest = new List<object>();
        var exceptions = new List<object>();

        foreach (var row in table.GetProperty("rows").EnumerateArray())
        {
            switch (Text(row, "part"))
            {
                case "requests":
                    summary = new
                    {
                        total = (int)(Number(row, "total") ?? 0),
                        failed = (int)(Number(row, "failed") ?? 0),
                        p50_ms = Number(row, "p50"),
                        p95_ms = Number(row, "p95"),
                    };
                    break;
                case "slowest":
                    slowest.Add(new
                    {
                        name = Text(row, "route") ?? "",
                        calls = (int)(Number(row, "calls") ?? 0),
                        avg_ms = Number(row, "avg_ms"),
                    });
                    break;
                case "exception":
                    exceptions.Add(new
                    {
                        type = Text(row, "err_type") ?? "",
                        method = Text(row, "err_method") ?? "",
                        count = (int)(Number(row, "hits") ?? 0),
                        last_at = Text(row, "last_at") ?? "",
                    });
                    break;
                case "newest":
                    newest = Text(row, "newest_at");
                    break;
                case "browser":
                    browser = new
                    {
                        count = (int)(Number(row, "hits") ?? 0),
                        last_at = Text(row, "last_at") ?? "",
                    };
                    break;
            }
        }

        return new
        {
            configured = true,
            available = true,
            window = "the last hour",
            site = siteName,
            scope = ScopeFor(siteName),
            summary = summary ?? new { total = 0, failed = 0, p50_ms = (double?)null, p95_ms = (double?)null },
            slowest,
            exceptions,
            browser = browser ?? new { count = 0, last_at = "" },
            newest_request_at = newest,
        };
    }
    // #endregion shape
}
