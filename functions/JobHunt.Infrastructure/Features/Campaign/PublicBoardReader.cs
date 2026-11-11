using System.Globalization;
using System.Text.Json;

namespace JobHunt.Infrastructure.Features.Campaign;

/// <summary>
/// The three public job boards' keyless JSON (the review, F6; PROMPT-JOBHUNT-POSTINGS's three feeds): Greenhouse's
/// boards API with content and pay_transparency (pay returns only with that parameter), Lever's postings API, and
/// Ashby's posting API with includeCompensation, one request a company. Each posting comes back with the board's id,
/// address, location, workplace, its description as plain text and its published band: Greenhouse's
/// pay_input_ranges in cents, Lever's salaryRange, Ashby's Salary component; a board that published no band gives
/// null, never a figure read out of the text. A board that does not answer is null and the company is left as it
/// was. Not answering covers a network error, a wait past <see cref="Timeout"/> and a body that is not the board's
/// JSON (a page of HTML, an error object), so one slow or odd board never stops the scan of the rest. Each posting
/// also carries the instant the board says it went up: Greenhouse's first_published (never updated_at), Lever's
/// createdAt in epoch milliseconds, Ashby's publishedAt. Greenhouse's list without content and pay is the fresh pass's
/// light read: about a twentieth of the bytes of the whole boards (7.8 MB against 157 MB for 112 boards).
/// </summary>
public sealed class PublicBoardReader(IHttpClientFactory http) : IBoardReader
{
    /// <summary>
    /// The longest one public request may take. HttpClient's own limit is 100 seconds, and the scan reads every
    /// board one after another inside the host's ten-minute limit.
    /// </summary>
    public static readonly TimeSpan RequestCap = TimeSpan.FromSeconds(15);

    /// <summary>The first and last instants a DateTimeOffset holds, as epoch milliseconds: the range a Lever createdAt is read in.</summary>
    private static readonly long EarliestMs = DateTimeOffset.MinValue.ToUnixTimeMilliseconds(), LatestMs = DateTimeOffset.MaxValue.ToUnixTimeMilliseconds();

    /// <summary>How long one board may take to answer; the tests set it shorter.</summary>
    public TimeSpan Timeout { get; init; } = RequestCap;

