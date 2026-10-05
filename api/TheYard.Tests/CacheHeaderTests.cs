using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// Cache headers (ADR-015): anything that can change under the same address
/// says no-cache, a hashed bundle file may be kept for a year but only when
/// it exists, and the photo set keeps its one-day rule.
/// </summary>
public class CacheHeaderTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    /// <summary>The header as sent, not as parsed, so the assertion reads like the wire.</summary>
    private static string CacheControl(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Cache-Control", out var values) ? string.Join(", ", values) : "";

    [Theory]
    [InlineData("/api/version")]
    [InlineData("/api/health")]
    [InlineData("/api/facets")]
    [InlineData("/api/docs/practices")]
    [InlineData("/api/docs/changelog")]
    public async Task Changing_addresses_say_no_cache(string path)
    {
        var response = await _client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-cache", CacheControl(response));
    }

    [Fact]
    public async Task A_missing_bundle_file_is_not_remembered_for_a_year()
    {
        var response = await _client.GetAsync("/assets/index-doesnotexist.js");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-cache", CacheControl(response));
    }

    /// <summary>One response header as sent, or an empty string when there is none.</summary>
    private static string Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? string.Join(", ", values) : "";

    private async Task<HttpResponseMessage> GetAsync(string path, string? cookie = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (cookie is not null)
        {
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
        }

        return await _client.SendAsync(request);
    }

    /// <summary>
    /// The listing and the filter values may be kept by the edge for fifteen
    /// seconds, per query string and per store, while the browser is still told
    /// to ask every time (ADR-015, the addendum on the edge's copy).
    /// </summary>
    [Theory]
    [InlineData("/api/vehicles")]
    [InlineData("/api/vehicles?make=Ford&limit=10")]
    [InlineData("/api/facets")]
    public async Task The_catalogue_reads_may_be_kept_by_the_edge_and_the_browser_still_asks(string path)
    {
        var response = await GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-cache", CacheControl(response));
        Assert.Equal(CatalogueReads.EdgeRule, Header(response, "Netlify-CDN-Cache-Control"));
        Assert.Equal("query", Header(response, "Netlify-Vary"));
        Assert.Contains(Backends.HeaderName, response.Headers.Vary);
    }

    [Theory]
    [InlineData("/api/version")]
    [InlineData("/api/health")]
    [InlineData("/api/stores")]
    [InlineData("/api/vehicles?sort=alphabetical")]
    public async Task Nothing_else_is_kept_by_the_edge(string path)
    {
        var response = await GetAsync(path);

        Assert.Equal("", Header(response, "Netlify-CDN-Cache-Control"));
    }

    [Fact]
    public async Task A_request_that_carries_a_cookie_is_never_kept_by_the_edge()
    {
        var response = await GetAsync("/api/vehicles", "theyard-session=not-a-real-token");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("", Header(response, "Netlify-CDN-Cache-Control"));
    }

    [Fact]
    public async Task The_photo_set_keeps_its_one_day_rule()
    {
        var response = await _client.GetAsync("/api/images/coupe-01.jpg");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=86400", CacheControl(response));
    }
}
