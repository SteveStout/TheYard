using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TheYard.Api;
using TheYard.Application;
using TheYard.Infrastructure.Cosmos;

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
    /// <summary>A sender made of a list, so the emailed half can be held without a mailbox.</summary>
    public sealed class RecordingSender : IEmailSender
    {
        public readonly List<(string To, string Subject, string Text)> Sent = [];

        public bool Configured => true;

        public string Reason => "recorded";

        public Task<bool> SendAsync(string to, string subject, string text, CancellationToken cancellation)
        {
            Sent.Add((to, subject, text));
            return Task.FromResult(true);
        }
    }

    public sealed class KeyedHost : WebApplicationFactory<Program>
    {
        public const string Key = "the-reset-test-key";
        public readonly RecordingSender Sender = new();

        /// <summary>The site as a visitor reaches it, which is what a link must carry and the test host's own address is not.</summary>
        public const string SiteUrl = "https://yard.example.test";

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Admin:Key", Key);
            builder.UseSetting("Site:Url", SiteUrl + "/");
            builder.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(Sender));
        }
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

    [Fact]
    public async Task Forgot_password_mails_the_same_link_to_a_known_address_and_the_same_sentence_to_any()
    {
        var client = Client();
        string email = await RegisterAsync(client, $"forgot-{Guid.NewGuid():N}@example.com", "first password");
        int before = _host.Sender.Sent.Count;

        var known = await client.PostAsJsonAsync("/api/auth/forgot", new { email });
        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
        string sentence = await known.Content.ReadAsStringAsync();
        Assert.Contains("on its way", sentence);
        var mail = Assert.Single(_host.Sender.Sent.Skip(before));
        Assert.Equal(email, mail.To);
        Assert.Contains("?view=account&reset=", mail.Text);

        // A stranger's address gets the same sentence and no mail.
        var unknown = await client.PostAsJsonAsync("/api/auth/forgot", new { email = $"nobody-{Guid.NewGuid():N}@example.com" });
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        Assert.Equal(sentence, await unknown.Content.ReadAsStringAsync());
        Assert.Single(_host.Sender.Sent.Skip(before));

        // The same address again inside five minutes: the sentence, no second mail.
        await client.PostAsJsonAsync("/api/auth/forgot", new { email });
        Assert.Single(_host.Sender.Sent.Skip(before));

        // The mailed link is the reset link: it sets the password and signs in.
        string url = mail.Text.Split('\n').First(line => line.Contains("reset=", StringComparison.Ordinal)).Trim();
        string token = Uri.UnescapeDataString(url[(url.IndexOf("reset=", StringComparison.Ordinal) + 6)..]);
        var reset = await client.PostAsJsonAsync("/api/auth/reset", new { token, password = "mailed password" });
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "mailed password" })).StatusCode);
    }
    // #endregion reset

    // #region link-shape
    /// <summary>
    /// What a link looks like (14 September, after the first emailed one
    /// carried the origin's host and the whole signed token): the site's own
    /// address from Site:Url, and a plain GUID after reset=. The token never
    /// appears in the link, and the GUID names nothing once it is used.
    /// </summary>
    [Fact]
    public async Task A_link_is_the_sites_own_address_and_a_plain_guid_and_never_the_token()
    {
        var client = Client();
        string email = await RegisterAsync(client, $"shape-{Guid.NewGuid():N}@example.com", "first password");
        int before = _host.Sender.Sent.Count;

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/reset-links") { Content = JsonContent.Create(new { email }) };
        request.Headers.Add("X-Admin-Key", KeyedHost.Key);
        var minted = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, minted.StatusCode);
        using var json = JsonDocument.Parse(await minted.Content.ReadAsStringAsync());
        string url = json.RootElement.GetProperty("url").GetString()!;
        Assert.StartsWith(KeyedHost.SiteUrl + "/?view=account&reset=", url);
        string id = url[(url.IndexOf("reset=", StringComparison.Ordinal) + 6)..];
        Assert.True(Guid.TryParseExact(id, "D", out _), $"the link carries {id}, which is not a GUID");
        Assert.DoesNotContain(".", id);

        // The emailed one is the same shape, and nothing in the mail is a token.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/forgot", new { email })).StatusCode);
        var mail = Assert.Single(_host.Sender.Sent.Skip(before));
        string mailed = mail.Text.Split('\n').First(line => line.Contains("reset=", StringComparison.Ordinal)).Trim();
        Assert.StartsWith(KeyedHost.SiteUrl + "/?view=account&reset=", mailed);
        Assert.True(Guid.TryParseExact(mailed[(mailed.IndexOf("reset=", StringComparison.Ordinal) + 6)..], "D", out _));
        Assert.DoesNotContain("eyJ", mail.Text);

        // Used once, the GUID names nothing; a GUID never minted names nothing either.
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/reset", new { token = id, password = "second password" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/reset", new { token = id, password = "third password" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/reset", new { token = Guid.NewGuid().ToString("D"), password = "third password" })).StatusCode);
    }
    // #endregion link-shape
}

