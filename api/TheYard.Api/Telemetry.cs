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
public sealed class TelemetryReader(string appId, string clientId, bool enabled, string? siteName = null)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>
    /// The site this reader counts for, as Application Insights names it. Both
    /// sites send to one component, so a query with no filter counts the two
    /// together. App Service sets WEBSITE_SITE_NAME on every site, and the Azure
    /// Monitor distro's App Service resource detector makes that name the
    /// service name, which the exporter writes as cloud_RoleName. Off App
    /// Service the variable is absent, the role name is not this site's own,
    /// and the reader counts everything and says so.
    /// </summary>
    private readonly string? _siteName = SiteNameOrNull(siteName ?? Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME"));

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
    /// One query, five answers, so the Admin card costs a single round trip:
    /// how the last hour's requests went, the slowest routes, the exceptions,
    /// how many browser errors arrived, and the newest request of the last day.
    /// Several questions in several round trips would be several chances to
    /// time out.
    ///
    /// Three things here are not stylistic. `part` labels each block because
    /// `kind` is a reserved word and a query using it does not parse. The
    /// labels sit in `extend` rather than `summarize`, because summarize takes
    /// aggregations only. And `success` is compared as text because the classic
    /// Application Insights schema stores it as the string "True" while the
    /// workspace schema stores a bool; comparing it to a bool is a 400 in one
    /// of the two (ADR: Telemetry that outlives the container, second pass).
    ///
    /// Every table is filtered to this site's cloud_RoleName when the site's
    /// name is known, because both sites send to the same component; `site_role` is
    /// empty when it is not known, and the filter then lets every row through.
    /// Counts are sum(itemCount) rather than count(): the distro samples traces
    /// by default, and a kept row stands for itemCount requests.
    ///
    /// The newest request of the last day is how the card tells an empty hour
    /// from a quiet site: a component at its daily data cap takes nothing more
    /// until midnight UTC, and the card would otherwise read that as no traffic.
    /// `newest_at` and not `last`: `last` does not parse.
    /// </summary>
    private const string Query = """
        let lookback = 1h;
        let site_role = "__SITE__";
        let summary = requests
            | where timestamp > ago(lookback)
            | where isempty(site_role) or cloud_RoleName =~ site_role
            | summarize total = sum(itemCount), failed = sumif(itemCount, tostring(success) !in ("True", "true")),
                        p50 = percentile(duration, 50), p95 = percentile(duration, 95)
            | extend part = "requests", p50 = round(p50, 1), p95 = round(p95, 1)
            | project part, total, failed, p50, p95;
        let slowest = requests
            | where timestamp > ago(lookback)
            | where isempty(site_role) or cloud_RoleName =~ site_role
            | summarize calls = sum(itemCount), avg_ms = avg(duration) by route = name
            | top 5 by avg_ms desc
            | extend part = "slowest", avg_ms = round(avg_ms, 1)
            | project part, route, calls, avg_ms;
        let failures = exceptions
            | where timestamp > ago(lookback)
            | where isempty(site_role) or cloud_RoleName =~ site_role
            | extend err_type = tostring(type), err_method = tostring(method)
            | summarize hits = sum(itemCount), last_at = max(timestamp) by err_type, err_method
            | top 5 by last_at desc
            | extend part = "exception"
            | project part, err_type, err_method, hits, last_at;
        let browser = traces
            | where timestamp > ago(lookback)
            | where isempty(site_role) or cloud_RoleName =~ site_role
            | where message startswith "Browser error on"
            | summarize hits = sum(itemCount), last_at = max(timestamp)
            | extend part = "browser"
            | project part, hits, last_at;
        let newest = requests
            | where timestamp > ago(1d)
            | where isempty(site_role) or cloud_RoleName =~ site_role
            | summarize newest_at = max(timestamp)
            | extend part = "newest"
            | project part, newest_at;
        union summary, slowest, failures, browser, newest
        """;

    /// <summary>The query with this site's name in place of the marker; empty when the name is not known.</summary>
    private static string QueryFor(string? siteName) => Query.Replace("__SITE__", siteName ?? "", StringComparison.Ordinal);

    /// <summary>
    /// An App Service site name is letters, digits and hyphens. Anything else
    /// is not a site name, and it is never put into a query: the value goes
    /// inside a Kusto string literal, so a quote in it would end the literal.
    /// </summary>
    private static string? SiteNameOrNull(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Length <= 60 && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
            ? name
            : null;

    /// <summary>
    /// Which requests the card's figures count, in words: this site alone when
    /// its name is known, or every site that reports to the component when it
    /// is not, so a figure from both sites is never read as one site's.
    /// </summary>
    private static string ScopeFor(string? siteName) => siteName is null
        ? "Counts every site that reports to this Application Insights component: both sites share it, so these figures are both sites together."
        : $"Counts {siteName} only. The other site reports to the same Application Insights component and is left out.";
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
            // Whichever door this host has: the metadata address on Container
            // Instances, the named endpoint on App Service (ADR: One plan, two sites).
            string token = await IdentityTokens.AcquireAsync(Http, "https://api.applicationinsights.io", clientId);

            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"https://api.applicationinsights.io/v1/apps/{appId}/query")
            {
                Content = JsonContent.Create(new { query = QueryFor(_siteName) }),
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var resp = await Http.SendAsync(req);
            string payload = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                return Cache(Failed($"the telemetry API answered {(int)resp.StatusCode}: {Trim(payload)}"));
            }
            using var body = JsonDocument.Parse(payload);
            return Cache(Shape(body, _siteName));
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
