// The one door this concern asks Azure Resource Manager through: a GET with a token the caller has
// already acquired, twenty seconds of patience, and the answer as a document. Its own file because
// two types ask the same way now, the reader of the plan's readings in PlanReader.cs and the keep
// of the plan's layout in PlanLayout.cs, and a client built in two places is a pool built twice.
using System.Text.Json;

namespace TheYard.Api.Shared.PlanReadings;

/// <summary>
/// How this concern reads Azure Resource Manager: one GET against the management endpoint with a
/// bearer token, and the body parsed. It is static because there is nothing to remember between
/// reads: the token belongs to whoever asked for it and the clock belongs to the keep.
/// </summary>
internal static class PlanArm
{
    /// <summary>The management endpoint every path below is read against.</summary>
    public const string Endpoint = "https://management.azure.com";

    /// <summary>
    /// The one client every read here shares, built where every outbound client in this process is
    /// built (ADR: The onion cut into vertical slices, the socket rule). Twenty seconds, because an
    /// Activity Log read over an hour is the slowest thing this concern asks for. Public because
    /// the token the reads are made with is asked for on the same client.
    /// </summary>
    public static Outbound Http => OutboundHttp.Client(TimeSpan.FromSeconds(20));

    /// <summary>
    /// One resource read, as the document Azure answered with. Throws on anything but success,
    /// because every caller here either has a card section to blank or a refusal of its own to
    /// make, and a half-read document would be worse than either.
    /// </summary>
    /// <param name="token">A bearer token for the management endpoint, acquired by the caller.</param>
    /// <param name="pathAndQuery">The path and query to read, beginning with a slash.</param>
    /// <param name="cancellation">Cancelled when whatever asked for this reading has gone.</param>
    public static async Task<JsonDocument> GetAsync(string token, string pathAndQuery, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint + pathAndQuery);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await Http.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
    }
}
