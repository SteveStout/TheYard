using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace JobHunt.Server.Infrastructure.Shared.Site;

/// <summary>
/// Posts a lane's usage to the site with the key from the secrets file. A missing key or a refused post is a line in the
/// DONE note and never a failed run: the morning's work matters more than its report.
/// </summary>
/// <param name="config">The settings.</param>
/// <param name="http">The client.</param>
public sealed class SiteUsage(ServerConfig config, HttpClient http) : IUsageSink
{
    /// <inheritdoc/>
    public async Task<string> ReportAsync(string document, CancellationToken ct)
    {
        string? key = await KeyAsync(ct);
        if (string.IsNullOrWhiteSpace(key))
        {
            return "usage not reported: no key";
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{config.Site}/api/admin/usage")
        {
            Content = new StringContent(document, Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
        };
        request.Headers.Add("X-Admin-Key", key);
        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, ct);
            return response.IsSuccessStatusCode ? "usage reported" : $"usage not reported: the site answered {(int)response.StatusCode}";
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException)
        {
            return $"usage not reported: {failure.Message}";
        }
    }

    private async Task<string?> KeyAsync(CancellationToken ct)
    {
        if (!File.Exists(config.SecretsFile))
        {
            return null;
        }
        try
        {
            return JsonNode.Parse(await File.ReadAllTextAsync(config.SecretsFile, ct))?["adminKey"]?.GetValue<string>();
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
