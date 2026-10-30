// Where the telemetry reading comes from: the token, the query, the one POST to Application
// Insights, and what to say when the service refuses. Its own file because asking a service is a
// different job from deciding how long an answer is good for, which is the reader in Telemetry.cs;
// the query itself is Telemetry.Kql.cs and the shaping is Telemetry.Shape.cs.
using System.Text.Json;

namespace TheYard.Api.Shared.Observability;

/// <summary>
/// The Application Insights component this build reports to, asked for the last hour (ADR-024).
/// It queries with the container's managed identity, the same identity that already reads the web
/// app's own record, so no key is stored anywhere for reading.
///
/// <para>Every failure comes back as a shape the card can render rather than as a throw: a
/// telemetry panel that breaks the page it reports on would be worse than useless (ADR-010), and
/// the note carries what the service actually said, because a status code alone does not name the
/// cause and finding it otherwise takes a deploy.</para>
/// </summary>
/// <param name="appId">The component's app id, which is public: it is not a key.</param>
/// <param name="clientId">The user-assigned identity the token is asked for.</param>
/// <param name="siteName">The site to count for, when the caller knows it better than the identity does.</param>
/// <param name="identity">Where this process runs: its web app's name is the role counted, its door the one a token is asked at.</param>
public sealed class TelemetrySource(string appId, string clientId, string? siteName = null, HostIdentity? identity = null)
{
    /// <summary>The one client every read shares, with a short timeout so a slow service cannot hold the Admin card.</summary>
    private static Outbound Http => OutboundHttp.Client(TimeSpan.FromSeconds(8));

    /// <summary>
    /// The site this source counts for, as Application Insights names it. Both
    /// sites send to one component, so a query with no filter counts the two
    /// together. App Service sets WEBSITE_SITE_NAME on every site, and the Azure
    /// Monitor distro's App Service resource detector makes that name the
    /// service name, which the exporter writes as cloud_RoleName. Off App
    /// Service the variable is absent, the role name is not this site's own,
    /// and the reading counts everything and says so.
    /// </summary>
    private readonly string? _siteName = TelemetryKql.SiteNameOrNull(siteName ?? identity?.SiteName);

    /// <summary>True when a component is named to ask about at all.</summary>
    public bool ComponentNamed => !string.IsNullOrWhiteSpace(appId);

    // #region ask
    /// <summary>
    /// The last hour from the component, shaped as the Admin tab shows it, or a note saying what
    /// the service said instead.
    /// </summary>
    /// <param name="cancellation">Cancelled when the poll that asked has gone.</param>
    public async Task<object> AskAsync(CancellationToken cancellation)
    {
        try
        {
            // Whichever door this host has: the metadata address on Container
            // Instances, the named endpoint on App Service (ADR: One plan, two sites).
            string token = await IdentityTokens.AcquireAsync(Http, "https://api.applicationinsights.io", clientId, identity ?? HostIdentity.None, cancellation);

            using var req = new HttpRequestMessage(HttpMethod.Post,
                $"https://api.applicationinsights.io/v1/apps/{appId}/query")
            {
                Content = JsonContent.Create(new { query = TelemetryKql.QueryFor(_siteName) }),
            };
            req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var resp = await Http.SendAsync(req, cancellation);
            string payload = await resp.Content.ReadAsStringAsync(cancellation);
            if (!resp.IsSuccessStatusCode)
            {
                return Failed($"the telemetry API answered {(int)resp.StatusCode}: {Trim(payload)}");
            }
            using var body = JsonDocument.Parse(payload);
            return TelemetryShape.Shape(body, _siteName);
        }
        catch (Exception ex)
        {
            return Failed(ex.GetType().Name);
        }
    }
    // #endregion ask

    /// <summary>
    /// Enough of the service's own words to name the cause, short enough that
    /// the Admin card stays a card. An error body from this API carries no
    /// secret: the key is in the request, never the response.
    /// </summary>
    private static string Trim(string payload) =>
        payload.Length <= 300 ? payload : payload[..300] + "...";

    /// <summary>The answer the card shows when telemetry is wired but could not be read, with the reason in words.</summary>
    private static object Failed(string reason) => new
    {
        configured = true,
        available = false,
        note = "Telemetry could not be read: " + reason,
    };
}
