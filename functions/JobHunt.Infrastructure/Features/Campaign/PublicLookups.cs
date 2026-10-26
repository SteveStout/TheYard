using System.Globalization;
using System.Text.Json;

namespace JobHunt.Infrastructure.Features.Campaign;

/// <summary>
/// A domain's MX records over DNS over HTTPS (Google's public resolver, dns.google/resolve, keyless JSON): .NET
/// has no MX lookup of its own, and a package for one is a dependency for one question. Status 3 (no such name)
/// or an answer with no MX record is no mail; a resolver that does not answer is null, and the lead waits.
/// Not answering covers a network error, a wait past <see cref="Timeout"/> and a body that is not the JSON
/// expected, so one odd answer never fails the pass that asked.
/// </summary>
public sealed class DohMxCheck(IHttpClientFactory http) : IMxCheck
{
    /// <summary>The resolver's address.</summary>
    public const string Resolver = "https://dns.google/resolve";

    /// <summary>How long one question may wait; the tests set it shorter.</summary>
    public TimeSpan Timeout { get; init; } = PublicBoardReader.RequestCap;

    /// <inheritdoc />
    public async Task<bool?> HasMxAsync(string domain, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return false;
        }
        using var cap = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cap.CancelAfter(Timeout);
        try
        {
            using var answer = await http.CreateClient().GetAsync($"{Resolver}?name={Uri.EscapeDataString(domain)}&type=MX", cap.Token);
            return answer.IsSuccessStatusCode ? Read(await answer.Content.ReadAsStringAsync(cap.Token)) : null;
        }
        catch (Exception ex) when (PublicBoardReader.DidNotAnswer(ex, ct))
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
/// it was. Not answering covers a network error, a wait past <see cref="Timeout"/> and a body that is not the
/// board's JSON (a page of HTML, an error object), so one slow or odd board never stops the scan of the rest.
/// </summary>
public sealed class PublicBoardReader(IHttpClientFactory http) : IBoardReader
{
    /// <summary>
    /// The longest one public request may take. HttpClient's own limit is 100 seconds, and the scan reads about a
    /// hundred boards one after another inside the host's ten-minute limit.
    /// </summary>
    public static readonly TimeSpan RequestCap = TimeSpan.FromSeconds(15);

    /// <summary>How long one board may take to answer; the tests set it shorter.</summary>
    public TimeSpan Timeout { get; init; } = RequestCap;

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
        using var cap = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cap.CancelAfter(Timeout);
        try
        {
            using var answer = await http.CreateClient().GetAsync(Address(kind, token), cap.Token);
            return answer.IsSuccessStatusCode ? Read(kind, await answer.Content.ReadAsStringAsync(cap.Token)) : null;
        }
        catch (Exception ex) when (DidNotAnswer(ex, ct))
        {
            return null;
        }
    }

    /// <summary>
    /// Whether a failed request means the service did not answer, which the ports report as null: a network
    /// error, a timeout (HttpClient reports its own and the cap's as a cancellation) while the caller still wants
    /// the answer, or JSON that does not parse or has another shape. A cancellation the caller asked for is not
    /// one of them and goes on up.
    /// </summary>
    internal static bool DidNotAnswer(Exception ex, CancellationToken caller) => ex switch
    {
        HttpRequestException or JsonException or InvalidOperationException or FormatException => true,
        OperationCanceledException => !caller.IsCancellationRequested,
        _ => false,
    };

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
