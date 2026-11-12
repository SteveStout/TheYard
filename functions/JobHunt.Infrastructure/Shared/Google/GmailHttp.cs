using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace JobHunt.Infrastructure.Shared.Google;

/// <summary>
/// Every Gmail request the Function makes, with the nightly lane's token (ADR-JOBHUNT-001). One clock for the whole
/// process spaces the requests, because the sweep, the reply drafts, the pounce, the campaign and the network log run
/// in one host and share one user's quota: each waits for its slot first, so the reads stay under the quota instead of
/// meeting it. A rate answer (a 429, or the 403 Gmail gives a user over its per-second quota: rateLimitExceeded,
/// userRateLimitExceeded) is waited out five times with a doubling wait; a server error too, on a call that may be
/// made twice. Any other answer fails at once. A failure names Gmail's own reason word and the tries it took, so the
/// pass's FAILED line says why.
/// </summary>
/// <param name="http">The client factory.</param>
/// <param name="google">The lane's credentials.</param>
/// <param name="time">The clock the doubling waits run on; the slots always run on the process's own.</param>
public sealed class GmailHttp(IHttpClientFactory http, GoogleCredentials google, TimeProvider time)
{
    /// <summary>The signed-in user's root in the Gmail API.</summary>
    public const string Api = "https://gmail.googleapis.com/gmail/v1/users/me";

    /// <summary>How many times a rate answer or a server error is waited out before the request fails.</summary>
    public const int Retries = 5;

    /// <summary>
    /// The least time between two requests: 20 a second, 200 of the 250 quota units a second Gmail allows one user,
    /// at 10 units a thread read. Four reads at once with no spacing ran about 33 a second and drew a 403 on one read
    /// in eight, and the doubling waits behind those 403s were most of a backfill's time.
    /// </summary>
    public static readonly TimeSpan Spacing = TimeSpan.FromMilliseconds(50);

    /// <summary>The reason words Gmail puts on a 403 that means slow down, not refused.</summary>
    public static readonly IReadOnlySet<string> RateReasons = new HashSet<string>(["rateLimitExceeded", "userRateLimitExceeded"], StringComparer.Ordinal);

    private static readonly Lock Gate = new();

    private static long _next;

    private HttpClient? _client;

    /// <summary>A GET's answer.</summary>
    /// <param name="url">The request.</param>
    /// <param name="ct">The cancellation token.</param>
    public async Task<JsonDocument> GetAsync(string url, CancellationToken ct) =>
        (await SendAsync(HttpMethod.Get, url, null, true, false, ct))!;

    /// <summary>A GET's answer, or null when Gmail answers 404.</summary>
    /// <param name="url">The request.</param>
    /// <param name="ct">The cancellation token.</param>
    public Task<JsonDocument?> GetOrMissingAsync(string url, CancellationToken ct) =>
        SendAsync(HttpMethod.Get, url, null, true, true, ct);

    /// <summary>
    /// A POST's answer. A server error is waited out only when <paramref name="again"/> says the call may be made twice
    /// (a label change), never for one that makes something (a send, a draft, a label), where it may have landed.
    /// </summary>
    /// <param name="url">The request.</param>
    /// <param name="body">The JSON body.</param>
    /// <param name="again">True when making the call twice does no more than making it once.</param>
    /// <param name="ct">The cancellation token.</param>
    public async Task<JsonDocument> PostAsync(string url, object body, bool again, CancellationToken ct) =>
        (await SendAsync(HttpMethod.Post, url, body, again, false, ct))!;

    /// <summary>
    /// How long a request waits for its slot, and the slot after it: the next free slot or now, whichever is later,
    /// with the following one a spacing on. In timestamp ticks, so the callers share one clock.
    /// </summary>
    /// <param name="now">Now, as a timestamp.</param>
    /// <param name="next">The next free slot, zero before the first request.</param>
    /// <param name="spacing">The spacing in timestamp ticks.</param>
    public static (long Wait, long Next) Slot(long now, long next, long spacing)
    {
        long slot = Math.Max(now, next);
        return (slot - now, slot + spacing);
    }

    /// <summary>Whether an answer means slow down: a 429, a 403 with a rate reason, or a server error.</summary>
    /// <param name="status">The answer's status.</param>
    /// <param name="reason">Gmail's reason word, or null.</param>
    public static bool IsRate(HttpStatusCode status, string? reason) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500
        || (status == HttpStatusCode.Forbidden && reason is not null && RateReasons.Contains(reason));

    /// <summary>Gmail's reason word from an error body (error.errors[0].reason, else error.status); null when the body has none.</summary>
    /// <param name="body">The error answer's body.</param>
    public static string? Reason(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (!json.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            if (error.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0
                && errors[0].TryGetProperty("reason", out var reason) && reason.GetString() is { Length: > 0 } word)
            {
                return word;
            }
            return error.TryGetProperty("status", out var status) ? status.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task<JsonDocument?> SendAsync(HttpMethod method, string url, object? body, bool again, bool missing, CancellationToken ct)
    {
        _client ??= await google.ClientAsync(http, ct);
        for (int attempt = 1; ; attempt++)
        {
            await PaceAsync(ct);
            using var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
            using var answer = await _client.SendAsync(request, ct);
            string text = await answer.Content.ReadAsStringAsync(ct);
            if (answer.IsSuccessStatusCode)
            {
                return JsonDocument.Parse(text);
            }
            if (missing && answer.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            string? reason = Reason(text);
            bool wait = IsRate(answer.StatusCode, reason) && (again || (int)answer.StatusCode < 500);
            if (wait && attempt <= Retries)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt)), time, ct);
                continue;
            }
            throw new HttpRequestException($"Gmail answered {(int)answer.StatusCode} ({reason ?? "no reason given"}) after {attempt} tries", null, answer.StatusCode);
        }
    }

    private static async Task PaceAsync(CancellationToken ct)
    {
        var clock = TimeProvider.System;
        long wait;
        lock (Gate)
        {
            (wait, _next) = Slot(clock.GetTimestamp(), _next, Spacing.Ticks * clock.TimestampFrequency / TimeSpan.TicksPerSecond);
        }
        if (wait > 0)
        {
            await Task.Delay(TimeSpan.FromSeconds((double)wait / clock.TimestampFrequency), ct);
        }
    }
}
