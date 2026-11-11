using System.Text.Json;

namespace JobHunt.Infrastructure.Features.Jobs;

/// <summary>
/// What the site says the tagged codes did, from its two public Admin cards: /api/admin/links (each code's opens and
/// paths) and /api/admin/review (each code's highest stop on the review route), both for the last seven days. They are
/// read with the site's own self-read agent, so the read is never counted as a visit. A links card that does not
/// answer, or that says it keeps no opens, gives no visits; a review card that does not answer gives every code stop 0.
/// </summary>
/// <param name="http">The client factory.</param>
public sealed class SiteVisits(IHttpClientFactory http) : ISiteVisits
{
    /// <summary>The site the cards are read from.</summary>
    public const string Site = "https://theyard.stevenstout.biz";

    /// <inheritdoc />
    public async Task<IReadOnlyList<CodeVisit>?> ReadAsync(CancellationToken ct)
    {
        string? links = await GetAsync($"{Site}/api/admin/links?window=7d", ct);
        return links is null ? null : Parse(links, await GetAsync($"{Site}/api/admin/review?window=7d", ct));
    }

    /// <summary>
    /// The two cards' JSON as one visit a code: the links card's codes, each with its highest review stop beside it.
    /// Null when the links card is not one, or says the site keeps no opens.
    /// </summary>
    /// <param name="links">/api/admin/links's answer.</param>
    /// <param name="review">/api/admin/review's answer, or null when it was not read.</param>
    public static IReadOnlyList<CodeVisit>? Parse(string links, string? review)
    {
        try
        {
            using var card = JsonDocument.Parse(links);
            if (!card.RootElement.TryGetProperty("available", out var available) || available.ValueKind != JsonValueKind.True
                || !card.RootElement.TryGetProperty("codes", out var codes) || codes.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            var stops = Stops(review);
            var visits = new List<CodeVisit>();
            foreach (var row in codes.EnumerateArray())
            {
                if (row.TryGetProperty("code", out var code) && code.GetString() is { Length: > 0 } name
                    && row.TryGetProperty("first_ms", out var first) && row.TryGetProperty("last_ms", out var last) && row.TryGetProperty("opens", out var opens))
                {
                    var paths = row.TryGetProperty("paths", out var p) && p.ValueKind == JsonValueKind.Object
                        ? p.EnumerateObject().Where(e => e.Value.ValueKind == JsonValueKind.Number).ToDictionary(e => e.Name, e => e.Value.GetInt32(), StringComparer.Ordinal)
                        : [];
                    visits.Add(new CodeVisit(name, DateTimeOffset.FromUnixTimeMilliseconds(first.GetInt64()), DateTimeOffset.FromUnixTimeMilliseconds(last.GetInt64()), opens.GetInt32(), paths, stops.GetValueOrDefault(name)));
                }
            }
            return visits;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    private static Dictionary<string, int> Stops(string? review)
    {
        var stops = new Dictionary<string, int>(StringComparer.Ordinal);
        if (review is null)
        {
            return stops;
        }
        try
        {
            using var card = JsonDocument.Parse(review);
            if (card.RootElement.TryGetProperty("by_code", out var rows) && rows.ValueKind == JsonValueKind.Array)
            {
                foreach (var row in rows.EnumerateArray())
                {
                    if (row.TryGetProperty("code", out var code) && code.GetString() is { Length: > 0 } name && row.TryGetProperty("stop", out var stop))
                    {
                        stops[name] = stop.GetInt32();
                    }
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            stops.Clear();
        }
        return stops;
    }

    private async Task<string?> GetAsync(string url, CancellationToken ct)
    {
        try
        {
            using var client = http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("TheYard-SelfRead/1 (pounce follow-up)");
            using var answer = await client.GetAsync(url, ct);
            return answer.IsSuccessStatusCode ? await answer.Content.ReadAsStringAsync(ct) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return null;
        }
    }
}
