using System.Text;
using System.Text.Json;

namespace JobHunt.Infrastructure.Features.NetworkLog;

/// <summary>
/// The network log's Gmail writes with the nightly lane's token: users.labels (list, then create when absent),
/// users.drafts.create into the original thread, users.messages.modify for the label, users.drafts.get for the read
/// back. Nothing here sends: the class has no call to messages.send or drafts.send, and NetworkLogSliceTests holds
/// that. Only the `on` path builds it; shadow is handed a writer that refuses. Every call goes through
/// <see cref="GmailHttp"/>; a call that makes something is made again only after a rate answer.
/// </summary>
/// <param name="http">The client factory.</param>
/// <param name="google">The lane's credentials.</param>
/// <param name="time">The clock a rate answer is waited out on; the system's when none is given.</param>
public sealed class GmailDrafts(IHttpClientFactory http, GoogleCredentials google, TimeProvider? time = null) : IDraftWriter
{
    private const string Api = GmailHttp.Api;

    private readonly Dictionary<string, string> _labels = new(StringComparer.Ordinal);

    private readonly GmailHttp _gmail = new(http, google, time ?? TimeProvider.System);

    private bool _listed;

    /// <inheritdoc />
    public async Task<string> EnsureLabelAsync(string name, CancellationToken ct)
    {
        if (_labels.TryGetValue(name, out string? cached))
        {
            return cached;
        }
        if (!_listed)
        {
            // One list a pass: every label's id is cached by name, so the second label costs no second list.
            using var json = await _gmail.GetAsync($"{Api}/labels", ct);
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
        using var answer = await _gmail.PostAsync($"{Api}/labels", new { name, labelListVisibility = "labelShow", messageListVisibility = "show" }, false, ct);
        return _labels[name] = answer.RootElement.GetProperty("id").GetString()!;
    }

    /// <inheritdoc />
    public async Task<(string DraftId, string MessageId)> CreateDraftAsync(DraftMail draft, CancellationToken ct)
    {
        string raw = MimeText.Base64Url(Encoding.UTF8.GetBytes(Mime(draft, "yard-" + Guid.NewGuid().ToString("N"))));
        using var json = await _gmail.PostAsync($"{Api}/drafts", new { message = new { raw, threadId = draft.ThreadId } }, false, ct);
        return (json.RootElement.GetProperty("id").GetString()!, json.RootElement.GetProperty("message").GetProperty("id").GetString()!);
    }

    /// <inheritdoc />
    public async Task LabelAsync(string messageId, string labelId, CancellationToken ct)
    {
        using var answer = await _gmail.PostAsync($"{Api}/messages/{messageId}/modify", new { addLabelIds = new[] { labelId } }, true, ct);
    }

    /// <inheritdoc />
    public async Task<bool> DraftExistsAsync(string draftId, CancellationToken ct)
    {
        using var answer = await _gmail.GetOrMissingAsync($"{Api}/drafts/{draftId}?format=minimal", ct);
        return answer is not null;
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
}
