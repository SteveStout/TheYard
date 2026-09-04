using System.Text.Json;

namespace TheYard.Api;

/// <summary>
/// Application Insights, read back (ADR-024). The API sends telemetry with the
/// Azure Monitor OpenTelemetry distro; this type is the other direction, so the
/// Admin tab can show what the running app has been reporting about itself.
/// It queries the component's own data with the container's managed identity,
/// the same identity that already reads the container group's state, so no key
/// is stored anywhere for reading.
/// </summary>
public sealed class TelemetryReader(string appId, string clientId, bool enabled)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private readonly object _gate = new();
    private object? _cached;
    private DateTimeOffset _cachedAt;

    /// <summary>
    /// True only where telemetry is actually wired, which is the deployed
    /// container. The component's app id is not the test: it has a default,
    /// so reading it alone made this always true, the unconfigured path dead
    /// code, and every local request an eight-second wait on a metadata
    /// endpoint that only exists in Azure.
    /// </summary>
    public bool Configured => enabled && !string.IsNullOrWhiteSpace(appId);

    // #region kql
    /// <summary>
    /// One query, four answers, so the Admin card costs a single round trip:
    /// how the last hour's requests went, the slowest routes, the exceptions,
    /// and how many browser errors arrived. Three questions in three round
    /// trips would be three chances to time out.
    ///
    /// Three things here are not stylistic. `part` labels each block because
    /// `kind` is a reserved word and a query using it does not parse. The
    /// labels sit in `extend` rather than `summarize`, because summarize takes
    /// aggregations only. And `success` is compared as text because the classic
    /// Application Insights schema stores it as the string "True" while the
    /// workspace schema stores a bool; comparing it to a bool is a 400 in one
    /// of the two. Every one of those was learned from the query API's own
    /// error message rather than guessed, after the first version shipped
    /// broken (ADR: Telemetry that outlives the container, second pass).
    /// </summary>
    private const string Query = """
        let lookback = 1h;
        let summary = requests
            | where timestamp > ago(lookback)
            | summarize total = count(), failed = countif(tostring(success) !in ("True", "true")),
                        p50 = percentile(duration, 50), p95 = percentile(duration, 95)
            | extend part = "requests", p50 = round(p50, 1), p95 = round(p95, 1)
            | project part, total, failed, p50, p95;
        let slowest = requests
            | where timestamp > ago(lookback)
            | summarize calls = count(), avg_ms = avg(duration) by route = name
            | top 5 by avg_ms desc
            | extend part = "slowest", avg_ms = round(avg_ms, 1)
            | project part, route, calls, avg_ms;
        let failures = exceptions
            | where timestamp > ago(lookback)
            | extend err_type = tostring(type), err_method = tostring(method)
            | summarize hits = count(), last_at = max(timestamp) by err_type, err_method
            | top 5 by last_at desc
            | extend part = "exception"
            | project part, err_type, err_method, hits, last_at;
        let browser = traces
            | where timestamp > ago(lookback)
            | where message startswith "Browser error on"
            | summarize hits = count(), last_at = max(timestamp)
            | extend part = "browser"
            | project part, hits, last_at;
        union summary, slowest, failures, browser
        """;
    // #endregion kql

    // #region read
    /// <summary>
    /// The last hour, as the Admin tab shows it. Every failure answers with a
    /// shape the card can render rather than throwing: a telemetry panel that
    /// breaks the page it reports on would be worse than useless (ADR-010).
    /// The failure note carries what the service actually said, because the
    /// first version reported only a status code and that cost a deploy to
    /// diagnose.
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
            string payload = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                return Cache(Failed($"the telemetry API answered {(int)resp.StatusCode}: {Trim(payload)}"));
            }
            using var body = JsonDocument.Parse(payload);
            return Cache(Shape(body));
        }
        catch (Exception ex)
        {
            return Cache(Failed(ex.GetType().Name));
        }
    }
    // #endregion read

    /// <summary>
    /// Enough of the service's own words to name the cause, short enough that
    /// the Admin card stays a card. An error body from this API carries no
    /// secret: the key is in the request, never the response.
    /// </summary>
    private static string Trim(string payload) =>
        payload.Length <= 300 ? payload : payload[..300] + "...";

    /// <summary>
    /// Failures are cached like successes. Without this, a component that has
    /// gone unreachable costs a full timeout on every request to a public,
    /// unauthenticated endpoint, and the answer is the same every time anyway.
    /// </summary>
    private object Cache(object state)
    {
        lock (_gate)
        {
            _cached = state;
            _cachedAt = DateTimeOffset.UtcNow;
        }
        return state;
    }

    private static object Failed(string reason) => new
    {
        configured = true,
        available = false,
        note = "Telemetry could not be read: " + reason,
    };

    // #region shape
    /// <summary>
    /// Kusto answers with columns and rows, not objects. This turns the one
    /// table into the four pieces the card renders, reading each row by the
    /// column's name rather than its position, because a query edit that adds
    /// a column would otherwise shift every value silently. A union pads the
    /// columns a row does not have, so each branch reads only its own.
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
        object? browser = null;
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
            summary = summary ?? new { total = 0, failed = 0, p50_ms = (double?)null, p95_ms = (double?)null },
            slowest,
            exceptions,
            browser = browser ?? new { count = 0, last_at = "" },
        };
    }
    // #endregion shape
}
