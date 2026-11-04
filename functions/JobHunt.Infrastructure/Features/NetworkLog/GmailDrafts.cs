using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace JobHunt.Infrastructure.Features.NetworkLog;

/// <summary>
/// The network log's Gmail writes with the nightly lane's token: users.labels (list, then create when absent),
/// users.drafts.create into the original thread, users.messages.modify for the label, users.drafts.get for the read
/// back. Nothing here sends: the class has no call to messages.send or drafts.send, and NetworkLogSliceTests holds
/// that. Only the `on` path builds it; shadow is handed a writer that refuses.
/// </summary>
public sealed class GmailDrafts(IHttpClientFactory http, GoogleCredentials google) : IDraftWriter
{
    private const string Api = "https://gmail.googleapis.com/gmail/v1/users/me";

    private readonly Dictionary<string, string> _labels = new(StringComparer.Ordinal);

    private HttpClient? _client;

    private bool _listed;

    /// <inheritdoc />
    public async Task<string> EnsureLabelAsync(string name, CancellationToken ct)
    {
        if (_labels.TryGetValue(name, out string? cached))
        {
            return cached;
        }
        var client = await ClientAsync(ct);
        if (!_listed)
        {
            // One list a pass: every label's id is cached by name, so the second label costs no second list.
            using var list = await client.GetAsync($"{Api}/labels", ct);
            list.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await list.Content.ReadAsStringAsync(ct));
            foreach (var label in json.RootElement.TryGetProperty("labels", out var labels) ? labels.EnumerateArray() : default)
            {
                _labels[label.GetProperty("name").GetString()!] = label.GetProperty("id").GetString()!;
            }
            _listed = true;
            if (_labels.TryGetValue(name, out string? listed))
            {
                return listed;
            }
        }
        using var made = await client.PostAsync($"{Api}/labels", JsonContent.Create(new { name, labelListVisibility = "labelShow", messageListVisibility = "show" }), ct);
        made.EnsureSuccessStatusCode();
        using var answer = JsonDocument.Parse(await made.Content.ReadAsStringAsync(ct));
        return _labels[name] = answer.RootElement.GetProperty("id").GetString()!;
    }

    /// <inheritdoc />
    public async Task<(string DraftId, string MessageId)> CreateDraftAsync(DraftMail draft, CancellationToken ct)
    {
        var client = await ClientAsync(ct);
        string raw = MimeText.Base64Url(Encoding.UTF8.GetBytes(Mime(draft, "yard-" + Guid.NewGuid().ToString("N"))));
        using var answer = await client.PostAsync($"{Api}/drafts", JsonContent.Create(new { message = new { raw, threadId = draft.ThreadId } }), ct);
        answer.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await answer.Content.ReadAsStringAsync(ct));
        return (json.RootElement.GetProperty("id").GetString()!, json.RootElement.GetProperty("message").GetProperty("id").GetString()!);
    }

    /// <inheritdoc />
    public async Task LabelAsync(string messageId, string labelId, CancellationToken ct)
    {
        var client = await ClientAsync(ct);
        using var answer = await client.PostAsync($"{Api}/messages/{messageId}/modify", JsonContent.Create(new { addLabelIds = new[] { labelId } }), ct);
        answer.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public async Task<bool> DraftExistsAsync(string draftId, CancellationToken ct)
    {
        var client = await ClientAsync(ct);
        using var answer = await client.GetAsync($"{Api}/drafts/{draftId}?format=minimal", ct);
        if (answer.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
        answer.EnsureSuccessStatusCode();
        return true;
    }

    /// <summary>
    /// The draft as RFC 5322 text: To, Subject, In-Reply-To and References so Gmail and the person's client keep it in
    /// the thread, then the two bodies. A header with a line break or a second address is refused.
    /// </summary>
    public static string Mime(DraftMail draft, string boundary)
    {
        string[] headers = [draft.To, draft.Subject, draft.InReplyTo ?? "", draft.References ?? ""];
        if (headers.Any(h => h.IndexOfAny(['\r', '\n']) >= 0) || draft.To.IndexOfAny([',', ';']) >= 0 || !draft.To.Contains('@', StringComparison.Ordinal))
        {
            throw new ArgumentException("a header with a line break, a second address or no address", nameof(draft));
        }
        var text = new StringBuilder();
        text.Append("To: ").Append(draft.To).Append("\r\n");
        text.Append("Subject: ").Append(MimeText.Subject(draft.Subject)).Append("\r\n");
        if (!string.IsNullOrEmpty(draft.InReplyTo))
        {
            text.Append("In-Reply-To: ").Append(draft.InReplyTo).Append("\r\n");
        }
        if (!string.IsNullOrEmpty(draft.References))
        {
            text.Append("References: ").Append(draft.References).Append("\r\n");
        }
        text.Append("MIME-Version: 1.0\r\nContent-Type: multipart/alternative; boundary=\"").Append(boundary).Append("\"\r\n\r\n");
        text.Append(MimeText.Alternative(boundary, draft.Text, draft.Html));
        return text.ToString();
    }

    private async Task<HttpClient> ClientAsync(CancellationToken ct) => _client ??= await google.ClientAsync(http, ct);
}
