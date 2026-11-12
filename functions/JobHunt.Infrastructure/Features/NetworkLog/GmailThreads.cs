using System.Globalization;
using System.Net;
using System.Text.Json;

namespace JobHunt.Infrastructure.Features.NetworkLog;

/// <summary>
/// The network log's Gmail reads with the nightly lane's token (gmail.modify, ADR-JOBHUNT-001): users.threads.list
/// for the window, users.threads.get as metadata for the senders, times, labels and the opening words, and as full
/// only for an invite's calendar part. The search syntax is written here and nowhere else. Every request goes through
/// <see cref="GmailHttp"/>, which spaces it on the process's one clock and waits out a rate answer; a failure names
/// Gmail's own reason word.
/// </summary>
public sealed class GmailThreads(IHttpClientFactory http, GoogleCredentials google, TimeProvider time) : IThreadReader
{
    private const string Api = GmailHttp.Api;

    /// <summary>The least time between two requests, the shared one (<see cref="GmailHttp.Spacing"/>).</summary>
    public static readonly TimeSpan Spacing = GmailHttp.Spacing;

    private readonly GmailHttp _gmail = new(http, google, time);

    /// <summary>The search a window becomes: every thread touched in it, before a point when one is set, chats left out.</summary>
    public static string Query(ThreadSearch search) =>
        $"newer_than:{search.NewerThanDays.ToString(CultureInfo.InvariantCulture)}d"
        + (search.Before is { } before ? $" before:{before.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}" : "")
        + " -in:chats";

    /// <summary>How long a request waits for its slot, and the slot after it (<see cref="GmailHttp.Slot"/>).</summary>
    /// <param name="now">Now, as a timestamp.</param>
    /// <param name="next">The next free slot, zero before the first request.</param>
    /// <param name="spacing">The spacing in timestamp ticks.</param>
    public static (long Wait, long Next) Slot(long now, long next, long spacing) => GmailHttp.Slot(now, next, spacing);

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

    /// <summary>How many times a rate answer or a server error is waited out before the read fails.</summary>
    public const int Retries = GmailHttp.Retries;

    /// <summary>The reason words Gmail puts on a 403 that means slow down, not refused.</summary>
    public static readonly IReadOnlySet<string> RateReasons = GmailHttp.RateReasons;

    /// <summary>Gmail's reason word from an error body (<see cref="GmailHttp.Reason"/>).</summary>
    /// <param name="body">The error answer's body.</param>
    public static string? Reason(string body) => GmailHttp.Reason(body);

    private Task<JsonDocument> GetAsync(string url, CancellationToken ct) => _gmail.GetAsync(url, ct);
}
