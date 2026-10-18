// The Netlify edge, read from Netlify rather than Azure: this cycle's production deploys and
// bandwidth, which Netlify bills in credits, the certificate it answers with, and a small hourly
// sample of how often it serves a page from its cache. The token is a GitHub secret carried as an
// app setting, never in the repository; without one the card says so and reads nothing.
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using TheYard.Application;

namespace TheYard.Api;

// #region netlify-edge
/// <summary>Reads the edge's own figures from Netlify's API, and samples its cache once an hour.</summary>
public sealed class NetlifyEdge(string? token, string siteName, Uri publicSite)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>What each meter costs in credits on the free plan, as Netlify publishes them.</summary>
    public const double CreditsPerDeploy = 15, CreditsPerGb = 20, FreeCredits = 300;

    /// <summary>The pages the hourly sample asks the edge for: the files every visitor's first page pulls.</summary>
    public static readonly string[] Sampled = ["/", "/favicon.svg", "/og.png", "/api/version"];

    /// <summary>True when a token is configured.</summary>
    public bool Configured => !string.IsNullOrWhiteSpace(token) && !token.StartsWith("__", StringComparison.Ordinal);

    /// <summary>The last cache sample's hit share, kept until the next hour's sample.</summary>
    private double? _cacheHit;

    /// <summary>The site's id and account, read once.</summary>
    private (string Id, string Account)? _site;

    /// <summary>
    /// One reading for the current hour: credits spent this cycle (deploys and bandwidth), the
    /// bandwidth itself, the deploys, and the last cache sample. Written as one document an hour,
    /// because Netlify's own figures move no faster than that.
    /// </summary>
    public async Task<IReadOnlyList<ResourceMinute>> MinutesAsync(DateTimeOffset now, string version, CancellationToken cancellation)
    {
        if (!Configured)
        {
            return [];
        }
        (string Id, string Account) found;
        try
        {
            found = await SiteAsync(cancellation);
        }
        catch (NetlifySiteMissingException)
        {
            return [];
        }
        var (id, account) = found;
        double? usedGb = null;
        DateTimeOffset cycleStart = new(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        try
        {
            using var bandwidth = await GetAsync($"/api/v1/accounts/{account}/bandwidth", cancellation);
            usedGb = bandwidth.RootElement.TryGetProperty("used", out var used) && used.TryGetDouble(out double bytes) ? bytes / 1024 / 1024 / 1024 : null;
            if (bandwidth.RootElement.TryGetProperty("period_start_date", out var start) && DateTimeOffset.TryParse(start.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var s))
            {
                cycleStart = s;
            }
        }
        catch (HttpRequestException)
        {
            // The account's bandwidth was not answered: credits count the deploys alone, and the
            // bandwidth reads as not read rather than as nothing.
        }
        using var deploys = await GetAsync($"/api/v1/sites/{id}/deploys?production=true&per_page=100", cancellation);
        int count = deploys.RootElement.EnumerateArray().Count(d => AzureMetrics.Text(d, "state") == "ready"
            && DateTimeOffset.TryParse(AzureMetrics.Text(d, "created_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) && at >= cycleStart);
        var values = new Dictionary<string, double?>(StringComparer.Ordinal)
        {
            ["credits"] = Math.Round(count * CreditsPerDeploy + (usedGb ?? 0) * CreditsPerGb, 1),
            ["bandwidth_gb"] = usedGb is { } gb ? Math.Round(gb, 3) : null,
            ["deploys"] = count,
            ["cache_hit"] = _cacheHit,
        };
        return [new ResourceMinute(MachineWindows.BucketOf(now, MachineGrain.Hour), values, version)];
    }

    /// <summary>The hour's facts: the certificate's names and expiry, the domain, and a fresh cache sample.</summary>
    public async Task<ResourceHour> HourAsync(DateTimeOffset hour, CancellationToken cancellation)
    {
        if (!Configured)
        {
            return new ResourceHour(hour, "", null, new Dictionary<string, string?> { ["token"] = "missing" }, []);
        }
        string id;
        try
        {
            (id, _) = await SiteAsync(cancellation);
        }
        catch (NetlifySiteMissingException missing)
        {
            return new ResourceHour(hour, "", null, new Dictionary<string, string?> { ["token"] = "set", ["site"] = missing.Message }, []);
        }
        using var site = await GetAsync($"/api/v1/sites/{id}", cancellation);
        using var ssl = await GetAsync($"/api/v1/sites/{id}/ssl", cancellation);
        _cacheHit = await SampleAsync(cancellation);
        return new ResourceHour(hour, id, null, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["token"] = "set",
            ["domain"] = AzureMetrics.Text(site.RootElement, "custom_domain"),
            ["cert_names"] = ssl.RootElement.TryGetProperty("domains", out var domains) && domains.ValueKind == JsonValueKind.Array ? string.Join(", ", domains.EnumerateArray().Select(d => d.GetString())) : null,
            ["cert_expires"] = AzureMetrics.Text(ssl.RootElement, "expires_at"),
            ["plan"] = AzureMetrics.Text(site.RootElement, "plan"),
        }, []);
    }

    /// <summary>Asks the edge for each sampled page and counts the answers its cache served.</summary>
    private async Task<double?> SampleAsync(CancellationToken cancellation)
    {
        int asked = 0, hits = 0;
        foreach (string path in Sampled)
        {
            try
            {
                using var response = await Http.GetAsync(new Uri(publicSite, path), HttpCompletionOption.ResponseHeadersRead, cancellation);
                asked++;
                string status = response.Headers.TryGetValues("Cache-Status", out var values) ? string.Join(';', values) : "";
                if (status.Contains("hit", StringComparison.OrdinalIgnoreCase))
                {
                    hits++;
                }
            }
            catch (HttpRequestException)
            {
                // A page that did not answer is left out of the share rather than counted as a miss.
            }
        }
        return asked == 0 ? null : Math.Round(100.0 * hits / asked, 1);
    }

    private async Task<(string Id, string Account)> SiteAsync(CancellationToken cancellation)
    {
        if (_site is { } known)
        {
            return known;
        }
        using var sites = await GetAsync($"/api/v1/sites?name={Uri.EscapeDataString(siteName)}&filter=all", cancellation);
        var listed = sites.RootElement.ValueKind == JsonValueKind.Array ? sites.RootElement.EnumerateArray().ToList() : [];
        // Netlify's name filter is a substring search, so the site is matched by its name or its
        // default subdomain; a token that cannot see it gets the sentence, not an exception.
        var site = listed.FirstOrDefault(s => string.Equals(AzureMetrics.Text(s, "name"), siteName, StringComparison.OrdinalIgnoreCase)
            || (AzureMetrics.Text(s, "url") ?? "").Contains("//" + siteName + ".", StringComparison.OrdinalIgnoreCase));
        if (site.ValueKind != JsonValueKind.Object)
        {
            throw new NetlifySiteMissingException($"the token sees {listed.Count} sites and none is {siteName}");
        }
        _site = (AzureMetrics.Text(site, "id") ?? "", AzureMetrics.Text(site, "account_slug") ?? AzureMetrics.Text(site, "account_id") ?? "");
        return _site.Value;
    }

    private async Task<JsonDocument> GetAsync(string path, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.netlify.com" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await Http.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
    }
}
/// <summary>The token answered, but none of the sites it can see is the edge.</summary>
public sealed class NetlifySiteMissingException(string message) : Exception(message);
// #endregion netlify-edge
