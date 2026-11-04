using System.Text;

namespace JobHunt.Infrastructure.Shared.Google;

/// <summary>
/// The pieces of an RFC 5322 message both Gmail writers build: a body part in base64 so no line is rewrapped, a
/// subject encoded when it is not ASCII, and the base64url Gmail's raw field takes.
/// </summary>
public static class MimeText
{
    /// <summary>One body part: its type, UTF-8, base64 in lines of 76, with CRLF line ends in the body.</summary>
    public static string Part(string type, string body) =>
        $"Content-Type: {type}; charset=\"UTF-8\"\r\nContent-Transfer-Encoding: base64\r\n\r\n"
        + string.Join("\r\n", Convert.ToBase64String(Encoding.UTF8.GetBytes(body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal))).Chunk(76).Select(c => new string(c)))
        + "\r\n";

    /// <summary>The subject as a header value: as written when ASCII, else an encoded word.</summary>
    public static string Subject(string subject) =>
        subject.All(c => c < 128) ? subject : $"=?UTF-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(subject))}?=";

    /// <summary>The text and HTML bodies as one multipart/alternative body, the plain one first.</summary>
    public static string Alternative(string boundary, string text, string html) =>
        $"--{boundary}\r\n" + Part("text/plain", text) + $"--{boundary}\r\n" + Part("text/html", html) + $"--{boundary}--\r\n";

    /// <summary>Bytes as base64url without padding.</summary>
    public static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
