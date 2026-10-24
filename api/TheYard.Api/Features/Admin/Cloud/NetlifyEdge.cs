// The Netlify edge, read from Netlify rather than Azure: this cycle's production deploys and
// bandwidth, which Netlify bills in credits, the certificate it answers with, and a small hourly
// sample of how often it serves a page from its cache. The token is a GitHub secret carried as an
// app setting, never in the repository; without one the card says so and reads nothing.
using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TheYard.Api.Features.Admin.Cloud;

// #region netlify-edge
/// <summary>Reads the edge's own figures from Netlify's API, and samples its cache once an hour.</summary>
/// <param name="bundleFile">One hashed bundle file this build serves, read from the built page at startup
/// (<see cref="BundleFileOf"/>); null on a machine with no build, and the sample says it skipped it.</param>
public sealed partial class NetlifyEdge(string? token, string siteName, Uri publicSite, string? bundleFile = null)
{
    private static Outbound Http => OutboundHttp.Client(TimeSpan.FromSeconds(20));

    /// <summary>What each meter costs in credits on the free plan, as Netlify publishes them.</summary>
    public const double CreditsPerDeploy = 15, CreditsPerGb = 20, FreeCredits = 300;

    /// <summary>
    /// The addresses the hourly sample scores beside a bundle file: each one the app tells the edge it
    /// may keep (EdgeKeeps, CatalogueReads), so zero means the edge is broken and anything else is a
    /// real number. The page and /api/version are never scored: the app tells the edge not to keep them.
    /// </summary>
    public static readonly string[] Sampled = ["/og.png", "/api/facets", "/api/docs/readme"];

    /// <summary>What this process scores each hour: its bundle file when it has one, then <see cref="Sampled"/>.</summary>
    public IReadOnlyList<string> Sample => bundleFile is { } file ? [file, .. Sampled] : Sampled;

    /// <summary>The first hashed script the built page names, or null when there is no built page.</summary>
    public static string? BundleFileOf(string indexHtml) =>
        File.Exists(indexHtml) && BundleScript().Match(File.ReadAllText(indexHtml)) is { Success: true } found ? found.Value : null;

    [GeneratedRegex(@"/assets/[A-Za-z0-9._-]+\.js")]
    private static partial Regex BundleScript();

