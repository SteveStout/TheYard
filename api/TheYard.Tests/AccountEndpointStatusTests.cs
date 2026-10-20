using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api;

namespace TheYard.Tests;

// #region account-endpoint-status-tests
/// <summary>
/// Every status code the Accounts slice's routes declare, one case each through the real host (ADR:
/// The onion cut into vertical slices, rule 7): on the keyed host with a sender, on a host whose
/// allowance is one account, and on a host whose store did not come up.
/// </summary>
public sealed class AccountEndpointStatusTests(PasswordResetTests.KeyedHost host) : IClassFixture<PasswordResetTests.KeyedHost>
{
    private static string Address() => $"status-{Guid.NewGuid():N}@example.com";

    private static StringContent NotJson() => new("{ this is not json", Encoding.UTF8, "application/json");

    [Fact]
    [Covers("POST", "/api/auth/register", 200)]
    [Covers("POST", "/api/auth/register", 400)]
    [Covers("POST", "/api/auth/login", 200)]
    [Covers("POST", "/api/auth/login", 400)]
    [Covers("POST", "/api/auth/login", 401)]
    [Covers("POST", "/api/auth/logout", 200)]
    [Covers("GET", "/api/auth/me", 200)]
    public async Task Register_sign_in_sign_out_and_who_am_i_answer_each_status_they_declare()
    {
        var client = host.CreateClient();
        string email = Address();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", new { email, password = "short" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/register", new { email, password = "correct horse" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/logout", content: null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "not the password" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/auth/login", NotJson())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/login", new { email, password = "correct horse" })).StatusCode);
    }

    [Fact]
    [Covers("POST", "/api/auth/forgot", 200)]
    [Covers("POST", "/api/auth/forgot", 400)]
    [Covers("POST", "/api/auth/reset", 200)]
    [Covers("POST", "/api/auth/reset", 400)]
    [Covers("POST", "/api/admin/reset-links", 200)]
    [Covers("POST", "/api/admin/reset-links", 404)]
    public async Task The_reset_link_is_mailed_and_used_and_a_bad_one_is_refused()
    {
        var client = host.CreateClient();
        string email = Address();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/register", new { email, password = "correct horse" })).StatusCode);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/forgot", new { email = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/forgot", new { email })).StatusCode);

        // The operator's link: 404 without the key (the route is not there for anyone else) and
        // for an address with no account; 200 with the key for an address that has one.
        using var unkeyed = new HttpRequestMessage(HttpMethod.Post, "/api/admin/reset-links") { Content = JsonContent.Create(new { email }) };
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(unkeyed)).StatusCode);
        using var nobody = new HttpRequestMessage(HttpMethod.Post, "/api/admin/reset-links") { Content = JsonContent.Create(new { email = Address() }) };
        nobody.Headers.Add("X-Admin-Key", PasswordResetTests.KeyedHost.Key);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(nobody)).StatusCode);
        using var mint = new HttpRequestMessage(HttpMethod.Post, "/api/admin/reset-links") { Content = JsonContent.Create(new { email }) };
        mint.Headers.Add("X-Admin-Key", PasswordResetTests.KeyedHost.Key);
        using var minted = await client.SendAsync(mint);
        Assert.Equal(HttpStatusCode.OK, minted.StatusCode);
        using var link = JsonDocument.Parse(await minted.Content.ReadAsStringAsync());
        string id = new Uri(link.RootElement.GetProperty("url").GetString()!).Query.Split("reset=")[1];

        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/reset", new { token = "not-a-link", password = "another horse" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/reset", new { token = id, password = "another horse" })).StatusCode);
    }

    [Fact]
    [Covers("POST", "/api/auth/register", 429)]
    public async Task A_registration_past_the_hours_allowance_is_429()
    {
        await using var one = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.UseSetting("Accounts:RegistrationsPerHour", "1"));
        var client = one.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/auth/register", new { email = Address(), password = "correct horse" })).StatusCode);
        Assert.Equal((HttpStatusCode)429, (await client.PostAsJsonAsync("/api/auth/register", new { email = Address(), password = "correct horse" })).StatusCode);
    }

    [Fact]
    [Covers("POST", "/api/auth/register", 503)]
    [Covers("POST", "/api/auth/login", 503)]
    [Covers("POST", "/api/auth/forgot", 503)]
    [Covers("POST", "/api/auth/reset", 503)]
    [Covers("POST", "/api/admin/reset-links", 503)]
    public async Task Every_account_route_is_503_where_the_store_did_not_come_up()
    {
        // A file standing where the database's folder would need to be: the store refuses, and the
        // site serves the catalogue from files with no accounts (the fallback PersistenceTests holds).
        string blocker = Path.Combine(Path.GetTempPath(), $"theyard-blocker-{Guid.NewGuid():N}");
        await File.WriteAllTextAsync(blocker, "a file, standing where a directory would need to be");
        try
        {
            await using var down = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:Yard", $"Data Source={Path.Combine(blocker, "yard.db")}");
                builder.UseSetting("Cosmos:AccountEndpoint", "");
                builder.UseSetting(StorePrepare.FirstWaitSetting, "0");
                builder.UseSetting("Admin:Key", PasswordResetTests.KeyedHost.Key);
            });
            var client = down.CreateClient();
            var credentials = new { email = Address(), password = "correct horse" };
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/auth/register", credentials)).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/auth/login", credentials)).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/auth/forgot", new { email = credentials.email })).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.PostAsJsonAsync("/api/auth/reset", new { token = "x", password = "another horse" })).StatusCode);
            using var mint = new HttpRequestMessage(HttpMethod.Post, "/api/admin/reset-links") { Content = JsonContent.Create(new { email = credentials.email }) };
            mint.Headers.Add("X-Admin-Key", PasswordResetTests.KeyedHost.Key);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.SendAsync(mint)).StatusCode);
        }
        finally
        {
            File.Delete(blocker);
        }
    }
}
// #endregion account-endpoint-status-tests
