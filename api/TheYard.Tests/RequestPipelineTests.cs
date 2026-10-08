using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// The order of the request pipeline (ADR: The order of the request pipeline), held by
/// what each placement does rather than by the order of the lines: a file is answered
/// before any session is read, the health probes are answered by routing alone, the
/// visitor and the scheme come from the forwarded headers counted from the right and
/// never from a value a visitor wrote, and the session cookie is Secure exactly when
/// the visitor's own request was HTTPS.
/// </summary>
public class RequestPipelineTests(RequestPipelineTests.KeyedHost host) : IClassFixture<RequestPipelineTests.KeyedHost>
{
    /// <summary>A host with the operator's key, for the arrival read.</summary>
    public sealed class KeyedHost : WebApplicationFactory<Program>
    {
        /// <summary>The operator's key on this host.</summary>
        public const string Key = "the-pipeline-test-key";

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.UseSetting("Admin:Key", Key);
    }

    private HttpClient Client() => host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

    // #region files-first
    [Fact]
    public async Task A_photo_is_answered_by_the_files_before_any_session_is_read()
    {
        // A token two days from its end would be renewed by any request that reaches the
        // session middleware under /api. The photos live under /api/images and are
        // answered by the files, which sit above it, so the photo comes back with its
        // own one-day rule and no new cookie.
        var issuer = host.Services.GetRequiredService<TokenIssuer>();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/images/coupe-01.jpg");
        request.Headers.Add("Cookie", $"{TokenIssuer.CookieName}={issuer.Issue("user-1", "someone", "sql", TimeSpan.FromDays(2))}");
        var response = await Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=86400", response.Headers.CacheControl?.ToString());
        Assert.False(response.Headers.Contains("Set-Cookie"), "a file is served before the session middleware, so nothing renews it");
    }
    // #endregion files-first

