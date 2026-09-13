using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// The password reset in two halves (ADR: Accounts and per-user bids,
/// addendum): the operator mints a link behind the key, the visitor uses it
/// once. Held on a real host: the link is a 404 without the key and a 404
/// for an address with no account; a valid link changes the password and
/// signs the visitor in; the old password stops working and the new one
/// works; the same link a second time is refused; a session token is not a
/// reset token and a reset token is not a session.
/// </summary>
public class PasswordResetTests : IClassFixture<PasswordResetTests.KeyedHost>
{
    public sealed class KeyedHost : WebApplicationFactory<Program>
    {
        public const string Key = "the-reset-test-key";

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.UseSetting("Admin:Key", Key);
    }

    private readonly KeyedHost _host;

    public PasswordResetTests(KeyedHost host)
    {
        _host = host;
    }

    private HttpClient Client() => _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    private static async Task<string> RegisterAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new { email, password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return email;
    }

    private static async Task<string> MintAsync(HttpClient client, string email)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/reset-links")
        {
            Content = JsonContent.Create(new { email }),
        };
        request.Headers.Add("X-Admin-Key", KeyedHost.Key);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        string url = json.RootElement.GetProperty("url").GetString()!;
        Assert.Contains("?view=account&reset=", url);
        return Uri.UnescapeDataString(url[(url.IndexOf("reset=", StringComparison.Ordinal) + 6)..]);
    }

    // #region reset
    [Fact]
    public async Task A_link_needs_the_key_and_an_account()
    {
        var client = Client();
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/api/admin/reset-links", new { email = "x@example.com" })).StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/reset-links")
        {
            Content = JsonContent.Create(new { email = $"nobody-{Guid.NewGuid():N}@example.com" }),
        };
        request.Headers.Add("X-Admin-Key", KeyedHost.Key);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task A_link_changes_the_password_once_and_signs_the_visitor_in()
    {
        var client = Client();
        string email = await RegisterAsync(client, $"reset-{Guid.NewGuid():N}@example.com", "first password");
        string token = await MintAsync(client, email);

        // A short password leaves the old one in place.
        var tooShort = await client.PostAsJsonAsync("/api/auth/reset", new { token, password = "short" });
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "first password" })).StatusCode);

        var reset = await client.PostAsJsonAsync("/api/auth/reset", new { token, password = "second password" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Contains(reset.Headers.GetValues("Set-Cookie"), c => c.StartsWith(TokenIssuer.CookieName + "=", StringComparison.Ordinal));
        using var signedIn = JsonDocument.Parse(await reset.Content.ReadAsStringAsync());
        Assert.True(signedIn.RootElement.GetProperty("signed_in").GetBoolean());

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "first password" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "second password" })).StatusCode);

        // The same link a second time: the password hash moved, so the
        // fingerprint no longer matches and the link is spent.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/reset", new { token, password = "third password" })).StatusCode);
    }

    [Fact]
    public async Task A_session_token_is_not_a_reset_token_and_a_reset_token_is_not_a_session()
    {
        var client = Client();
        var issuer = _host.Services.GetRequiredService<TokenIssuer>();
        string session = issuer.Issue("user-1", "someone", "sql");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/reset", new { token = session, password = "long enough" })).StatusCode);

        string reset = issuer.IssueReset("user-1", "sql", null);
        using var asSession = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        asSession.Headers.Add("Cookie", $"{TokenIssuer.CookieName}={reset}");
        var me = await client.SendAsync(asSession);
        if (me.StatusCode == HttpStatusCode.OK)
        {
            using var json = JsonDocument.Parse(await me.Content.ReadAsStringAsync());
            Assert.False(json.RootElement.GetProperty("signed_in").GetBoolean());
        }
        else
        {
            Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        }

        Assert.Null(await issuer.ReadResetAsync(session));
        Assert.Null(await issuer.ReadResetAsync("not a token"));
        var read = await issuer.ReadResetAsync(reset);
        Assert.NotNull(read);
        Assert.Equal(("user-1", "sql", TokenIssuer.Fingerprint(null)), read.Value);
    }
    // #endregion reset
}
