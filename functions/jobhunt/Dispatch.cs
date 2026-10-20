using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

/// <summary>
/// Fires repository_dispatch at the repository that holds nightly.yml: "nightly" for the 4:00 AM
/// reply lane (on when NIGHTLY_DISPATCH is "on") and "bookkeeping" for the four-hour records run
/// (on when BOOKKEEPING_DISPATCH is "on", which waits for the side-by-side model test).
/// </summary>
public sealed class Dispatch(IHttpClientFactory http, ILogger<Dispatch> log)
{
    public async Task<string> RunAsync(string eventType, DateTime local, CancellationToken ct)
    {
        string gate = eventType == CentralClock.Bookkeeping ? "BOOKKEEPING_DISPATCH" : "NIGHTLY_DISPATCH";
        if (!string.Equals(Environment.GetEnvironmentVariable(gate), "on", StringComparison.OrdinalIgnoreCase))
        {
            log.LogInformation("{Event} not dispatched: {Gate} is not on", eventType, gate);
            return $"dispatch off ({gate} is not on)";
        }
        string? token = Environment.GetEnvironmentVariable("NIGHTLY_GITHUB_TOKEN");
        string repo = Environment.GetEnvironmentVariable("NIGHTLY_REPO") ?? "SteveStout/TheYard";
        if (string.IsNullOrEmpty(token)) { return "dispatch blocked: NIGHTLY_GITHUB_TOKEN is not set"; }

        using var client = http.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{repo}/dispatches")
        {
            Content = JsonContent.Create(new { event_type = eventType, client_payload = new { date = local.ToString("yyyy-MM-dd"), slot = local.ToString("HHmm") } }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("jobhunt-nightly", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await client.SendAsync(request, ct);
        string line = $"dispatch {eventType} {(int)response.StatusCode}";
        log.LogInformation("{Line}", line);
        return line;
    }
}
