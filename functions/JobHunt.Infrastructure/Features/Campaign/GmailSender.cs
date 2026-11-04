using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace JobHunt.Infrastructure.Features.Campaign;

/// <summary>
/// The campaign's one sending call (ADR-118): users.messages.send with the nightly lane's token, which holds
/// gmail.modify and so may send (the review, F4), then users.messages.get for the Message-Id header a delivery
/// failure quotes back. The message is multipart/alternative, the plain body first and the HTML second, each
/// base64 so no line is rewrapped. Only CampaignSend's `on` path reaches this class; shadow never builds it.
/// </summary>
public sealed class GmailSender(IHttpClientFactory http, GoogleCredentials google) : IMailSender
{
    private const string Api = "https://gmail.googleapis.com/gmail/v1/users/me";

    private HttpClient? _client;

    /// <inheritdoc />
    public async Task<SentMail> SendAsync(OutgoingMail mail, CancellationToken ct)
    {
        _client ??= await google.ClientAsync(http, ct);
        string raw = MimeText.Base64Url(Encoding.UTF8.GetBytes(Mime(mail, "yard-" + Guid.NewGuid().ToString("N"))));
        using var answer = await _client.PostAsync($"{Api}/messages/send", JsonContent.Create(new { raw }), ct);
        answer.EnsureSuccessStatusCode();
        using var sent = JsonDocument.Parse(await answer.Content.ReadAsStringAsync(ct));
        string id = sent.RootElement.GetProperty("id").GetString()!;
        string thread = sent.RootElement.GetProperty("threadId").GetString()!;
        using var read = await _client.GetAsync($"{Api}/messages/{id}?format=metadata&metadataHeaders=Message-Id", ct);
        string? rfc = null;
        if (read.IsSuccessStatusCode)
        {
            using var json = JsonDocument.Parse(await read.Content.ReadAsStringAsync(ct));
            rfc = json.RootElement.GetProperty("payload").TryGetProperty("headers", out var headers)
                ? headers.EnumerateArray().Where(h => string.Equals(h.GetProperty("name").GetString(), "Message-Id", StringComparison.OrdinalIgnoreCase))
                    .Select(h => h.GetProperty("value").GetString()).FirstOrDefault()
                : null;
        }
        return new SentMail(id, thread, rfc);
    }

    /// <summary>The message as RFC 5322 text: To, Subject (encoded when not ASCII), and the two bodies.</summary>
    public static string Mime(OutgoingMail mail, string boundary)
    {
        if (mail.To.IndexOfAny(['\r', '\n', ',', ';']) >= 0 || mail.Subject.IndexOfAny(['\r', '\n']) >= 0)
        {
            throw new ArgumentException("a header with a line break or a second address", nameof(mail));
        }
        return $"To: {mail.To}\r\nSubject: {MimeText.Subject(mail.Subject)}\r\nMIME-Version: 1.0\r\nContent-Type: multipart/alternative; boundary=\"{boundary}\"\r\n\r\n"
            + MimeText.Alternative(boundary, mail.Text, mail.Html);
    }
}
