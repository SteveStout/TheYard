using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// A login lasts a year past the last visit (ADR: Accounts and per-user bids,
/// addendum): the token is a year long, and a request that arrives with a
/// token more than a day into its life gets a fresh one. Held here on a real
/// host: the rule without a request, then the cookie on the wire.
/// </summary>
public class SessionRenewalTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _host;

    public SessionRenewalTests(WebApplicationFactory<Program> host)
    {
        _host = host;
    }

    // #region rule
    [Fact]
    public void A_session_is_a_year_and_renews_once_a_day_of_use()
    {
        var issuer = _host.Services.GetRequiredService<TokenIssuer>();
        Assert.Equal(TimeSpan.FromDays(365), issuer.Lifetime);

        var now = DateTimeOffset.UtcNow;
        string fresh = (now + issuer.Lifetime).ToUnixTimeSeconds().ToString();
        string aDayOld = (now + issuer.Lifetime - TimeSpan.FromDays(1)).ToUnixTimeSeconds().ToString();
        string anHourOld = (now + issuer.Lifetime - TimeSpan.FromHours(1)).ToUnixTimeSeconds().ToString();
        Assert.False(issuer.ShouldRenew(fresh, now));
        Assert.False(issuer.ShouldRenew(anHourOld, now));
        Assert.True(issuer.ShouldRenew(aDayOld, now));
        Assert.False(issuer.ShouldRenew(null, now));
        Assert.False(issuer.ShouldRenew("not a number", now));
    }
    // #endregion rule

    // #region wire
    [Fact]
    public async Task A_request_with_a_token_days_into_its_life_gets_a_fresh_cookie_and_a_fresh_one_does_not()
    {
        var issuer = _host.Services.GetRequiredService<TokenIssuer>();
        // Cookies by hand: the factory's own jar would send the renewed cookie
        // back on the next request and muddle what each request carried.
        var client = _host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        // Two days of lifetime left on a year-long session: issued 363 days ago, as far as the rule can tell.
        using var old = new HttpRequestMessage(HttpMethod.Get, "/api/version");
        old.Headers.Add("Cookie", $"{TokenIssuer.CookieName}={issuer.Issue("user-1", "someone", "sql", TimeSpan.FromDays(2))}");
        var renewed = await client.SendAsync(old);
        Assert.True(renewed.IsSuccessStatusCode);
        Assert.True(renewed.Headers.TryGetValues("Set-Cookie", out var cookies), "a token days into its life should be re-issued");
        var cookie = Assert.Single(cookies!, c => c.StartsWith(TokenIssuer.CookieName + "=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("max-age=" + (int)issuer.Lifetime.TotalSeconds, cookie, StringComparison.OrdinalIgnoreCase);

        using var young = new HttpRequestMessage(HttpMethod.Get, "/api/version");
        young.Headers.Add("Cookie", $"{TokenIssuer.CookieName}={issuer.Issue("user-1", "someone", "sql")}");
        var kept = await client.SendAsync(young);
        Assert.True(kept.IsSuccessStatusCode);
        Assert.False(kept.Headers.TryGetValues("Set-Cookie", out _), "a fresh token is left alone");

        // Signing out is never a renewal, whatever the token's age: the one
        // cookie the response sets is the empty one that ends the session.
        using var leaving = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        leaving.Headers.Add("Cookie", $"{TokenIssuer.CookieName}={issuer.Issue("user-1", "someone", "sql", TimeSpan.FromDays(2))}");
        var gone = await client.SendAsync(leaving);
        Assert.True(gone.Headers.TryGetValues("Set-Cookie", out var deleting));
        var ended = Assert.Single(deleting!, c => c.StartsWith(TokenIssuer.CookieName + "=", StringComparison.Ordinal));
        Assert.StartsWith(TokenIssuer.CookieName + "=;", ended);
    }
    // #endregion wire
}
