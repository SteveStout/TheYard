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
        // ShortCircuit() marks the endpoint with the framework's own metadata type.
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
        // A store other than the default warms on its first read here, because the
        // background warm and the keep-warm loop are off in a test host. So each store is
        // read once by name, and only then is every store ready.
        var client = Client();
        using var stores = JsonDocument.Parse(await client.GetStringAsync("/api/stores"));
        foreach (var store in stores.RootElement.GetProperty("stores").EnumerateArray())
        {
            using var read = new HttpRequestMessage(HttpMethod.Get, "/api/facets");
            read.Headers.Add(Backends.HeaderName, store.GetProperty("key").GetString());
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(read)).StatusCode);
        }

        HttpResponseMessage response;
        var waited = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            response = await client.GetAsync("/readyz?stores=all");
            if (response.StatusCode == HttpStatusCode.OK)
            {
                break;
            }
            await Task.Delay(250);
        }
        while (waited.Elapsed < TimeSpan.FromSeconds(60));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
    // #endregion probes-short-circuit

    // #region forwarded
    [Fact]
    public async Task The_visitor_is_the_second_address_from_the_right_and_a_value_the_visitor_wrote_is_never_read()
    {
        // What reaches the container: whatever the visitor sent, then the visitor as the
        // edge saw them, then the edge as App Service's front end saw it.
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/arrival");
        request.Headers.Add("X-Admin-Key", KeyedHost.Key);
        request.Headers.Add("X-Forwarded-For", "6.6.6.6, 203.0.113.7, 10.1.2.3");
        request.Headers.Add("X-Forwarded-Proto", "http, https, https");
        request.Headers.Add("X-Forwarded-Host", "evil.example.test");
        var response = await Client().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var arrival = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("203.0.113.7", arrival.RootElement.GetProperty("address").GetString());
        Assert.Equal("https", arrival.RootElement.GetProperty("scheme").GetString());
        // What the visitor wrote is left where it was, unread.
        Assert.Equal("6.6.6.6", arrival.RootElement.GetProperty("forwarded_for").GetString());
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
