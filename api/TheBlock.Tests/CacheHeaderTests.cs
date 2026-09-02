using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheBlock.Tests;

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

    [Fact]
    public async Task The_photo_set_keeps_its_one_day_rule()
    {
        var response = await _client.GetAsync("/api/images/coupe-01.jpg");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("public, max-age=86400", CacheControl(response));
    }
}
