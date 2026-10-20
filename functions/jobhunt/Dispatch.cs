using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

/// <summary>
/// Fires repository_dispatch "nightly" at the repository that holds nightly.yml. Off until
/// NIGHTLY_DISPATCH is "on", which waits for step 0's measured hand run.
/// </summary>
public sealed class Dispatch(IHttpClientFactory http, ILogger<Dispatch> log)
{
    public async Task<string> RunAsync(DateTime local, CancellationToken ct)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("NIGHTLY_DISPATCH"), "on", StringComparison.OrdinalIgnoreCase))
        {
            return "dispatch off (NIGHTLY_DISPATCH is not on)";
        }
        string? token = Environment.GetEnvironmentVariable("NIGHTLY_GITHUB_TOKEN");
        string repo = Environment.GetEnvironmentVariable("NIGHTLY_REPO") ?? "SteveStout/TheYard";
        if (string.IsNullOrEmpty(token)) { return "dispatch blocked: NIGHTLY_GITHUB_TOKEN is not set"; }

        using var client = http.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{repo}/dispatches")
        {
            Content = JsonContent.Create(new { event_type = "nightly", client_payload = new { date = local.ToString("yyyy-MM-dd") } }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("jobhunt-nightly", "1.0"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await client.SendAsync(request, ct);
        string line = $"dispatch {(int)response.StatusCode}";
        log.LogInformation("{Line}", line);
        return line;
    }
}
