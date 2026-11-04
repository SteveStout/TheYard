using System.Globalization;
using System.Net;
using System.Text.Json;

namespace JobHunt.Infrastructure.Features.NetworkLog;

/// <summary>
/// The network log's Gmail reads with the nightly lane's token (gmail.modify, ADR-JOBHUNT-001): users.threads.list
/// for the window, users.threads.get as metadata for the senders, times, labels and the opening words, and as full
/// only for an invite's calendar part. The search syntax is written here and nowhere else. A rate answer (429) or a
/// server error is waited out three times before the pass fails.
/// </summary>
public sealed class GmailThreads(IHttpClientFactory http, GoogleCredentials google) : IThreadReader
{
    private const string Api = "https://gmail.googleapis.com/gmail/v1/users/me";

    private HttpClient? _client;

    /// <summary>The search a window becomes: every thread touched in it, chats left out.</summary>
    public static string Query(ThreadSearch search) => $"newer_than:{search.NewerThanDays.ToString(CultureInfo.InvariantCulture)}d -in:chats";

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> FindThreadIdsAsync(ThreadSearch search, CancellationToken ct)
    {
        var ids = new List<string>();
        string? page = null;
        do
        {
            string url = $"{Api}/threads?q={Uri.EscapeDataString(Query(search))}&maxResults={Math.Min(100, search.Max - ids.Count)}" + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page));
            using var json = await GetAsync(url, ct);
            if (json.RootElement.TryGetProperty("threads", out var threads))
            {
                ids.AddRange(threads.EnumerateArray().Select(t => t.GetProperty("id").GetString()!));
            }
            page = json.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        }
        while (page is not null && ids.Count < search.Max);
        return ids;
    }

    /// <inheritdoc />
    public async Task<ThreadSummary> ReadSummaryAsync(string threadId, CancellationToken ct)
    {
        using var json = await GetAsync($"{Api}/threads/{threadId}?format=metadata&metadataHeaders=From&metadataHeaders=To&metadataHeaders=Subject&metadataHeaders=Message-Id", ct);
        return Summary(json.RootElement);
    }

    /// <inheritdoc />
    public async Task<bool> ReadHasCalendarPartAsync(string threadId, CancellationToken ct)
    {
        using var json = await GetAsync($"{Api}/threads/{threadId}?format=full", ct);
        return json.RootElement.TryGetProperty("messages", out var messages) && messages.EnumerateArray().Any(m => m.TryGetProperty("payload", out var p) && HasCalendar(p));
    }

    /// <summary>A thread's metadata answer as the network log reads it. Drafts are not messages anyone sent.</summary>
    public static ThreadSummary Summary(JsonElement thread)
    {
        string id = thread.GetProperty("id").GetString()!;
        var messages = new List<ThreadMessage>();
        string subject = "";
        if (thread.TryGetProperty("messages", out var list))
        {
            foreach (var m in list.EnumerateArray())
            {
                var labels = m.TryGetProperty("labelIds", out var l) ? l.EnumerateArray().Select(x => x.GetString()).ToHashSet(StringComparer.Ordinal) : [];
                if (labels.Contains("DRAFT"))
                {
                    continue;
                }
                var payload = m.GetProperty("payload");
                string from = Header(payload, "From");
                if (subject.Length == 0)
                {
                    subject = Header(payload, "Subject");
                }
                long ms = long.Parse(m.GetProperty("internalDate").GetString()!, CultureInfo.InvariantCulture);
                bool outbound = labels.Contains("SENT") || HumanSenderRule.IsSteve(from);
                string snippet = WebUtility.HtmlDecode(m.TryGetProperty("snippet", out var s) ? s.GetString() ?? "" : "");
                string rfc = Header(payload, "Message-Id");
                messages.Add(new ThreadMessage(from, DateTimeOffset.FromUnixTimeMilliseconds(ms), outbound, false, snippet, Header(payload, "To"), rfc.Length == 0 ? null : rfc));
            }
        }
        return new ThreadSummary(id, subject, [.. messages.OrderBy(m => m.At)]);
    }

    private static bool HasCalendar(JsonElement part) =>
        (part.TryGetProperty("mimeType", out var type) && string.Equals(type.GetString(), "text/calendar", StringComparison.OrdinalIgnoreCase))
        || (part.TryGetProperty("filename", out var name) && (name.GetString() ?? "").EndsWith(".ics", StringComparison.OrdinalIgnoreCase))
        || (part.TryGetProperty("parts", out var parts) && parts.EnumerateArray().Any(HasCalendar));

    private static string Header(JsonElement payload, string name) =>
        payload.TryGetProperty("headers", out var headers)
            ? headers.EnumerateArray().Where(h => string.Equals(h.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase)).Select(h => h.GetProperty("value").GetString() ?? "").FirstOrDefault() ?? ""
            : "";

    private async Task<JsonDocument> GetAsync(string url, CancellationToken ct)
    {
        _client ??= await google.ClientAsync(http, ct);
        for (int attempt = 1; ; attempt++)
        {
            using var answer = await _client.GetAsync(url, ct);
            if ((answer.StatusCode == HttpStatusCode.TooManyRequests || (int)answer.StatusCode >= 500) && attempt <= 3)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
                continue;
            }
            answer.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await answer.Content.ReadAsStringAsync(ct));
        }
    }
}
