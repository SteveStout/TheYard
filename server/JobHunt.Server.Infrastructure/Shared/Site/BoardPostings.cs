using System.Text.Json;

namespace JobHunt.Server.Infrastructure.Shared.Site;

/// <summary>
/// Asks a posting's own board, through its public API, whether the posting is still up and what it is called:
/// Greenhouse's job, Ashby's job board list, Lever's posting. Anything that does not answer with the posting, in time,
/// is "not live", and the invite keeps its note without the link (invites-joblink1112).
/// </summary>
/// <param name="http">The client.</param>
public sealed class BoardPostings(HttpClient http) : IPostingCheck
{
    /// <summary>How long one board may take.</summary>
    public static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    /// <inheritdoc/>
    public async Task<string?> LiveTitleAsync(string url, CancellationToken ct)
    {
        string[] parts = url.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            return null;
        }
        string host = parts[0].ToLowerInvariant();
        string id = parts[^1];
        string? api = host switch
        {
            "job-boards.greenhouse.io" or "boards.greenhouse.io" => $"https://boards-api.greenhouse.io/v1/boards/{parts[1]}/jobs/{id}",
            "jobs.ashbyhq.com" => $"https://api.ashbyhq.com/posting-api/job-board/{parts[1]}",
            "jobs.lever.co" => $"https://api.lever.co/v0/postings/{parts[1]}/{id}",
            _ => null,
        };
        if (api is null)
        {
            return null;
        }
        using var cap = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cap.CancelAfter(Wait);
        try
        {
            using HttpResponseMessage response = await http.GetAsync(api, cap.Token);
            return response.IsSuccessStatusCode ? Title(host, id, await response.Content.ReadAsStringAsync(cap.Token)) : null;
        }
        catch (Exception failure) when (failure is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>The posting's title out of its board's answer, or null when the answer does not hold that posting.</summary>
    public static string? Title(string host, string id, string json)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (host == "jobs.ashbyhq.com")
        {
            return root.TryGetProperty("jobs", out var jobs) && jobs.ValueKind == JsonValueKind.Array
                ? jobs.EnumerateArray().Where(job => job.TryGetProperty("id", out var j) && string.Equals(j.GetString(), id, StringComparison.OrdinalIgnoreCase))
                    .Select(job => job.TryGetProperty("title", out var t) ? t.GetString() : null).FirstOrDefault()
                : null;
        }
        string field = host == "jobs.lever.co" ? "text" : "title";
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty(field, out var title) && title.ValueKind == JsonValueKind.String ? title.GetString() : null;
    }
}
