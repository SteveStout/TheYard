using System.Text.Json;

namespace TheBlock.Api;

/// <summary>
/// Application Insights, read back (ADR-024). The API sends telemetry with the
/// Azure Monitor OpenTelemetry distro; this type is the other direction, so the
/// Admin tab can show what the running app has been reporting about itself.
/// It queries the component's own data with the container's managed identity,
/// the same identity that already reads the container group's state, so no key
/// is stored anywhere for reading.
/// </summary>
public sealed class TelemetryReader(string appId, string clientId)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private readonly object _gate = new();
    private object? _cached;
    private DateTimeOffset _cachedAt;

    /// <summary>True when the component id was configured; false locally and in tests.</summary>
    public bool Configured => !string.IsNullOrWhiteSpace(appId);

    // #region kql
    /// <summary>
    /// One query, three answers, so the Admin card costs a single round trip:
    /// how many requests the last hour carried and how they went, the slowest
    /// routes, and the exceptions. Kusto's `union` with a `kind` column is the
    /// cheapest way to ask three questions at once; the alternative is three
    /// calls and three chances to time out.
    /// </summary>
    private const string Query = """
        let window = 1h;
        let requests_summary = requests
            | where timestamp > ago(window)
            | summarize kind = "requests", total = count(), failed = countif(success == false),
                        p50 = round(percentile(duration, 50), 1), p95 = round(percentile(duration, 95), 1)
            | project kind, total, failed, p50, p95;
        let slowest = requests
            | where timestamp > ago(window)
            | summarize calls = count(), avg_ms = round(avg(duration), 1) by name
            | top 5 by avg_ms desc
            | project kind = "slowest", name, calls, avg_ms;
        let failures = exceptions
            | where timestamp > ago(window)
            | summarize count_ = count(), last_at = max(timestamp) by type = tostring(type), method = tostring(method)
            | top 5 by last_at desc
            | project kind = "exception", type, method, count_, last_at;
        union withsource = source requests_summary, slowest, failures
        """;
    // #endregion kql

    // #region read
    /// <summary>
    /// The last hour, as the Admin tab shows it. Every failure answers with a
    /// shape the card can render rather than throwing: a telemetry panel that
    /// breaks the page it reports on would be worse than useless (ADR-010).
    /// </summary>
    public async Task<object> GetRecentAsync()
    {
        if (!Configured)
        {
            return new
            {
                configured = false,
                note = "Application Insights is not configured for this build. It is wired at deploy time from Azure; a local run reports nothing.",
            };
        }
        lock (_gate)
        {
            // A minute of cache: the Admin tab is a page someone leaves open,
            // and the query costs a token exchange plus a round trip.
            if (_cached is not null && DateTimeOffset.UtcNow - _cachedAt < TimeSpan.FromSeconds(60))
            {
                return _cached;
            }
        }
        try
        {
            using var tokenReq = new HttpRequestMessage(HttpMethod.Get,
                "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01" +
                "&resource=https%3A%2F%2Fapi.applicationinsights.io&client_id=" + clientId);
            tokenReq.Headers.Add("Metadata", "true");
            using var tokenResp = await Http.SendAsync(tokenReq);
            tokenResp.EnsureSuccessStatusCode();
            using var tokenJson = JsonDocument.Parse(await tokenResp.Content.ReadAsStringAsync());
            string token = tokenJson.RootElement.GetProperty("access_token").GetString()!;

            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"https://api.applicationinsights.io/v1/apps/{appId}/query")
            {
                Content = JsonContent.Create(new { query = Query }),
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var resp = await Http.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                return Failed($"the telemetry API answered {(int)resp.StatusCode}");
            }
            using var body = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var state = Shape(body);
            lock (_gate)
            {
                _cached = state;
                _cachedAt = DateTimeOffset.UtcNow;
            }
            return state;
        }
        catch (Exception ex)
        {
            return Failed(ex.GetType().Name);
        }
    }
    // #endregion read

    private static object Failed(string reason) => new
    {
        configured = true,
        available = false,
        note = "Telemetry could not be read: " + reason,
    };

    // #region shape
    /// <summary>
    /// Kusto answers with columns and rows, not objects. This turns the one
    /// table into the three pieces the card renders, reading each row by the
    /// column's name rather than its position, because a query edit that adds
    /// a column would otherwise shift every value silently.
    /// </summary>
    private static object Shape(JsonDocument body)
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
        var slowest = new List<object>();
        var exceptions = new List<object>();

        foreach (var row in table.GetProperty("rows").EnumerateArray())
        {
            switch (Text(row, "kind"))
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
                        name = Text(row, "name") ?? "",
                        calls = (int)(Number(row, "calls") ?? 0),
                        avg_ms = Number(row, "avg_ms"),
                    });
                    break;
                case "exception":
                    exceptions.Add(new
                    {
                        type = Text(row, "type") ?? "",
                        method = Text(row, "method") ?? "",
                        count = (int)(Number(row, "count_") ?? 0),
                        last_at = Text(row, "last_at") ?? "",
                    });
                    break;
            }
        }

        return new
        {
            configured = true,
            available = true,
            window = "the last hour",
            summary = summary ?? new { total = 0, failed = 0, p50_ms = (double?)null, p95_ms = (double?)null },
            slowest,
            exceptions,
        };
    }
    // #endregion shape
}
