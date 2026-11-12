using System.Text;
using System.Text.Json;

namespace JobHunt.Infrastructure.Features.Campaign;

/// <summary>
/// The campaign's one sending call (ADR-118): users.messages.send with the nightly lane's token, which holds
/// gmail.modify and so may send (the review, F4), then users.messages.get for the Message-Id header a delivery
/// failure quotes back. The message is multipart/alternative, the plain body first and the HTML second, each
/// base64 so no line is rewrapped. Only CampaignSend's `on` path reaches this class; shadow never builds it.
/// Both calls go through <see cref="GmailHttp"/>: the send is made again only after a rate answer, which Gmail gives
/// before it sends anything, never after a server error, which may have come after the message went.
/// </summary>
/// <param name="http">The client factory.</param>
/// <param name="google">The lane's credentials.</param>
/// <param name="time">The clock a rate answer is waited out on; the system's when none is given.</param>
public sealed class GmailSender(IHttpClientFactory http, GoogleCredentials google, TimeProvider? time = null) : IMailSender
{
    private const string Api = GmailHttp.Api;

    private readonly GmailHttp _gmail = new(http, google, time ?? TimeProvider.System);

    /// <inheritdoc />
    public async Task<SentMail> SendAsync(OutgoingMail mail, CancellationToken ct)
    {
        string raw = MimeText.Base64Url(Encoding.UTF8.GetBytes(Mime(mail, "yard-" + Guid.NewGuid().ToString("N"))));
        using var sent = await _gmail.PostAsync($"{Api}/messages/send", new { raw }, false, ct);
        string id = sent.RootElement.GetProperty("id").GetString()!;
        string thread = sent.RootElement.GetProperty("threadId").GetString()!;
        return new SentMail(id, thread, await MessageIdAsync(id, ct));
    }

    /// <summary>The sent message's Message-Id header, or null when it cannot be read: the message went either way.</summary>
    private async Task<string?> MessageIdAsync(string id, CancellationToken ct)
    {
        try
        {
            using var json = await _gmail.GetAsync($"{Api}/messages/{id}?format=metadata&metadataHeaders=Message-Id", ct);
            return json.RootElement.GetProperty("payload").TryGetProperty("headers", out var headers)
                ? headers.EnumerateArray().Where(h => string.Equals(h.GetProperty("name").GetString(), "Message-Id", StringComparison.OrdinalIgnoreCase))
                    .Select(h => h.GetProperty("value").GetString()).FirstOrDefault()
                : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
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
