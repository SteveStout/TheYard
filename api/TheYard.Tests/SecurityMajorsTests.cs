using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Domain;
using TheYard.Infrastructure;

namespace TheYard.Tests;

/// <summary>
/// What a stranger can cause, held where the 2026-10-18 review found it open: a bid with no
/// ceiling, two admin routes anybody could call, the operator's key in an address, and a session
/// nothing could end before its year was up.
/// </summary>
public class SecurityMajorsTests(LogEndpointTests.KeyedHost host) : IClassFixture<LogEndpointTests.KeyedHost>
{
    // #region bid-ceiling
    [Fact]
    public void A_bid_at_int_max_is_refused_and_no_price_under_the_ceiling_overflows()
    {
        var clock = TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4)));
        string id = Enumerable.Range(0, 1000).Select(i => $"cap-{i}").First(candidate => AuctionSchedule.StatusFor(candidate, clock) == AuctionStatus.Live);
        var vehicle = TestData.Vehicle(id: id, currentBid: 22_800);

        var refused = BidRules.ResolveBid(vehicle, int.MaxValue, clock, sold: false);
        Assert.Equal(BidOutcomeKind.Rejected, refused.Kind);
        Assert.Equal("A bid can be at most $10,000,000.", refused.Reason);

        var atTheCeiling = TestData.Vehicle(id: id, currentBid: BidRules.MaxBid);
        Assert.Equal(BidRules.MaxBid + 500, BidRules.MinNextBid(atTheCeiling));
    }
    // #endregion bid-ceiling

    // #region admin-routes
    [Fact]
    public async Task The_self_test_and_the_page_sweep_refuse_a_stranger()
    {
        var stranger = host.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.GetAsync("/api/admin/selftest/exception")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stranger.PostAsync("/api/admin/pages", null)).StatusCode);
    }

    [Fact]
    public async Task The_self_test_runs_once_a_minute_per_account()
    {
        var signedIn = await Buyers.SignedIn(host);
        Assert.Equal(HttpStatusCode.InternalServerError, (await signedIn.GetAsync("/api/admin/selftest/exception")).StatusCode);
        var again = await signedIn.GetAsync("/api/admin/selftest/exception");
        Assert.Equal(HttpStatusCode.TooManyRequests, again.StatusCode);
        Assert.Equal("application/problem+json", again.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_operator_key_is_read_from_the_header_and_never_from_the_address()
    {
        var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/admin/logs/kept?key={LogEndpointTests.KeyedHost.Key}")).StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/logs/kept");
        request.Headers.Add(AdminKey.HeaderName, LogEndpointTests.KeyedHost.Key);
        Assert.NotEqual(HttpStatusCode.NotFound, (await client.SendAsync(request)).StatusCode);
    }
    // #endregion admin-routes

    // #region session-revocation
    [Fact]
    public async Task A_copied_cookie_stops_working_once_its_owner_signs_out()
    {
        var (client, cookie, _) = await Registered();
        Assert.True(await SignedIn(client, cookie));

        using (var leaving = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout"))
        {
            leaving.Headers.Add("Cookie", cookie);
            await client.SendAsync(leaving);
        }

        Assert.False(await SignedIn(client, cookie));
    }

    [Fact]
    public async Task A_password_change_ends_every_session_opened_before_it()
    {
        var (client, cookie, email) = await Registered();
        Assert.True(await SignedIn(client, cookie));

        using (var scope = host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<YardUser>>();
            var user = (await users.FindByEmailAsync(email))!;
            Assert.True((await users.RemovePasswordAsync(user)).Succeeded);
            Assert.True((await users.AddPasswordAsync(user, "a new password")).Succeeded);
        }

        Assert.False(await SignedIn(client, cookie));
    }

    /// <summary>A new account, the cookie its registration set, and its email, on a client that keeps no cookies of its own.</summary>
    private async Task<(HttpClient Client, string Cookie, string Email)> Registered()
    {
        var client = host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        string email = $"revoke-{Guid.NewGuid():N}@example.com";
        var registered = await client.PostAsJsonAsync("/api/auth/register", new { email, password = "correct horse" });
        registered.EnsureSuccessStatusCode();
        string setCookie = registered.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(TokenIssuer.CookieName + "=", StringComparison.Ordinal));
        return (client, setCookie.Split(';')[0], email);
    }

    /// <summary>Whether /api/auth/me reads the cookie as a signed-in session.</summary>
    private static async Task<bool> SignedIn(HttpClient client, string cookie)
    {
        using var me = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        me.Headers.Add("Cookie", cookie);
        using var json = JsonDocument.Parse(await (await client.SendAsync(me)).Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("signed_in").GetBoolean();
    }
    // #endregion session-revocation
}
