// Application Insights, read back for the Admin tab. This file holds the reader itself: its
// settings, its cache and the one read the card calls. The other parts live beside it:
// Telemetry.Kql.cs (the query and the site filter it carries) and Telemetry.Shape.cs (turning the
// query's table into the pieces the card renders).
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
public sealed partial class TelemetryReader(string appId, string clientId, bool enabled, string? siteName = null)
{
    /// <summary>The one client every read shares, with a short timeout so a slow service cannot hold the Admin card.</summary>
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

    /// <summary>The lock that guards the cached answer, since several requests can read and fill it at once.</summary>
    private readonly object _gate = new();

    /// <summary>The last answer, success or failure, served again until it is a minute old.</summary>
    private object? _cached;

    /// <summary>When the cached answer was read.</summary>
    private DateTimeOffset _cachedAt;

    /// <summary>
    /// True only where telemetry is actually wired, which is the deployed
    /// container. The component's app id is not the test: it has a default,
    /// so reading it alone would make this always true, the unconfigured path
    /// dead code, and every local request an eight-second wait on a metadata
    /// endpoint that only exists in Azure.
    /// </summary>
    public bool Configured => enabled && !string.IsNullOrWhiteSpace(appId);

    // #region read
    /// <summary>
    /// The last hour, as the Admin tab shows it. Every failure answers with a
    /// shape the card can render rather than throwing: a telemetry panel that
    /// breaks the page it reports on would be worse than useless (ADR-010).
    /// The failure note carries what the service actually said, because a
    /// status code alone does not name the cause, and finding it otherwise
    /// takes a deploy.
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

    /// <summary>The answer the card shows when telemetry is wired but could not be read, with the reason in words.</summary>
    private static object Failed(string reason) => new
    {
        configured = true,
        available = false,
        note = "Telemetry could not be read: " + reason,
    };
}
