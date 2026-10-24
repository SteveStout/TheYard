using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using TheYard.Application.Features.Blog;
using TheYard.Data;

namespace TheYard.Tests;

// #region blog-upload-tests
/// <summary>
/// Putting a picture into a post (ADR-113, the addendum on putting a picture in): the header reader on one
/// file of each kind, the command with fakes refusing by the figure it names, keeping under the hash and
/// putting the row at its position, and the route's 401 without the key.
/// </summary>
public sealed class BlogUploadTests
{
    /// <summary>One small picture of each kind, made with Pillow: 37 by 21, the extended WebP 45 by 33 with alpha, and a PNG 2001 wide.</summary>
    internal static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAACUAAAAVCAIAAABOhrD5AAAAJklEQVR4nGPkcotioCNgoqdlo/aN2jdq36h9o/aN2jdq36h9VAAA4rYA1Hx22UsAAAAASUVORK5CYII=");
    internal static readonly byte[] Jpeg = Convert.FromBase64String("/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAYEBQYFBAYGBQYHBwYIChAKCgkJChQODwwQFxQYGBcUFhYaHSUfGhsjHBYWICwgIyYnKSopGR8tMC0oMCUoKSj/2wBDAQcHBwoIChMKChMoGhYaKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCj/wAARCAAVACUDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwDxyiiivXPmgooooAKKKKACiiigAooooAKKKKAP/9k=");
    internal static readonly byte[] Lossy = Convert.FromBase64String("UklGRkgAAABXRUJQVlA4IDwAAABwAwCdASolABUAPm02l0ikIqIhJWgAgA2JZwDNsoAALGeNWgAA/vUnX+LYpDcz/7jsDdc2wm9wmizgAAA=");
    internal static readonly byte[] Lossless = Convert.FromBase64String("UklGRh4AAABXRUJQVlA4TBEAAAAvJAAFAAdQoypUq/+BiOh/AAA=");
    internal static readonly byte[] Extended = Convert.FromBase64String("UklGRnQAAABXRUJQVlA4WAoAAAAQAAAALAAAIAAAQUxQSBAAAAABB1DAiAgACeH/eTWi/ykBVlA4ID4AAABwAwCdASotACEAPm02mEkkIqKhI4gAgA2JZwB2AAA5qac/bgAA/vUnX/4tikNzP/+xZy2BEw9P9fgjGAAAAA==");
    internal static readonly byte[] Wide = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAB9EAAAAECAIAAAC2kFinAAAALklEQVR4nO3BgQAAAADDoPlTX+AIVQEAAAAAAAAAAAAAAAAAAAAAAAAAAAAA8Axd0AAB+uYLjAAAAABJRU5ErkJggg==");

