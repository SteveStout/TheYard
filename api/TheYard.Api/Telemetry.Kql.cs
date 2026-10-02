// The question TelemetryReader asks Application Insights: the one Kusto query, the site name it
// is filtered to, and the sentence that says whose requests the figures count. It is its own file
// because the query is long and has rules of its own that are worth reading on their own. The
// reader starts in Telemetry.cs.
namespace TheYard.Api;

/// <summary>The KQL queries of TelemetryReader; the type and what it is for are described in Telemetry.cs.</summary>
public sealed partial class TelemetryReader
{
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
}
