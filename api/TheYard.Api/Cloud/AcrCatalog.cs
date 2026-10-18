// What the container registry holds, read from the registry itself rather than from Resource
// Manager, which does not list images: the repositories, their tags, and the bytes held by
// manifests no tag points at. Read once an hour with the site's own identity, exchanged for a
// registry token the way the docker client does. Nothing here deletes anything.
using System.Net.Http.Headers;
using System.Text.Json;

namespace TheYard.Api;

/// <summary>One read of the registry's catalogue.</summary>
/// <param name="Repositories">Each repository with its tag count.</param>
/// <param name="UntaggedManifests">How many manifests no tag points at.</param>
/// <param name="UntaggedBytes">The bytes those manifests hold.</param>
public sealed record AcrHoldings(IReadOnlyList<(string Name, int Tags)> Repositories, int UntaggedManifests, long UntaggedBytes);

/// <summary>Reads a registry's repositories, tags and untagged manifests.</summary>
public static class AcrCatalog
{
    private static HttpClient Http => OutboundHttp.Client(TimeSpan.FromSeconds(20));

    /// <summary>
    /// The registry's holdings. The identity's Resource Manager token is exchanged at the registry
    /// for a refresh token, then for an access token scoped to the catalogue and each repository's
    /// metadata; AcrPull is enough for all of it.
    /// </summary>
    public static async Task<AcrHoldings> ReadAsync(string loginServer, string armToken, string tenant, CancellationToken cancellation)
    {
        string refresh = await ExchangeAsync(loginServer, armToken, tenant, cancellation);
        string catalogToken = await TokenAsync(loginServer, refresh, "registry:catalog:*", cancellation);
        using var catalog = await GetAsync($"https://{loginServer}/v2/_catalog", catalogToken, cancellation);
        var names = catalog.RootElement.TryGetProperty("repositories", out var repos) ? repos.EnumerateArray().Select(r => r.GetString()!).ToList() : [];
        var repositories = new List<(string, int)>();
        int untagged = 0;
        long bytes = 0;
        foreach (string name in names)
        {
            string token = await TokenAsync(loginServer, refresh, $"repository:{name}:metadata_read", cancellation);
            using var manifests = await GetAsync($"https://{loginServer}/acr/v1/{name}/_manifests", token, cancellation);
            int tags = 0;
            var listed = manifests.RootElement.TryGetProperty("manifests", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().ToList() : [];
            foreach (var manifest in listed)
            {
                int count = manifest.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array ? t.GetArrayLength() : 0;
                tags += count;
                if (count == 0)
                {
                    untagged++;
                    bytes += manifest.TryGetProperty("imageSize", out var size) && size.TryGetInt64(out long b) ? b : 0;
                }
            }
            repositories.Add((name, tags));
        }
        return new AcrHoldings(repositories, untagged, bytes);
    }

    private static async Task<string> ExchangeAsync(string loginServer, string armToken, string tenant, CancellationToken cancellation)
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "access_token",
            ["service"] = loginServer,
            ["tenant"] = tenant,
            ["access_token"] = armToken,
        });
        using var response = await Http.PostAsync($"https://{loginServer}/oauth2/exchange", body, cancellation);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        return json.RootElement.GetProperty("refresh_token").GetString()!;
    }

    private static async Task<string> TokenAsync(string loginServer, string refresh, string scope, CancellationToken cancellation)
    {
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["service"] = loginServer,
            ["scope"] = scope,
            ["refresh_token"] = refresh,
        });
        using var response = await Http.PostAsync($"https://{loginServer}/oauth2/token", body, cancellation);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        return json.RootElement.GetProperty("access_token").GetString()!;
    }

    private static async Task<JsonDocument> GetAsync(string url, string token, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await Http.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
    }
}
