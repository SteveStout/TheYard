using System.Text.Json;

namespace TheYard.Api;

// #region identity-token
/// <summary>
/// A managed identity token, asked for by hand, from whichever door this host
/// has (ADR: One plan, two sites). The Azure SDK finds the door on its own, and
/// the two stores and the mail go through it. The two readings this
/// application takes without the SDK, its own Azure resource and its own
/// telemetry, asked the instance metadata address directly, which is the door
/// Container Instances has and App Service does not.
///
/// <para>App Service names its door in two environment variables: an endpoint
/// on the loopback, and a header value that proves the caller is inside the
/// sandbox. When both are present they are used; otherwise the metadata
/// address is, exactly as before. The request is built by a pure function so a
/// test can hold both shapes without either host existing.</para>
/// </summary>
public static class IdentityTokens
{
    public const string MetadataAddress = "http://169.254.169.254/metadata/identity/oauth2/token";

    /// <summary>The request for a token for <paramref name="resource"/>, as the user-assigned identity <paramref name="clientId"/> names.</summary>
    public static HttpRequestMessage RequestFor(string resource, string clientId, string? endpoint, string? header)
    {
        string query = "resource=" + Uri.EscapeDataString(resource) + "&client_id=" + Uri.EscapeDataString(clientId);
        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(header))
        {
            // App Service. The version is the one its documentation names for
            // this endpoint, and the header is the platform's own secret for
            // this sandbox: it never leaves the process that read it.
            var onPlan = new HttpRequestMessage(HttpMethod.Get, endpoint + "?api-version=2019-08-01&" + query);
            onPlan.Headers.Add("X-IDENTITY-HEADER", header);
            return onPlan;
        }

        var metadata = new HttpRequestMessage(HttpMethod.Get, MetadataAddress + "?api-version=2018-02-01&" + query);
        metadata.Headers.Add("Metadata", "true");
        return metadata;
    }

    /// <summary>True when the platform has named an identity endpoint, which is how this process knows it is on App Service.</summary>
    public static bool OnAppService =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT"))
        && !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("IDENTITY_HEADER"));

    /// <summary>Asks this host's door for a token. Throws what the HTTP call throws; both callers already answer a failure with a shape rather than an exception.</summary>
    public static async Task<string> AcquireAsync(HttpClient http, string resource, string clientId)
    {
        using var request = RequestFor(
            resource,
            clientId,
            Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT"),
            Environment.GetEnvironmentVariable("IDENTITY_HEADER"));
        using var response = await http.SendAsync(request);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("access_token").GetString()!;
    }
}
// #endregion identity-token