    // #region probes-short-circuit
    [Theory]
    [InlineData("/healthz")]
    [InlineData("/readyz")]
    public async Task A_health_probe_is_answered_by_routing_and_goes_no_further(string path)
    {
        var endpoint = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Single(candidate => candidate.RoutePattern.RawText == path);
        // ShortCircuit() marks the endpoint with the framework's own metadata type, which is
        // internal, so it is matched by name.
        Assert.Contains(endpoint.Metadata, metadata => metadata.GetType().Name == "ShortCircuitMetadata");

        // And it still answers: liveness at once, readiness once the catalogue is in.
        HttpResponseMessage response;
        var waited = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            response = await Client().GetAsync(path);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                break;
            }
            await Task.Delay(250);
        }
        while (waited.Elapsed < TimeSpan.FromSeconds(60));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_on_every_store_waits_for_every_store_this_host_runs()
    {
        // A host of its own, so no other test has read its stores. A store other than the
        // default warms only on its first read here, because the background warm and the
        // keep-warm loop are off in a test host. Where the host runs two stores (the gate's
        // Cosmos DB pass), every store is not ready until the second has been read; where
        // it runs one, every store is the default.
        await using var fresh = new KeyedHost();
        var client = fresh.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        await Ready(client, "/readyz");
        using var stores = JsonDocument.Parse(await client.GetStringAsync("/api/stores"));
        var others = stores.RootElement.GetProperty("stores").EnumerateArray()
            .Where(store => !store.GetProperty("default").GetBoolean())
            .Select(store => store.GetProperty("key").GetString()!)
            .ToList();
        if (others.Count > 0)
        {
            var notYet = await client.GetAsync("/readyz?stores=all");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, notYet.StatusCode);
            Assert.Contains("another store", await notYet.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
        foreach (string key in others)
        {
            using var read = new HttpRequestMessage(HttpMethod.Get, "/api/facets");
            read.Headers.Add(Backends.HeaderName, key);
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(read)).StatusCode);
        }
        await Ready(client, "/readyz?stores=all");
    }

    /// <summary>Asks until the address answers 200, a minute at most, and fails if it never does.</summary>
    private static async Task Ready(HttpClient client, string path)
    {
        HttpResponseMessage response;
        var waited = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            response = await client.GetAsync(path);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                return;
            }
            await Task.Delay(250);
        }
        while (waited.Elapsed < TimeSpan.FromSeconds(60));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    // #endregion probes-short-circuit

    // #region forwarded
    [Theory]
    // Through the edge: what the visitor sent, the visitor, the edge.
    [InlineData("6.6.6.6, 203.0.113.7, 10.1.2.3", "http, https, https", "203.0.113.7", "https")]
    // App Service's front end often sends one scheme for two addresses; the scheme still comes through.
    [InlineData("203.0.113.7, 10.1.2.3", "https", "203.0.113.7", "https")]
    // Straight to the origin, skipping the edge: one address, which is the caller's.
    [InlineData("198.51.100.9", "https", "198.51.100.9", "https")]
    public async Task The_visitor_is_the_second_address_from_the_right_and_a_value_the_visitor_wrote_is_never_read(
        string forwardedFor, string forwardedProto, string address, string scheme)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/arrival");
        request.Headers.Add("X-Admin-Key", KeyedHost.Key);
        request.Headers.Add("X-Forwarded-For", forwardedFor);
        request.Headers.Add("X-Forwarded-Proto", forwardedProto);
        request.Headers.Add("X-Forwarded-Host", "evil.example.test");
        var response = await Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var arrival = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(address, arrival.RootElement.GetProperty("address").GetString());
        Assert.Equal(scheme, arrival.RootElement.GetProperty("scheme").GetString());
        // The read shows the header as it arrived, so a wrong hop count is visible live.
        Assert.Equal(forwardedFor, arrival.RootElement.GetProperty("arrived_for").GetString());
    }

    [Fact]
    public async Task A_reset_link_on_a_host_with_no_site_address_takes_its_own_host_and_never_a_forwarded_one()
    {
        // This host has no Site:Url, which is the only case the link was ever built from
        // headers; X-Forwarded-Host must not reach a link that goes out in an email.
        var client = Client();
        string email = $"reset-{Guid.NewGuid():N}@example.com";
        Assert.True((await client.PostAsJsonAsync("/api/auth/register", new { email, password = "correct horse" })).IsSuccessStatusCode);
        using var mint = new HttpRequestMessage(HttpMethod.Post, "/api/admin/reset-links") { Content = JsonContent.Create(new { email }) };
        mint.Headers.Add("X-Admin-Key", KeyedHost.Key);
        mint.Headers.Add("X-Forwarded-Host", "evil.example.test");
        mint.Headers.Add("X-Forwarded-Proto", "https");
        var minted = await client.SendAsync(mint);
        Assert.Equal(HttpStatusCode.OK, minted.StatusCode);
        using var json = JsonDocument.Parse(await minted.Content.ReadAsStringAsync());
        string url = json.RootElement.GetProperty("url").GetString()!;
        Assert.DoesNotContain("evil", url, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("https://localhost", url, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_arrival_read_is_a_404_without_the_key()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await Client().GetAsync("/api/admin/arrival")).StatusCode);
    }

    [Fact]
    public async Task The_session_cookie_is_Secure_when_the_visitors_own_request_was_https_and_not_otherwise()
    {
        var client = Client();
        using var overTls = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new { email = $"tls-{Guid.NewGuid():N}@example.com", password = "correct horse" }),
        };
        overTls.Headers.Add("X-Forwarded-For", "203.0.113.7, 10.1.2.3");
        overTls.Headers.Add("X-Forwarded-Proto", "https, https");
        var secured = await client.SendAsync(overTls);
        Assert.True(secured.IsSuccessStatusCode, $"registering over TLS answered {(int)secured.StatusCode}");
        Assert.Contains(secured.Headers.GetValues("Set-Cookie"), cookie => cookie.Contains("secure", StringComparison.OrdinalIgnoreCase));

        var plain = await client.PostAsJsonAsync("/api/auth/register", new { email = $"plain-{Guid.NewGuid():N}@example.com", password = "correct horse" });
        Assert.True(plain.IsSuccessStatusCode, $"registering over plain HTTP answered {(int)plain.StatusCode}");
        Assert.DoesNotContain(plain.Headers.GetValues("Set-Cookie"), cookie => cookie.Contains("secure", StringComparison.OrdinalIgnoreCase));
    }
    // #endregion forwarded
}