    /// <summary>The addresses whose answerer the card names: a page and the API. Neither may be kept, so neither is scored.</summary>
    public static readonly (string Path, string Label)[] Answerers = [("/", "the page"), ("/api/version", "/api")];

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
            // Read by kind, because a field Netlify leaves null would throw on a typed read.
            usedGb = double.TryParse(AzureMetrics.Text(bandwidth.RootElement, "used"), NumberStyles.Float, CultureInfo.InvariantCulture, out double bytes) ? bytes / 1024 / 1024 / 1024 : null;
            if (DateTimeOffset.TryParse(AzureMetrics.Text(bandwidth.RootElement, "period_start_date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var s))
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
        int count = deploys.RootElement.ValueKind != JsonValueKind.Array ? 0 : deploys.RootElement.EnumerateArray().Count(d => AzureMetrics.Text(d, "state") == "ready"
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
        using var deploys = await GetAsync($"/api/v1/sites/{id}/deploys?per_page=20", cancellation);
        var (log, live, skipped, failed) = DeployLog(deploys.RootElement);
        string? answeredBy, statuses;
        (_cacheHit, statuses) = await SampleAsync(cancellation);
        answeredBy = await AnsweredAsync(cancellation);
        return new ResourceHour(hour, id, null, new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["token"] = "set",
            ["domain"] = AzureMetrics.Text(site.RootElement, "custom_domain"),
            ["cert_names"] = ssl.RootElement.ValueKind == JsonValueKind.Object && ssl.RootElement.TryGetProperty("domains", out var domains) && domains.ValueKind == JsonValueKind.Array ? string.Join(", ", domains.EnumerateArray().Select(d => d.ValueKind == JsonValueKind.String ? d.GetString() : d.GetRawText())) : null,
            ["cert_expires"] = AzureMetrics.Text(ssl.RootElement, "expires_at"),
            ["plan"] = AzureMetrics.Text(site.RootElement, "plan"),
            ["admin_url"] = AzureMetrics.Text(site.RootElement, "admin_url"),
            ["deploy_log"] = log,
            ["deploy_rows"] = DeployRows(deploys.RootElement),
            ["deploys_live"] = live.ToString(CultureInfo.InvariantCulture),
            ["deploys_skipped"] = skipped.ToString(CultureInfo.InvariantCulture),
            ["deploys_failed"] = failed.ToString(CultureInfo.InvariantCulture),
            ["answered_by"] = answeredBy,
            ["sampled"] = string.Join(", ", Sample),
            ["sample_note"] = bundleFile is null ? "no built page here, so no bundle file is sampled" : null,
            ["cache_status"] = statuses,
        }, []);
    }

    /// <summary>
    /// What a deploy came to, in the card's words: live when Netlify published it, skipped when the
    /// repository's ignore rule found no change under edge/ (Netlify files those as errors with the
    /// reason "Canceled build due to no content change"), failed for any other error, and building
    /// while it runs. The card never calls a skipped build failed.
    /// </summary>
    public static string Outcome(JsonElement deploy)
    {
        string? state = AzureMetrics.Text(deploy, "state");
        string reason = AzureMetrics.Text(deploy, "error_message") ?? "";
        return state switch
        {
            "ready" => "live",
            "skipped" => "skipped",
            "error" when reason.Contains("no content change", StringComparison.OrdinalIgnoreCase) || reason.Contains("ignore", StringComparison.OrdinalIgnoreCase) => "skipped",
            "error" or "rejected" => "failed",
            _ => "building",
        };
    }

    /// <summary>The deploy log the card prints: the newest few, each with its time and what it came to.</summary>
    public static (string Log, int Live, int Skipped, int Failed) DeployLog(JsonElement deploys, int newest = 5)
    {
        var listed = deploys.ValueKind == JsonValueKind.Array ? deploys.EnumerateArray().ToList() : [];
        var outcomes = listed.Select(d => (At: AzureMetrics.Text(d, "created_at"), Outcome: Outcome(d))).ToList();
        string log = string.Join(" · ", outcomes.Take(newest).Select(o =>
            (DateTimeOffset.TryParse(o.At, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at.UtcDateTime.ToString("MMM d HH:mm", CultureInfo.InvariantCulture) + " UTC " : "")
            + (o.Outcome == "skipped" ? "skipped, no edge change" : o.Outcome)));
        return (outcomes.Count == 0 ? "no deploys listed" : log, outcomes.Count(o => o.Outcome == "live"), outcomes.Count(o => o.Outcome == "skipped"), outcomes.Count(o => o.Outcome == "failed"));
    }

    /// <summary>The newest deploys as table rows: when, what kind of thing, and what it came to.</summary>
    public static string DeployRows(JsonElement deploys, int newest = 8) =>
        CloudTable.ToFact((deploys.ValueKind == JsonValueKind.Array ? deploys.EnumerateArray().ToList() : []).Take(newest).Select(d => new[]
        {
            DateTimeOffset.TryParse(AzureMetrics.Text(d, "created_at"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at.UtcDateTime.ToString("MMM d HH:mm", CultureInfo.InvariantCulture) + " UTC" : "",
            AzureMetrics.Text(d, "context") == "production" ? "Production deploy" : "Deploy",
            Outcome(d) == "skipped" ? "skipped, no edge change" : Outcome(d),
        }));

    /// <summary>
    /// Asks the edge for each address it may keep, twice, and counts the second answers its cache
    /// served: the first ask fills the copy when the edge has none, and the second reads whether it
    /// kept it. One ask scored a deploy's purge as a broken edge, because the first sample runs as the
    /// container starts, before the warm-up has reached the edge (ADR: One frame for every resource,
    /// the addendum on asking twice). Each address's second Cache-Status is kept beside the share, so
    /// a low number names where it came from.
    /// </summary>
    private async Task<(double? Hit, string? Statuses)> SampleAsync(CancellationToken cancellation)
    {
        int asked = 0, hits = 0;
        var statuses = new List<string>();
        foreach (string path in Sample)
        {
            try
            {
                (await Http.GetAsync(new Uri(publicSite, path), HttpCompletionOption.ResponseHeadersRead, cancellation)).Dispose();
                using var response = await Http.GetAsync(new Uri(publicSite, path), HttpCompletionOption.ResponseHeadersRead, cancellation);
                asked++;
                string status = response.Headers.TryGetValues("Cache-Status", out var values) ? string.Join(';', values) : "";
                hits += status.Contains("hit", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                statuses.Add($"{path} {(status.Length == 0 ? "no Cache-Status" : status)}");
            }
            catch (HttpRequestException)
            {
                // An address that did not answer is left out of the share rather than counted as a miss.
                statuses.Add($"{path} did not answer");
            }
        }
        return (asked == 0 ? null : Math.Round(100.0 * hits / asked, 1), statuses.Count == 0 ? null : string.Join(" · ", statuses));
    }

    /// <summary>Reads which web app answered the page and the API from the header each names itself in; never scored.</summary>
    private async Task<string?> AnsweredAsync(CancellationToken cancellation)
    {
        var answered = new List<string>();
        foreach (var (path, label) in Answerers)
        {
            try
            {
                using var response = await Http.GetAsync(new Uri(publicSite, path), HttpCompletionOption.ResponseHeadersRead, cancellation);
                answered.Add(AnsweredBy(label, response.Headers.TryGetValues(EdgeKeeps.SiteHeader, out var names) ? names.FirstOrDefault() : null));
            }
            catch (HttpRequestException)
            {
                // Not answered: the row names who answered the rest.
            }
        }
        return answered.Count == 0 ? null : string.Join(" · ", answered);
    }

    /// <summary>
    /// The share of an hour's sample the edge served from its cache, read back from the per-address
    /// statuses it wrote ("path status · path status"): an address that did not answer is left out,
    /// one with no Cache-Status counts as a miss. Null when nothing was scored.
    /// </summary>
    public static (double? Share, int Scored) ShareOf(string? statuses)
    {
        if (string.IsNullOrWhiteSpace(statuses))
        {
            return (null, 0);
        }
        var scored = statuses.Split(" · ", StringSplitOptions.RemoveEmptyEntries)
            .Where(entry => !entry.EndsWith(" did not answer", StringComparison.Ordinal))
            .ToList();
        int hits = scored.Count(entry => entry.Contains("hit", StringComparison.OrdinalIgnoreCase));
        return (scored.Count == 0 ? null : Math.Round(100.0 * hits / scored.Count, 1), scored.Count);
    }

    /// <summary>One sampled answer in words: who answered it, or that it named nobody.</summary>
    public static string AnsweredBy(string label, string? site) =>
        site is { Length: > 0 } ? $"{label} answered by {site}" : $"{label} answered without naming its app";

    private async Task<(string Id, string Account)> SiteAsync(CancellationToken cancellation)
    {
        if (_site is { } known)
        {
            return known;
        }
        // Every site the token can see, matched here: the API's name filter answered nothing for a
        // site that exists (read live when the card was built), so the list is read whole.
        using var sites = await GetAsync("/api/v1/sites?filter=all&per_page=100", cancellation);
        var listed = sites.RootElement.ValueKind == JsonValueKind.Array ? sites.RootElement.EnumerateArray().ToList() : [];
        // Matched by its name or its default subdomain; a token that cannot see it gets the
        // sentence, not an exception.
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