/// <summary>
/// The port that keeps a link's token under its GUID, in memory: the shape
/// every implementation has to have, held where a clock can be moved.
/// </summary>
public class ResetLinksTests
{
    [Fact]
    public async Task A_kept_token_comes_back_once_by_its_guid_and_expires_on_the_clock()
    {
        var clock = new MovableClock { Now = new DateTimeOffset(2026, 9, 14, 15, 0, 0, TimeSpan.Zero) };
        var links = new MemoryResetLinks(clock);

        string id = await links.KeepAsync("the token", TimeSpan.FromHours(1), CancellationToken.None);
        Assert.True(ResetLinkIds.IsOne(id));
        Assert.Equal("the token", await links.ReadAsync(id, CancellationToken.None));
        Assert.Null(await links.ReadAsync(Guid.NewGuid().ToString("D"), CancellationToken.None));
        Assert.Null(await links.ReadAsync("not a guid", CancellationToken.None));

        Assert.True(await links.ForgetAsync(id, CancellationToken.None));
        Assert.False(await links.ForgetAsync(id, CancellationToken.None));
        Assert.Null(await links.ReadAsync(id, CancellationToken.None));

        string later = await links.KeepAsync("another", TimeSpan.FromHours(1), CancellationToken.None);
        clock.Now += TimeSpan.FromMinutes(61);
        Assert.Null(await links.ReadAsync(later, CancellationToken.None));
    }

    /// <summary>A clock the test moves by hand; no package for a fake one, on the rule that a pinned graph is a decision.</summary>
    private sealed class MovableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}

/// <summary>
/// The sender is built from two plain settings and the store's credential
/// rule (ADR: A second store on Cosmos DB, and what it costs, the addendum on
/// the pin's first catch): a placeholder or a blank means no sender and no
/// credential is asked for; two usable values mean one sender, one credential.
/// </summary>
public class EmailSenderTests
{
    // #region sender
    [Theory]
    [InlineData(null, null)]
    [InlineData("", "DoNotReply@example.azurecomm.net")]
    [InlineData("__EMAIL_ENDPOINT__", "DoNotReply@example.azurecomm.net")]
    [InlineData("https://acs.example.communication.azure.com", "__EMAIL_FROM__")]
    [InlineData("not a url", "DoNotReply@example.azurecomm.net")]
    public void A_missing_or_placeholder_setting_means_no_sender_and_no_credential(string? endpoint, string? from)
    {
        int asked = 0;
        var sender = AcsEmailSender.FromConfiguration(endpoint, from, () => { asked++; return CosmosStore.CredentialFor("azure-cli", ""); }, NullLogger<AcsEmailSender>.Instance);

        Assert.Same(NullEmailSender.Instance, sender);
        Assert.False(sender.Configured);
        Assert.Equal(0, asked);
    }

    [Fact]
    public void Two_usable_settings_mean_one_sender_with_the_stores_credential()
    {
        int asked = 0;
        var sender = AcsEmailSender.FromConfiguration(
            " https://acs.example.communication.azure.com ",
            " DoNotReply@example.azurecomm.net ",
            () => { asked++; return CosmosStore.CredentialFor("azure-cli", ""); },
            NullLogger<AcsEmailSender>.Instance);

        Assert.IsType<AcsEmailSender>(sender);
        Assert.True(sender.Configured);
        Assert.Equal("sent as DoNotReply@example.azurecomm.net", sender.Reason);
        Assert.Equal(1, asked);
    }

    [Fact]
    public void The_credential_rule_is_the_identity_when_the_setting_says_so_and_the_cli_otherwise()
    {
        Assert.Equal("ManagedIdentityCredential", CosmosStore.CredentialFor("managed-identity", "00000000-0000-0000-0000-000000000000").GetType().Name);
        Assert.Equal("ManagedIdentityCredential", CosmosStore.CredentialFor("Managed-Identity", "00000000-0000-0000-0000-000000000000").GetType().Name);
        Assert.Equal("AzureCliCredential", CosmosStore.CredentialFor("azure-cli", "").GetType().Name);
        Assert.Equal("AzureCliCredential", CosmosStore.CredentialFor("", "").GetType().Name);
    }
    // #endregion sender
}

/// <summary>A site with no sender says so, and the operator's link is the way.</summary>
public class ForgotWithoutASenderTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ForgotWithoutASenderTests(WebApplicationFactory<Program> host)
    {
        _client = host.CreateClient();
    }

    [Fact]
    public async Task Forgot_password_is_a_503_with_a_sentence_when_nothing_can_send()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/forgot", new { email = "someone@example.com" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("cannot send email", await response.Content.ReadAsStringAsync());
    }
}
