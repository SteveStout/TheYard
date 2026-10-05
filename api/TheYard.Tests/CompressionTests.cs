using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api;

namespace TheYard.Tests;

/// <summary>
/// The catalogue's two reads leave compressed, and nothing else does (ADR:
/// Cache headers, the addendum on compression). A hundred vehicles are about
/// 106 KB of JSON; the edge forwards every API read, so the hop from Azure
/// carried the whole of it until the container compressed it.
/// </summary>
public class CompressionTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<HttpResponseMessage> GetAsync(string path, string? acceptEncoding)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (acceptEncoding is not null)
        {
            request.Headers.TryAddWithoutValidation("Accept-Encoding", acceptEncoding);
        }

        return await _client.SendAsync(request);
    }

    private static string Decoded(byte[] body, string encoding)
    {
        using Stream input = new MemoryStream(body);
        using Stream reader = encoding == "br" ? new BrotliStream(input, CompressionMode.Decompress) : new GZipStream(input, CompressionMode.Decompress);
        using var text = new StreamReader(reader);
        return text.ReadToEnd();
    }

    [Theory]
    [InlineData("/api/vehicles", "br")]
    [InlineData("/api/vehicles", "gzip")]
    [InlineData("/api/facets", "br")]
    public async Task The_catalogue_reads_leave_compressed_and_say_so(string path, string encoding)
    {
        var plain = await GetAsync(path, null);
        var packed = await GetAsync(path, encoding);

        Assert.Equal(HttpStatusCode.OK, packed.StatusCode);
        Assert.Equal(encoding, Assert.Single(packed.Content.Headers.ContentEncoding));
        Assert.Contains("Accept-Encoding", packed.Headers.Vary);
        byte[] body = await packed.Content.ReadAsByteArrayAsync();
        byte[] whole = await plain.Content.ReadAsByteArrayAsync();
        Assert.True(body.Length < whole.Length, $"{path} as {encoding} is {body.Length} bytes against {whole.Length} plain");
        // The same JSON either way, so nothing a reader parses changed shape.
        using var json = JsonDocument.Parse(Decoded(body, encoding));
        Assert.Equal(JsonValueKind.Object, json.RootElement.ValueKind);
    }

    [Fact]
    public async Task A_page_of_vehicles_is_less_than_a_quarter_of_its_size_as_brotli()
    {
        var plain = await GetAsync("/api/vehicles", null);
        var packed = await GetAsync("/api/vehicles", "br");
        long whole = (await plain.Content.ReadAsByteArrayAsync()).LongLength;
        long sent = (await packed.Content.ReadAsByteArrayAsync()).LongLength;

        Assert.True(sent * 4 < whole, $"{sent} bytes as Brotli against {whole} plain");
    }

    [Theory]
    [InlineData("/api/version")]
    [InlineData("/api/health")]
    [InlineData("/api/auth/me")]
    [InlineData("/api/bids/history")]
    public async Task Every_other_address_leaves_as_it_was(string path)
    {
        var response = await GetAsync(path, "br, gzip");

        Assert.Empty(response.Content.Headers.ContentEncoding);
    }

    [Fact]
    public async Task A_caller_that_asks_for_no_encoding_gets_plain_json()
    {
        var response = await GetAsync("/api/vehicles", null);

        Assert.Empty(response.Content.Headers.ContentEncoding);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("total").GetInt32() > 0);
    }

    [Theory]
    [InlineData("GET", "/api/vehicles", true)]
    [InlineData("GET", "/API/Facets", true)]
    [InlineData("POST", "/api/vehicles", false)]
    [InlineData("GET", "/api/vehicles/1", false)]
    [InlineData("GET", "/api/vehicles/1/bids", false)]
    [InlineData("GET", "/api/bids", false)]
    public void Only_the_two_reads_are_covered(string method, string path, bool covered)
    {
        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;

        Assert.Equal(covered, CatalogueReads.Covers(context.Request));
    }
}