    private static readonly DateTimeOffset At = new(2026, 10, 24, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_header_reader_reads_each_kind_s_size_from_its_own_bytes_and_nothing_else()
    {
        Assert.Equal(new BlogImageHeader("png", 37, 21), BlogImageHeaders.Read(Png));
        Assert.Equal(new BlogImageHeader("jpg", 37, 21), BlogImageHeaders.Read(Jpeg));
        Assert.Equal(new BlogImageHeader("webp", 37, 21), BlogImageHeaders.Read(Lossy));
        Assert.Equal(new BlogImageHeader("webp", 37, 21), BlogImageHeaders.Read(Lossless));
        Assert.Equal(new BlogImageHeader("webp", 45, 33), BlogImageHeaders.Read(Extended));
        Assert.Null(BlogImageHeaders.Read("GIF89a not a kind we keep"u8));
        Assert.Null(BlogImageHeaders.Read([]));
    }

    private static async Task<(PutBlogImageHandler Handler, MemoryBlogStore Store, MemoryBlogImageStore Images)> SetUpAsync()
    {
        var store = new MemoryBlogStore("kept in the test's memory");
        await store.SaveAsync(new BlogPost(0, "solid-carousel", "SOLID", "Five slides.", "The body.", At, "https://www.linkedin.com/posts/x", null, [], []), At, CancellationToken.None);
        var images = new MemoryBlogImageStore();
        return (new PutBlogImageHandler(store, images, new FixedClock(At)), store, images);
    }

    [Fact]
    public async Task A_picture_is_kept_under_its_hash_and_its_row_put_at_its_position()
    {
        var (handler, store, images) = await SetUpAsync();

        var first = await handler.HandleAsync(new PutBlogImageCommand("solid-carousel", Lossy, "The first slide names the five principles.", 1), CancellationToken.None);
        var second = await handler.HandleAsync(new PutBlogImageCommand("solid-carousel", Png, "The second slide shows one class with one job.", 2), CancellationToken.None);
        var again = await handler.HandleAsync(new PutBlogImageCommand("solid-carousel", Jpeg, "The first slide, redrawn.", 1), CancellationToken.None);

        Assert.Equal($"/blog/images/{BlogImageNames.For(Lossy, "webp")}", first.Kept!.Url);
        Assert.Equal((37, 21), (second.Kept!.Width, second.Kept.Height));
        var post = (await store.PostAsync("solid-carousel", CancellationToken.None))!;
        Assert.Equal([1, 2], post.Images.Select(i => i.Position));
        Assert.Equal(again.Kept!.Url, post.Images[0].Url);
        Assert.EndsWith(".jpg", post.Images[0].Url, StringComparison.Ordinal);
        Assert.Equal(3, (await images.TallyAsync(CancellationToken.None)).Count);
    }

    [Theory]
    [InlineData("big", "at most 1,048,576 bytes")]
    [InlineData("gif", "a PNG, a JPEG or a WebP by its own bytes")]
    [InlineData("wide", "at most 2000 pixels wide, and this is 2001")]
    [InlineData("alt", "the alt text is one sentence")]
    [InlineData("position", "the position is the picture's place")]
    public async Task A_picture_is_refused_by_the_figure_it_breaks_and_nothing_is_kept(string fault, string names)
    {
        var (handler, _, images) = await SetUpAsync();
        var command = fault switch
        {
            "big" => new PutBlogImageCommand("solid-carousel", [.. Png, .. new byte[BlogImageHeaders.MaxBytes]], "A slide.", 1),
            "gif" => new PutBlogImageCommand("solid-carousel", "GIF89a......"u8.ToArray(), "A slide.", 1),
            "wide" => new PutBlogImageCommand("solid-carousel", Wide, "A slide.", 1),
            "alt" => new PutBlogImageCommand("solid-carousel", Png, "  ", 1),
            _ => new PutBlogImageCommand("solid-carousel", Png, "A slide.", 0),
        };

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.Equal(400, result.Status);
        Assert.Contains(names, result.Wrong, StringComparison.Ordinal);
        Assert.Equal(0, (await images.TallyAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public async Task A_post_that_is_not_there_is_a_404_and_nothing_is_kept()
    {
        var (_, store, images) = await SetUpAsync();
        var handler = new PutBlogImageHandler(store, images, TimeProvider.System);

        var result = await handler.HandleAsync(new PutBlogImageCommand("no-such-post", Png, "A slide.", 1), CancellationToken.None);

        Assert.Equal(404, result.Status);
        Assert.Equal(0, (await images.TallyAsync(CancellationToken.None)).Count);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

/// <summary>The upload route through a keyed host: 401 without the key, 400 for a refused picture, 404 for no such post, 200 kept.</summary>
public sealed class BlogUploadRouteTests(BlogEndpointsTests.KeyedHost host) : IClassFixture<BlogEndpointsTests.KeyedHost>
{
    internal static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string slug, byte[] picture, string? key)
    {
        using var form = new MultipartFormDataContent
        {
            { new ByteArrayContent(picture) { Headers = { ContentType = new MediaTypeHeaderValue("application/octet-stream") } }, "file", "slide.bin" },
            { new StringContent("The first slide names the five principles."), "alt" },
            { new StringContent("1"), "position" },
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/blog/posts/{slug}/images") { Content = form };
        if (key is not null)
        {
            request.Headers.Add(AdminKey.HeaderName, key);
        }
        return await client.SendAsync(request);
    }

    [Fact]
    [Covers("POST", "/api/admin/blog/posts/{slug}/images", 401)]
    [Covers("POST", "/api/admin/blog/posts/{slug}/images", 400)]
    [Covers("POST", "/api/admin/blog/posts/{slug}/images", 404)]
    [Covers("POST", "/api/admin/blog/posts/{slug}/images", 200)]
    public async Task The_upload_asks_for_the_key_refuses_by_the_limit_and_keeps_a_picture_on_its_post()
    {
        var client = host.CreateClient();
        using var save = new HttpRequestMessage(HttpMethod.Post, "/api/admin/blog/posts")
        {
            Content = JsonContent.Create(new { slug = "picture-post", title = "T", summary = "S", body = "B", published_at = "2026-10-19T14:00:00Z", linkedin_url = "https://www.linkedin.com/posts/p" }),
        };
        save.Headers.Add(AdminKey.HeaderName, BlogEndpointsTests.KeyedHost.Key);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(save)).StatusCode);

        await Problem.Is(await UploadAsync(client, "picture-post", BlogUploadTests.Png, key: null), HttpStatusCode.Unauthorized);
        await Problem.Is(await UploadAsync(client, "picture-post", BlogUploadTests.Wide, BlogEndpointsTests.KeyedHost.Key), HttpStatusCode.BadRequest);
        await Problem.Is(await UploadAsync(client, "no-such-post", BlogUploadTests.Png, BlogEndpointsTests.KeyedHost.Key), HttpStatusCode.NotFound);

        var kept = await UploadAsync(client, "picture-post", BlogUploadTests.Lossy, BlogEndpointsTests.KeyedHost.Key);
        Assert.Equal(HttpStatusCode.OK, kept.StatusCode);
        using var body = JsonDocument.Parse(await kept.Content.ReadAsStringAsync());
        string url = body.RootElement.GetProperty("url").GetString()!;
        Assert.Equal($"/blog/images/{BlogImageNames.For(BlogUploadTests.Lossy, "webp")}", url);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
        using var post = JsonDocument.Parse(await client.GetStringAsync("/api/blog/posts/picture-post"));
        Assert.Equal(url, post.RootElement.GetProperty("images")[0].GetProperty("url").GetString());
    }
}

/// <summary>The upload on a host whose blog store cannot keep anything: 503 with the store's own reason.</summary>
public sealed class BlogUploadUnkeptTests(BlogUnkeptTests.UnkeptHost host) : IClassFixture<BlogUnkeptTests.UnkeptHost>
{
    [Fact]
    [Covers("POST", "/api/admin/blog/posts/{slug}/images", 503)]
    public async Task A_store_that_cannot_keep_answers_503()
    {
        await Problem.Is(await BlogUploadRouteTests.UploadAsync(host.CreateClient(), "picture-post", BlogUploadTests.Png, BlogEndpointsTests.KeyedHost.Key), HttpStatusCode.ServiceUnavailable);
    }
}
// #endregion blog-upload-tests
