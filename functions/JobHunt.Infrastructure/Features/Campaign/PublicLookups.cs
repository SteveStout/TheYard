using System.Globalization;
using System.Text.Json;

namespace JobHunt.Infrastructure.Features.Campaign;

/// <summary>
/// A domain's MX records over DNS over HTTPS (Google's public resolver, dns.google/resolve, keyless JSON): .NET
/// has no MX lookup of its own, and a package for one is a dependency for one question. Status 3 (no such name)
/// or an answer with no MX record is no mail; a resolver that does not answer is null, and the lead waits.
/// </summary>
public sealed class DohMxCheck(IHttpClientFactory http) : IMxCheck
{
    /// <summary>The resolver's address.</summary>
    public const string Resolver = "https://dns.google/resolve";

    /// <inheritdoc />
    public async Task<bool?> HasMxAsync(string domain, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return false;
        }
        try
        {
            using var answer = await http.CreateClient().GetAsync($"{Resolver}?name={Uri.EscapeDataString(domain)}&type=MX", ct);
            return answer.IsSuccessStatusCode ? Read(await answer.Content.ReadAsStringAsync(ct)) : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>The resolver's JSON to the answer: an MX record (type 15) is mail.</summary>
    public static bool? Read(string json)
    {
        using var doc = JsonDocument.Parse(json);
        int status = doc.RootElement.TryGetProperty("Status", out var s) ? s.GetInt32() : -1;
        if (status == 3)
        {
            return false;
        }
        if (status != 0)
        {
            return null;
        }
        return doc.RootElement.TryGetProperty("Answer", out var answers) && answers.EnumerateArray().Any(a => a.TryGetProperty("type", out var t) && t.GetInt32() == 15);
    }
}

/// <summary>
/// The three public job boards' keyless JSON (the review, F6): Greenhouse's boards API, Lever's postings API and
/// Ashby's posting API, one request a company. A board that does not answer is null and the company is left as
/// it was.
/// </summary>
public sealed class PublicBoardReader(IHttpClientFactory http) : IBoardReader
{
    /// <summary>The address a board kind and token are read from.</summary>
    public static string Address(string kind, string token) => kind switch
    {
        "greenhouse" => $"https://boards-api.greenhouse.io/v1/boards/{Uri.EscapeDataString(token)}/jobs",
        "lever" => $"https://api.lever.co/v0/postings/{Uri.EscapeDataString(token)}?mode=json",
        "ashby" => $"https://api.ashbyhq.com/posting-api/job-board/{Uri.EscapeDataString(token)}",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "greenhouse, lever or ashby"),
    };

    /// <inheritdoc />
    public async Task<IReadOnlyList<BoardPosting>?> PostingsAsync(string kind, string token, CancellationToken ct)
    {
        try
        {
            using var answer = await http.CreateClient().GetAsync(Address(kind, token), ct);
            return answer.IsSuccessStatusCode ? Read(kind, await answer.Content.ReadAsStringAsync(ct)) : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>A board's JSON to its postings: the title and the day it was posted or last updated.</summary>
    public static IReadOnlyList<BoardPosting> Read(string kind, string json)
    {
        using var doc = JsonDocument.Parse(json);
        IEnumerable<JsonElement> jobs = kind == "lever" ? doc.RootElement.EnumerateArray()
            : doc.RootElement.TryGetProperty("jobs", out var list) ? list.EnumerateArray() : [];
        return [.. jobs.Select(job => kind switch
        {
            "lever" => new BoardPosting(Text(job, "text"), "", job.TryGetProperty("createdAt", out var ms) && ms.ValueKind == JsonValueKind.Number
                ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(ms.GetInt64()).UtcDateTime) : null),
            "ashby" => new BoardPosting(Text(job, "title"), "", Day(Text(job, "publishedAt"))),
            _ => new BoardPosting(Text(job, "title"), "", Day(job.TryGetProperty("first_published", out _) ? Text(job, "first_published") : Text(job, "updated_at"))),
        })];
    }

    private static string Text(JsonElement job, string name) => job.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    private static DateOnly? Day(string text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? DateOnly.FromDateTime(at.UtcDateTime) : null;
}
