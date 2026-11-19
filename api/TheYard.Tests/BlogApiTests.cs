using System.IO.Compression;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Application.Features.Blog;

namespace TheYard.Tests;

/// <summary>
/// The list and the post through the host (ADR: Every LinkedIn post is also a blog post, the addendum on the read
/// model), over a memory store holding the 15 live posts: the filter read from the address, the cards with no body,
/// the post drawn as HTML with its older, newer and related posts, the cache rule, the ETag and the 304, the host's
/// copy of an answer and its eviction on a save, and the list's size on the wire.
/// </summary>
public sealed class BlogApiTests(BlogApiTests.SeededHost host) : IClassFixture<BlogApiTests.SeededHost>
{
    /// <summary>A host whose blog is the 15 live posts in memory, with the operator's key set.</summary>
    public sealed class SeededHost : WebApplicationFactory<Program>
    {
        /// <summary>The store the host reads, seeded once.</summary>
        public MemoryBlogStore Store { get; } = Seeded();

        private static MemoryBlogStore Seeded()
        {
            var store = new MemoryBlogStore("in memory, for the test");
            store.KeepSynonymsAsync(KeywordRules.SynonymRows(), CancellationToken.None).GetAwaiter().GetResult();
            foreach (var entry in BlogFixture.Posts.Reverse())
            {
                var post = entry.Post with { Id = 0, Tags = entry.Topics };
                store.SaveAsync(post, PostDerivation.Of(post.Title, post.Summary, post.Body), DateTimeOffset.UtcNow, CancellationToken.None).GetAwaiter().GetResult();
            }
            return store;
        }

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.UseSetting("Admin:Key", BlogEndpointsTests.KeyedHost.Key);
            // The blog never reads Cosmos DB, so this host stays on the relational path even when the suite is pointed at
            // the document store, and the Cosmos pass spends none of its throughput booting it.
            builder.UseSetting("Cosmos:AccountEndpoint", "");
            builder.ConfigureTestServices(services => services.AddSingleton<IBlogStore>(Store));
        }
    }

    private const string Ignite = "microsoft-ignite-2026-all-in-on-ai-light-on-everything-else";

    private async Task<JsonElement> Json(string path)
    {
        using var answer = await host.CreateClient().GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, answer.StatusCode);
        return JsonDocument.Parse(await answer.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static IEnumerable<string> Slugs(JsonElement list) => list.GetProperty("cards").EnumerateArray().Select(c => c.GetProperty("slug").GetString()!);

    // #region blog-api-list
    [Fact]
    public async Task The_list_is_a_page_of_cards_with_no_body_the_total_the_chips_and_the_next_cursor()
    {
        var list = await Json("/api/blog/posts");
        Assert.Equal(15, list.GetProperty("total").GetInt32());
        Assert.Equal(10, list.GetProperty("cards").GetArrayLength());
        var first = list.GetProperty("cards")[0];
        Assert.Equal(Ignite, first.GetProperty("slug").GetString());
        Assert.False(first.TryGetProperty("body", out _));
        Assert.Equal(11, first.GetProperty("slides").GetInt32());
        Assert.Equal(["ai", "dotnet", "azure"], first.GetProperty("topics").EnumerateArray().Select(t => t.GetString()));
        Assert.Contains("copilot", first.GetProperty("keywords").EnumerateArray().Select(k => k.GetString()));
        Assert.True(first.GetProperty("reading_minutes").GetInt32() > 10);
        var cover = first.GetProperty("cover");
        Assert.EndsWith("-320.webp", cover.GetProperty("thumb_url").GetString(), StringComparison.Ordinal);
        Assert.Equal(10, list.GetProperty("topics").GetArrayLength());
        Assert.Equal("November 2026", list.GetProperty("months")[0].GetProperty("label").GetString());
        var next = list.GetProperty("next").GetString();
        var rest = await Json($"/api/blog/posts?after={Uri.EscapeDataString(next!)}");
        Assert.Equal(5, rest.GetProperty("cards").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, rest.GetProperty("next").ValueKind);
    }

    [Fact]
    public async Task The_address_carries_the_filter()
    {
        Assert.Equal(["my-resume-came-back-as-a-job", "scammers-read-the-job-market-too"], Slugs(await Json("/api/blog/posts?topic=security")));
        Assert.Equal([Ignite], Slugs(await Json("/api/blog/posts?keyword=copilot")));
        Assert.Equal([Ignite], Slugs(await Json("/api/blog/posts?q=microsoft%20copilot")));
        var searched = (await Json("/api/blog/posts?q=copilot")).GetProperty("cards")[0];
        Assert.Equal("Microsoft Copilot", searched.GetProperty("matched")[0].GetString());
        Assert.Equal(5, (await Json("/api/blog/posts?month=2026-10")).GetProperty("total").GetInt32());
        Assert.Equal(15, (await Json("/api/blog/posts?year=2026&sort=read")).GetProperty("total").GetInt32());
        // The widened terms ride along, so the page can mark them without a copy of the synonyms of its own.
        Assert.Equal(["copilot", "microsoft copilot", "github copilot"], (await Json("/api/blog/posts?q=copilot")).GetProperty("terms").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal(["microsoft"], (await Json("/api/blog/posts?q=ms")).GetProperty("terms").EnumerateArray().Select(t => t.GetString()));
        Assert.Empty((await Json("/api/blog/posts")).GetProperty("terms").EnumerateArray());
        // A part the list does not know is ignored, never refused: a link shared with a stray part still opens.
        Assert.Equal(15, (await Json("/api/blog/posts?sort=sideways&year=nope&after=not-a-cursor")).GetProperty("total").GetInt32());
    }
    // #endregion blog-api-list

    // #region blog-api-post
    [Fact]
    public async Task A_post_is_its_body_drawn_once_with_its_older_and_newer_posts_and_up_to_three_related_by_topic()
    {
        var post = await Json("/api/blog/posts/cqrs-for-beginners");
        Assert.StartsWith("<", post.GetProperty("body_html").GetString(), StringComparison.Ordinal);
        Assert.False(string.IsNullOrEmpty(post.GetProperty("body").GetString()));
        Assert.Equal("read-the-query-behind-the-dial", post.GetProperty("older").GetProperty("slug").GetString());
        Assert.Equal("onion-architecture-for-beginners", post.GetProperty("newer").GetProperty("slug").GetString());
        var related = post.GetProperty("related").EnumerateArray().Select(c => c.GetProperty("slug").GetString()).ToList();
        Assert.Equal(["microsoft-ignite-2026-all-in-on-ai-light-on-everything-else", "onion-architecture-for-beginners", "solid-in-five-real-files"], related);
        Assert.All(post.GetProperty("images").EnumerateArray(), image => Assert.EndsWith("-320.webp", image.GetProperty("thumb_url").GetString(), StringComparison.Ordinal));
        var newest = await Json($"/api/blog/posts/{Ignite}");
        Assert.Equal(JsonValueKind.Null, newest.GetProperty("newer").ValueKind);
        var oldest = await Json("/api/blog/posts/solid-in-five-real-files");
        Assert.Equal(JsonValueKind.Null, oldest.GetProperty("older").ValueKind);
    }
    // #endregion blog-api-post

    // #region blog-api-cache
    [Theory]
    [Covers("GET", "/api/blog/posts", 304)]
    [Covers("GET", "/api/blog/posts/{slug}", 304)]
    [InlineData("/api/blog/posts?topic=ai")]
    [InlineData("/api/blog/posts/the-spec-has-to-run")]
    public async Task Both_answers_carry_the_cache_rule_and_an_etag_and_a_repeat_is_a_304(string path)
    {
        var client = host.CreateClient();
        using var first = await client.GetAsync(path);
        Assert.Equal("public, max-age=60, stale-while-revalidate=600", first.Headers.CacheControl?.ToString());
        var etag = first.Headers.ETag;
        Assert.NotNull(etag);
        using var again = new HttpRequestMessage(HttpMethod.Get, path);
        again.Headers.IfNoneMatch.Add(etag);
        using var repeat = await client.SendAsync(again);
        Assert.Equal(HttpStatusCode.NotModified, repeat.StatusCode);
        Assert.Empty(await repeat.Content.ReadAsByteArrayAsync());
        Assert.Equal(etag, repeat.Headers.ETag);
    }

    [Fact]
    public async Task The_host_keeps_an_answer_a_minute_and_a_save_through_the_route_clears_it()
    {
        // A host of its own, because this test writes posts the other tests do not expect.
        await using var own = new SeededHost();
        async Task<JsonElement> Read(string path) =>
            JsonDocument.Parse(await own.CreateClient().GetStringAsync(path)).RootElement.Clone();
        string path = "/api/blog/posts?topic=career";
        Assert.Equal(["lee-markum-told-me-to-blog"], Slugs(await Read(path)));
        // Written to the store behind the route's back: the host still answers what it kept.
        var hidden = new BlogPost(0, "a-career-post-written-around-the-route", "Around the route", "Two lines.", "The text.", new DateTimeOffset(2026, 11, 13, 9, 0, 0, TimeSpan.Zero), "https://www.linkedin.com/posts/around", null, [], ["career"]);
        await own.Store.SaveAsync(hidden, PostDerivation.Of(hidden.Title, hidden.Summary, hidden.Body), DateTimeOffset.UtcNow, CancellationToken.None);
        Assert.Equal(["lee-markum-told-me-to-blog"], Slugs(await Read(path)));
        // Saved through the operator's route: the kept answers go, and the next read is the store's.
        using var save = new HttpRequestMessage(HttpMethod.Post, "/api/admin/blog/posts")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { slug = "a-career-post-saved-by-the-route", title = "By the route", summary = "Two lines.", body = "The text.", published_at = "2026-11-14T09:00:00Z", linkedin_url = "https://www.linkedin.com/posts/by-the-route", tags = new[] { "career" } }),
        };
        save.Headers.Add(AdminKey.HeaderName, BlogEndpointsTests.KeyedHost.Key);
        Assert.Equal(HttpStatusCode.OK, (await own.CreateClient().SendAsync(save)).StatusCode);
        Assert.Equal(["a-career-post-saved-by-the-route", "a-career-post-written-around-the-route", "lee-markum-told-me-to-blog"], Slugs(await Read(path)));
    }
    // #endregion blog-api-cache

    [Fact]
    public async Task The_first_page_of_the_list_is_under_six_kilobytes_in_brotli()
    {
        using var answer = await host.CreateClient().GetAsync("/api/blog/posts?sort=new");
        byte[] raw = await answer.Content.ReadAsByteArrayAsync();
        using var packed = new MemoryStream();
        using (var brotli = new BrotliStream(packed, CompressionLevel.Optimal, leaveOpen: true))
        {
            brotli.Write(raw);
        }
        Assert.True(packed.Length < 6 * 1024, $"the first page is {raw.Length} bytes raw and {packed.Length} in brotli");
    }
}
