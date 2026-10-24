using System.Net;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Api.Features.Blog;
using TheYard.Application.Features.Blog;
using TheYard.Infrastructure.Features.Blog;

namespace TheYard.Tests;

// #region blog-image-tests
/// <summary>
/// The blog's pictures (ADR-113, the addendum on where the pictures live): the name rule, the query with the
/// memory store and no host, the host's memory answering the second read, the container adapter over recorded
/// blob service answers, and the route's 200 with the year's cache and its 404 problem.
/// </summary>
public sealed class BlogImageTests
{
    internal static readonly byte[] Webp = Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 a small picture");

    [Fact]
    public void A_name_is_sixteen_hex_digits_of_the_bytes_hash_and_its_kind_and_nothing_else_is_looked_up()
    {
        string name = BlogImageNames.For(Webp, "webp");

        Assert.Matches("^[0-9a-f]{16}\\.webp$", name);
        Assert.Equal(name, BlogImageNames.For(Webp, "webp"));
        Assert.NotEqual(name, BlogImageNames.For([.. Webp, 0], "webp"));
        Assert.Equal("image/webp", BlogImageNames.ContentType(name));
        Assert.Equal("image/jpeg", BlogImageNames.ContentType("0123456789abcdef.jpg"));
        foreach (string bad in new[] { "../etc/passwd", "0123456789ABCDEF.webp", "0123456789abcdef.gif", "0123456789abcde.png", "" })
        {
            Assert.False(BlogImageNames.IsName(bad), bad);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => BlogImageNames.For(Webp, "gif"));
        // A picture is a page's asset, never a visit (Hits' asset rule by extension).
        Assert.False(TheYard.Api.Shared.Activity.Hits.Counts($"/blog/images/{name}"));
    }

    [Fact]
    public async Task The_query_answers_a_kept_picture_and_null_for_a_name_not_kept_or_not_a_name()
    {
        var store = new MemoryBlogImageStore();
        string name = BlogImageNames.For(Webp, "webp");
        await store.PutAsync(name, new BlogImageFile(Webp, "image/webp"), CancellationToken.None);
        var handler = new ReadBlogImageHandler(store);

        Assert.Equal(Webp, (await handler.HandleAsync(new ReadBlogImageQuery(name), CancellationToken.None))!.Bytes);
        Assert.Null(await handler.HandleAsync(new ReadBlogImageQuery("0123456789abcdef.png"), CancellationToken.None));
        Assert.Null(await handler.HandleAsync(new ReadBlogImageQuery("../secret"), CancellationToken.None));
        Assert.Equal(2, store.Reads);
    }

    [Fact]
    public async Task The_host_s_memory_answers_the_second_read_and_keeps_no_miss()
    {
        var store = new MemoryBlogImageStore();
        var cached = new CachedBlogImages(store);
        string name = BlogImageNames.For(Webp, "webp");

        Assert.Null(await cached.OpenAsync(name, CancellationToken.None));
        await store.PutAsync(name, new BlogImageFile(Webp, "image/webp"), CancellationToken.None);
        Assert.NotNull(await cached.OpenAsync(name, CancellationToken.None));
        Assert.NotNull(await cached.OpenAsync(name, CancellationToken.None));

        Assert.Equal(2, store.Reads);
        Assert.Equal(32L * 1024 * 1024, CachedBlogImages.MaxBytes);
    }

    private sealed class RecordedBlobs : HttpMessageHandler
    {
        public List<HttpRequestMessage> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add(request);
            var path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Put)
            {
                return Task.FromResult(new HttpResponseMessage(path.EndsWith(".png", StringComparison.Ordinal) ? HttpStatusCode.Conflict : HttpStatusCode.Created));
            }
            return Task.FromResult(path.EndsWith("0123456789abcdef.webp", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Webp) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public async Task The_container_adapter_reads_writes_once_and_names_its_token_and_version()
    {
        var blobs = new RecordedBlobs();
        using var route = OutboundHttp.RouteTo(blobs);
        var store = new BlobBlogImageStore("stjobhuntss", "blog", _ => Task.FromResult("token-1"));

        var file = await store.OpenAsync("0123456789abcdef.webp", CancellationToken.None);
        Assert.Equal(("image/webp", Webp.Length), (file!.ContentType, file.Bytes.Length));
        Assert.Null(await store.OpenAsync("fedcba9876543210.webp", CancellationToken.None));
        await store.PutAsync("0123456789abcdef.webp", file, CancellationToken.None);
        await store.PutAsync("0123456789abcdef.png", file, CancellationToken.None);

        Assert.Equal("https://stjobhuntss.blob.core.windows.net/blog/0123456789abcdef.webp", blobs.Asked[0].RequestUri!.ToString());
        Assert.All(blobs.Asked, request => Assert.Equal("Bearer token-1", request.Headers.Authorization!.ToString()));
        Assert.All(blobs.Asked, request => Assert.Equal(BlobBlogImageStore.Version, request.Headers.GetValues("x-ms-version").Single()));
        var put = blobs.Asked[2];
        Assert.Equal(("BlockBlob", "*"), (put.Headers.GetValues("x-ms-blob-type").Single(), put.Headers.IfNoneMatch.Single().Tag));
    }

}

/// <summary>The picture route through the host: the year's cache on a kept picture and the 404 problem for any other name.</summary>
public sealed class BlogImageRouteTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static byte[] Webp => BlogImageTests.Webp;

    [Fact]
    [Covers("GET", "/blog/images/{name}", 200)]
    [Covers("GET", "/blog/images/{name}", 404)]
    public async Task The_route_serves_a_kept_picture_for_a_year_and_a_404_problem_for_any_other_name()
    {
        string name = BlogImageNames.For(Webp, "webp");
        await factory.Services.GetRequiredService<MemoryBlogImageStore>().PutAsync(name, new BlogImageFile(Webp, "image/webp"), CancellationToken.None);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/blog/images/{name}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/webp", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(BlogImageEndpoints.CacheRule, response.Headers.CacheControl?.ToString());
        Assert.Equal($"\"{name}\"", response.Headers.ETag?.Tag);
        Assert.Equal(Webp, await response.Content.ReadAsByteArrayAsync());

        var missing = await client.GetAsync("/blog/images/0123456789abcdef.png");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/problem+json", missing.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/blog/images/not-a-name.webp")).StatusCode);
    }
}
// #endregion blog-image-tests