    /// <summary>The address a board kind and token are read from.</summary>
    public static string Address(string kind, string token) => kind switch
    {
        "greenhouse" => $"https://boards-api.greenhouse.io/v1/boards/{Uri.EscapeDataString(token)}/jobs?content=true&pay_transparency=true",
        "lever" => $"https://api.lever.co/v0/postings/{Uri.EscapeDataString(token)}?mode=json",
        "ashby" => $"https://api.ashbyhq.com/posting-api/job-board/{Uri.EscapeDataString(token)}?includeCompensation=true",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "greenhouse, lever or ashby"),
    };

    /// <summary>The board's list without descriptions, for a board that has one (Greenhouse); null for a board that has none.</summary>
    public static string? LightAddress(string kind, string token) =>
        kind == "greenhouse" ? $"https://boards-api.greenhouse.io/v1/boards/{Uri.EscapeDataString(token)}/jobs" : null;

    /// <inheritdoc />
    public Task<IReadOnlyList<BoardPosting>?> PostingsAsync(string kind, string token, CancellationToken ct) => FetchAsync(kind, Address(kind, token), ct);

    /// <inheritdoc />
    public async Task<BoardList?> ListAsync(string kind, string token, CancellationToken ct) => LightAddress(kind, token) is { } light
        ? await FetchAsync(kind, light, ct) is { } listed ? new BoardList(listed, Whole: false) : null
        : await PostingsAsync(kind, token, ct) is { } whole ? new BoardList(whole, Whole: true) : null;

    private async Task<IReadOnlyList<BoardPosting>?> FetchAsync(string kind, string address, CancellationToken ct)
    {
        using var cap = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cap.CancelAfter(Timeout);
        try
        {
            using var answer = await http.CreateClient().GetAsync(address, cap.Token);
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

    /// <summary>A board's JSON to its postings: the title, the day it was posted or last updated, and the row's fields.</summary>
    public static IReadOnlyList<BoardPosting> Read(string kind, string json)
    {
        using var doc = JsonDocument.Parse(json);
        IEnumerable<JsonElement> jobs = kind == "lever" ? doc.RootElement.EnumerateArray()
            : doc.RootElement.TryGetProperty("jobs", out var list) ? list.EnumerateArray() : [];
        return [.. jobs.Select(job => kind switch
        {
            "lever" => Lever(job),
            "ashby" => Ashby(job),
            _ => Greenhouse(job),
        })];
    }

    private static BoardPosting Greenhouse(JsonElement job)
    {
        var pay = job.TryGetProperty("pay_input_ranges", out var ranges) && ranges.ValueKind == JsonValueKind.Array
            ? ranges.EnumerateArray().FirstOrDefault(r => Number(r, "max_cents") is not null) : default;
        bool paid = pay.ValueKind == JsonValueKind.Object;
        return new BoardPosting(Text(job, "title"), PostingRules.Plain(Text(job, "content")), Day(job.TryGetProperty("first_published", out _) ? Text(job, "first_published") : Text(job, "updated_at")))
        {
            Id = Id(job, "id"),
            Url = Text(job, "absolute_url"),
            Location = job.TryGetProperty("location", out var where) && where.ValueKind == JsonValueKind.Object ? Text(where, "name") : "",
            CompMin = paid ? Cents(Number(pay, "min_cents")) : null,
            CompMax = paid ? Cents(Number(pay, "max_cents")) : null,
            CompCurrency = paid ? Text(pay, "currency_type") : null,
            CompInterval = paid ? "year" : null,
            PublishedAt = Instant(Text(job, "first_published")),
        };
    }

    private static BoardPosting Lever(JsonElement job)
    {
        var salary = job.TryGetProperty("salaryRange", out var range) && range.ValueKind == JsonValueKind.Object ? range : default;
        bool paid = salary.ValueKind == JsonValueKind.Object && Number(salary, "max") is not null;
        var categories = job.TryGetProperty("categories", out var c) && c.ValueKind == JsonValueKind.Object ? c : default;
        // A number outside the years 1 to 9999 is no instant; read as none rather than thrown, so one posting cannot stop the scan.
        DateTimeOffset? created = job.TryGetProperty("createdAt", out var ms) && ms.ValueKind == JsonValueKind.Number && ms.TryGetInt64(out long epoch)
            && epoch >= EarliestMs && epoch <= LatestMs ? DateTimeOffset.FromUnixTimeMilliseconds(epoch) : null;
        return new BoardPosting(Text(job, "text"), Text(job, "descriptionPlain"), created is { } at ? DateOnly.FromDateTime(at.UtcDateTime) : null)
        {
            Id = Id(job, "id"),
            Url = Text(job, "hostedUrl"),
            Location = categories.ValueKind == JsonValueKind.Object ? Text(categories, "location") : "",
            Workplace = Text(job, "workplaceType") is { Length: > 0 } w ? w : null,
            CompMin = paid ? Whole(Number(salary, "min")) : null,
            CompMax = paid ? Whole(Number(salary, "max")) : null,
            CompCurrency = paid ? Text(salary, "currency") : null,
            CompInterval = paid ? Text(salary, "interval") : null,
            PublishedAt = created,
        };
    }

    private static BoardPosting Ashby(JsonElement job)
    {
        var salary = job.TryGetProperty("compensation", out var comp) && comp.ValueKind == JsonValueKind.Object
            && comp.TryGetProperty("summaryComponents", out var parts) && parts.ValueKind == JsonValueKind.Array
            ? parts.EnumerateArray().FirstOrDefault(p => Text(p, "compensationType") == "Salary" && Number(p, "maxValue") is not null) : default;
        bool paid = salary.ValueKind == JsonValueKind.Object;
        return new BoardPosting(Text(job, "title"), Text(job, "descriptionPlain"), Day(Text(job, "publishedAt")))
        {
            Id = Id(job, "id"),
            Url = Text(job, "jobUrl"),
            Location = Places(job),
            Workplace = Text(job, "workplaceType") is { Length: > 0 } w ? w : null,
            IsRemote = job.TryGetProperty("isRemote", out var remote) && remote.ValueKind is JsonValueKind.True or JsonValueKind.False ? remote.GetBoolean() : null,
            CompMin = paid ? Whole(Number(salary, "minValue")) : null,
            CompMax = paid ? Whole(Number(salary, "maxValue")) : null,
            CompCurrency = paid ? Text(salary, "currencyCode") : null,
            CompInterval = paid ? Text(salary, "interval") : null,
            PublishedAt = Instant(Text(job, "publishedAt")),
        };
    }

    /// <summary>
    /// Ashby's location name, then each secondary location's name, joined the way Greenhouse writes a posting listed in
    /// several places. A company may name its first location for a business unit ("BU: Pharmacy") and list the city only
    /// as a secondary one, so the first name alone can hide where the seat is.
    /// </summary>
    private static string Places(JsonElement job)
    {
        var names = new List<string> { Text(job, "location") };
        if (job.TryGetProperty("secondaryLocations", out var more) && more.ValueKind == JsonValueKind.Array)
        {
            names.AddRange(more.EnumerateArray().Where(place => place.ValueKind == JsonValueKind.Object).Select(place => Text(place, "location")));
        }
        return string.Join("; ", names.Select(name => name.Trim()).Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static string Text(JsonElement job, string name) => job.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    private static string Id(JsonElement job, string name) => job.TryGetProperty(name, out var value) ? value.ValueKind switch
    {
        JsonValueKind.String => value.GetString()!,
        JsonValueKind.Number => value.GetRawText(),
        _ => "",
    } : "";

    private static decimal? Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : null;

    private static int? Cents(decimal? cents) => cents is { } c ? (int)(c / 100) : null;

    private static int? Whole(decimal? value) => value is { } v ? (int)v : null;

    private static DateTimeOffset? Instant(string text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? at : null;

    private static DateOnly? Day(string text) =>
        DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at) ? DateOnly.FromDateTime(at.UtcDateTime) : null;
}
