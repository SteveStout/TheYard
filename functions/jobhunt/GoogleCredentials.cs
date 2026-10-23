using System.Net.Http.Headers;
using System.Text.Json;

namespace JobHunt.Functions;

/// <summary>
/// The Google OAuth client and refresh token the nightly lane holds (ADR-JOBHUNT-001 in the job-hunt
/// folder: gmail.modify and spreadsheets, one client owned by Steve), set on the Function out of band
/// and never kept in the repository. Each use trades the refresh token for a fresh access token.
/// </summary>
/// <param name="ClientId">GMAIL_CLIENT_ID.</param>
/// <param name="ClientSecret">GMAIL_CLIENT_SECRET.</param>
/// <param name="RefreshToken">GMAIL_REFRESH_TOKEN.</param>
public sealed record GoogleCredentials(string ClientId, string ClientSecret, string RefreshToken)
{
    /// <summary>The credentials as the Function holds them, or null while any is unset.</summary>
    public static GoogleCredentials? FromEnvironment()
    {
        string? id = Environment.GetEnvironmentVariable("GMAIL_CLIENT_ID");
        string? secret = Environment.GetEnvironmentVariable("GMAIL_CLIENT_SECRET");
        string? refresh = Environment.GetEnvironmentVariable("GMAIL_REFRESH_TOKEN");
        return string.IsNullOrEmpty(id) || string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(refresh) ? null : new GoogleCredentials(id, secret, refresh);
    }

    /// <summary>A client carrying a fresh access token.</summary>
    public async Task<HttpClient> ClientAsync(IHttpClientFactory http, CancellationToken ct)
    {
        var client = http.CreateClient();
        using var trade = await client.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["refresh_token"] = RefreshToken,
            ["grant_type"] = "refresh_token",
        }), ct);
        if (!trade.IsSuccessStatusCode)
        {
            // Google answers 400 invalid_grant when the refresh token was revoked, as Steve's password change of
            // 2026-10-23 08:12 did: every Gmail call of the 09:20 and 09:50 passes failed on it. Named, so the pass
            // says what is wrong and what restores it instead of three identical HTTP errors.
            throw new GoogleTokenRefusedException((int)trade.StatusCode);
        }
        using var token = JsonDocument.Parse(await trade.Content.ReadAsStringAsync(ct));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.RootElement.GetProperty("access_token").GetString());
        return client;
    }
}

/// <summary>Google refused to trade the refresh token for an access token: it was revoked or has expired, and one consent click restores it.</summary>
/// <param name="status">The HTTP status Google answered the trade with.</param>
public sealed class GoogleTokenRefusedException(int status)
    : Exception($"Google refused the refresh token (HTTP {status}); run J:\\staging\\pickup1023\\get_google_token_j.py on the laptop and allow it")
{
    /// <summary>The HTTP status Google answered with.</summary>
    public int Status { get; } = status;
}
