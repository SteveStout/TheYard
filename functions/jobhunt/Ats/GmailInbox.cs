using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace JobHunt.Functions.Ats;

/// <summary>
/// The inbox through the Gmail REST API with the nightly lane's client (gmail.modify). Four calls:
/// users.messages.list, users.messages.get (full, for the text), users.threads.get (metadata, the
/// senders) and users.messages.modify removing INBOX, read back with a second get. No call here
/// can send, draft, trash or delete.
/// </summary>
public sealed partial class GmailInbox(IHttpClientFactory http, GoogleCredentials google) : IInbox
{
    private const string Api = "https://gmail.googleapis.com/gmail/v1/users/me";

    private HttpClient? _client;

    public async Task<IReadOnlyList<string>> FindAsync(string query, int max, CancellationToken ct)
    {
        var ids = new List<string>();
        string? page = null;
        do
        {
            string url = $"{Api}/messages?q={Uri.EscapeDataString(query)}&maxResults={Math.Min(100, max - ids.Count)}" + (page is null ? "" : "&pageToken=" + Uri.EscapeDataString(page));
            using var json = await GetAsync(url, ct);
            if (json.RootElement.TryGetProperty("messages", out var messages))
            {
                ids.AddRange(messages.EnumerateArray().Select(m => m.GetProperty("id").GetString()!));
            }
            page = json.RootElement.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        }
        while (page is not null && ids.Count < max);
        return ids;
    }

    public async Task<InboxMessage> ReadAsync(string id, CancellationToken ct)
    {
        using var json = await GetAsync($"{Api}/messages/{id}?format=full", ct);
        var root = json.RootElement;
        var payload = root.GetProperty("payload");
        long ms = long.Parse(root.GetProperty("internalDate").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        return new InboxMessage(id, root.GetProperty("threadId").GetString()!, Header(payload, "From"), Header(payload, "Subject"),
            DateTimeOffset.FromUnixTimeMilliseconds(ms), Text(payload)) { Lines = Lines(payload), Html = Html(payload) };
    }

    public async Task<IReadOnlyList<string>> ThreadSendersAsync(string threadId, CancellationToken ct)
    {
        using var json = await GetAsync($"{Api}/threads/{threadId}?format=metadata&metadataHeaders=From", ct);
        return [.. json.RootElement.GetProperty("messages").EnumerateArray().Select(m => Header(m.GetProperty("payload"), "From"))];
    }

    public async Task<bool> ArchiveAsync(string id, CancellationToken ct)
    {
        var client = await ClientAsync(ct);
        using var answer = await client.PostAsync($"{Api}/messages/{id}/modify", JsonContent.Create(new { removeLabelIds = new[] { "INBOX" } }), ct);
        answer.EnsureSuccessStatusCode();
        using var json = await GetAsync($"{Api}/messages/{id}?format=minimal", ct);
        return !(json.RootElement.TryGetProperty("labelIds", out var labels) && labels.EnumerateArray().Any(l => l.GetString() == "INBOX"));
    }

    /// <summary>True when any message in the thread carries the DRAFT label.</summary>
    public async Task<bool> ThreadHasDraftAsync(string threadId, CancellationToken ct)
    {
        using var json = await GetAsync($"{Api}/threads/{threadId}?format=minimal", ct);
        return json.RootElement.GetProperty("messages").EnumerateArray()
            .Any(m => m.TryGetProperty("labelIds", out var labels) && labels.EnumerateArray().Any(l => l.GetString() == "DRAFT"));
    }

    /// <summary>The message's text: its plain parts, else its HTML parts with the markup taken out.</summary>
    public static string Text(JsonElement payload)
    {
        var plain = new StringBuilder();
        var html = new StringBuilder();
        Walk(payload, plain, html);
        string text = plain.Length > 0 ? plain.ToString() : Tags().Replace(html.ToString(), " ");
        return Space().Replace(WebUtility.HtmlDecode(text), " ").Trim();
    }

    /// <summary>The HTML parts as sent, markup and links kept.</summary>
    public static string Html(JsonElement payload)
    {
        var html = new StringBuilder();
        Walk(payload, new StringBuilder(), html);
        return html.ToString();
    }

    /// <summary>The plain part with its line breaks kept: CRLF read as one break, spaces inside a line collapsed.</summary>
    public static string Lines(JsonElement payload)
    {
        var plain = new StringBuilder();
        Walk(payload, plain, new StringBuilder());
        return string.Join('\n', plain.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => InLine().Replace(line, " ").Trim()));
    }

    private static void Walk(JsonElement part, StringBuilder plain, StringBuilder html)
    {
        string mime = part.TryGetProperty("mimeType", out var m) ? m.GetString() ?? "" : "";
        if (part.TryGetProperty("body", out var body) && body.TryGetProperty("data", out var data) && data.GetString() is { Length: > 0 } encoded)
        {
            string decoded = Encoding.UTF8.GetString(Convert.FromBase64String(Pad(encoded.Replace('-', '+').Replace('_', '/'))));
            (mime.Equals("text/plain", StringComparison.OrdinalIgnoreCase) ? plain : html).Append(decoded).Append('\n');
        }
        if (part.TryGetProperty("parts", out var parts))
        {
            foreach (var child in parts.EnumerateArray())
            {
                Walk(child, plain, html);
            }
        }
    }

    private static string Pad(string base64) => base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');

    private static string Header(JsonElement payload, string name) =>
        payload.TryGetProperty("headers", out var headers)
            ? headers.EnumerateArray().Where(h => string.Equals(h.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase)).Select(h => h.GetProperty("value").GetString() ?? "").FirstOrDefault() ?? ""
            : "";

    private async Task<JsonDocument> GetAsync(string url, CancellationToken ct)
    {
        var client = await ClientAsync(ct);
        using var answer = await client.GetAsync(url, ct);
        answer.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await answer.Content.ReadAsStringAsync(ct));
    }

    private async Task<HttpClient> ClientAsync(CancellationToken ct) => _client ??= await google.ClientAsync(http, ct);

    [GeneratedRegex(@"<(style|script)[^>]*>.*?</\1>|<[^>]+>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Tags();

    [GeneratedRegex(@"[ \t\f\v]+")]
    private static partial Regex InLine();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();
}
