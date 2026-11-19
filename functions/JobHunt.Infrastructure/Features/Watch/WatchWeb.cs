using System.Net.Http.Headers;
using System.Text.Json;
using JobHunt.Application.Features.Watch;

namespace JobHunt.Infrastructure.Features.Watch;

/// <summary>
/// The watch's GETs, twenty seconds each, with a User-Agent GitHub's API asks for and no credential: the site's card
/// routes are public and so is the repository, whose unauthenticated limit (60 an hour) a tick stays well under.
/// </summary>
/// <param name="http">The client factory.</param>
public sealed class WatchWeb(IHttpClientFactory http) : IWatchWeb
{
    /// <inheritdoc />
    public async Task<JsonDocument?> GetAsync(string url, CancellationToken ct)
    {
        try
        {
            using var client = http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("theyard-watch", "1"));
            request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
            using var response = await client.SendAsync(request, ct);
            return response.IsSuccessStatusCode ? await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct) : null;
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or JsonException) && !ct.IsCancellationRequested)
        {
            return null;
        }
    }
}
