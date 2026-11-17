using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Application.Features.Blog;
using TheYard.Infrastructure;
using TheYard.Infrastructure.Features.Blog;

namespace TheYard.Tests;

/// <summary>
/// The blog's edges, each with what it answers (ADR: Every LinkedIn post is also a blog post): every check a save makes,
/// each one sentence; a read and an event refused or kept by what they carry; a post not drawn yet drawn on the read; a
/// card with no slides; a store kept in a relational file that is not the blog's; and the picture names that are not one.
/// </summary>
public sealed class BlogEdgeTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly string _file = Path.Combine(Path.GetTempPath(), $"theyard-blogedge-{Guid.NewGuid():N}.db");

    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static BlogPost Post(string slug = "a-post") =>
        new(0, slug, "A post", "Two lines.", "The **text**.", Now, $"https://www.linkedin.com/posts/{slug}", null, [new BlogImage(1, "/blog/images/0123456789abcdef.webp", "A slide.", 1600, 2002)], ["career"]);

    [Theory]
    [InlineData("slug", "the slug is lower-case words joined by dashes")]
    [InlineData("title", "the title is 1 to 200 characters")]
    [InlineData("summary", "the summary is 1 to 400 characters")]
    [InlineData("body", "the body is the post's text")]
    [InlineData("linkedin", "the LinkedIn address starts https://www.linkedin.com/")]
    [InlineData("alt", "every image says what it shows")]
    public async Task A_save_names_the_one_thing_wrong_with_the_post_and_keeps_nothing(string fault, string wrong)
    {
        var store = new MemoryBlogStore("in memory, for the test");
        var post = fault switch
        {
            "slug" => Post() with { Slug = "Not A Slug" },
            "title" => Post() with { Title = new string('t', 201) },
            "summary" => Post() with { Summary = " " },
            "body" => Post() with { Body = "" },
            "linkedin" => Post() with { LinkedInUrl = "https://example.com/posts/a" },
            _ => Post() with { Images = [new BlogImage(1, "/blog/images/0123456789abcdef.webp", " ", 10, 10)] },
        };
        var (id, said, unavailable) = await new SavePostHandler(store, new Clock(Now)).HandleAsync(new SavePostCommand(post), CancellationToken.None);
        Assert.Null(id);
        Assert.Null(unavailable);
        Assert.StartsWith(wrong, said, StringComparison.Ordinal);
        Assert.Empty(await store.PostsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_read_and_an_event_are_kept_only_for_a_published_post_and_long_fields_are_cut()
    {
        var store = new MemoryBlogStore("in memory, for the test");
        await new SavePostHandler(store, new Clock(Now)).HandleAsync(new SavePostCommand(Post()), CancellationToken.None);
        var reads = new RecordReadHandler(store, new Clock(Now));
        byte[] session = new byte[32];
        Assert.Null(await reads.HandleAsync(new RecordReadCommand("Not A Slug", null, null, null, null, null, "desk", null, session, false), CancellationToken.None));
        Assert.Null(await reads.HandleAsync(new RecordReadCommand("no-such-post", null, null, null, null, null, "desk", null, session, false), CancellationToken.None));
        Assert.Equal("linkedin", await reads.HandleAsync(new RecordReadCommand("a-post", new string('h', 200), null, "linkedin", new string('m', 90), null, "phone", "US", session, false), CancellationToken.None));
        var events = new RecordEventHandler(store, new Clock(Now));
        Assert.Equal("no published post has that slug", await events.HandleAsync(new RecordEventCommand("no-such-post", "slide", "2", session, false), CancellationToken.None));
        Assert.Equal("a slide is its number, from 1", await events.HandleAsync(new RecordEventCommand("a-post", "slide", "0", session, false), CancellationToken.None));
        Assert.Null(await events.HandleAsync(new RecordEventCommand("a-post", "click", "slides", session, false), CancellationToken.None));
        var stats = (await new ReadBlogStatsHandler(store, new Clock(Now), new MemoryBlogImageStore()).HandleAsync(new ReadBlogStatsQuery(), CancellationToken.None)).Stats;
        Assert.Equal((1, 0), (stats.Single().Reads, stats.Single().ClickedOut));
    }

    [Fact]
    public async Task A_post_read_before_any_save_drew_it_is_drawn_on_the_read()
    {
        using (var db = new YardDbContext(new YardConnection(YardProvider.Sqlite, $"Data Source={_file};Pooling=False").Options()))
        {
            await db.Database.MigrateAsync();
            db.Posts.Add(new PostRow { Slug = "an-old-post", Title = "An old post", Summary = "Two lines.", Body = "## A heading\n\nA line.", PublishedAt = Now, LinkedInUrl = "https://www.linkedin.com/posts/old", IsPublished = true, CreatedAt = Now, UpdatedAt = Now });
            await db.SaveChangesAsync();
        }
        var store = new EfBlogStore(() => new SqliteContexts(_file));
        var page = await new ReadPostHandler(store, new Clock(Now)).HandleAsync(new ReadPostQuery("an-old-post"), CancellationToken.None);
        Assert.Equal("<h3>A heading</h3>\n<p>A line.</p>\n", page!.BodyHtml);
        Assert.Null(await new ReadPostHandler(store, new Clock(Now)).HandleAsync(new ReadPostQuery("Not A Slug"), CancellationToken.None));
        Assert.Null(await store.PostPageAsync("no-such-post", Now, CancellationToken.None));
        Assert.Null(await store.PostAsync("no-such-post", CancellationToken.None));
        var card = Assert.Single((await store.ListAsync(new BlogFilter(null, null, null, null, null, "new", null), Now, CancellationToken.None)).Cards);
        Assert.Null(card.Cover);
        Assert.Equal(1, card.ReadingMinutes);
    }

    [Theory]
    [InlineData("DROP TABLE \"PostEvent\"")]
    [InlineData("DROP TABLE \"PostImage\"")]
    [InlineData("DROP TABLE \"PostTag\"")]
    [InlineData("DROP TABLE \"PostWord\"")]
    [InlineData("DROP TABLE \"PostKeyword\"")]
    [InlineData("DROP TABLE \"SearchSynonym\"")]
    public async Task A_relational_file_missing_any_blog_table_says_so_and_does_not_throw(string drop)
    {
        using (var db = new YardDbContext(new YardConnection(YardProvider.Sqlite, $"Data Source={_file};Pooling=False").Options()))
        {
            await db.Database.EnsureCreatedAsync();
            await db.Database.ExecuteSqlRawAsync(drop);
        }
        var availability = await new EfBlogStore(() => new SqliteContexts(_file)).AvailabilityAsync(CancellationToken.None);
        Assert.False(availability.Available);
        Assert.Contains("not published to this database yet", availability.Reason, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0123456789abcdef.gif", null)]
    [InlineData("../0123456789abcdef.png", null)]
    [InlineData("0123456789abcdef-320.webp", "image/webp")]
    [InlineData("0123456789abcdef.jpg", "image/jpeg")]
    public void A_picture_s_type_comes_from_a_name_the_rule_makes_and_from_no_other(string name, string? type) =>
        Assert.Equal(type, BlogImageNames.ContentType(name));

    private sealed class SqliteContexts(string file) : IDbContextFactory<YardDbContext>
    {
        public YardDbContext CreateDbContext() => new(new YardConnection(YardProvider.Sqlite, $"Data Source={file};Pooling=False").Options());
    }

    public void Dispose()
    {
        if (File.Exists(_file))
        {
            File.Delete(_file);
        }
    }
}

/// <summary>The read and event routes refuse what they cannot keep, and a save with no date is a 400 that says so.</summary>
public sealed class BlogRouteEdgeTests(BlogEndpointsTests.KeyedHost host) : IClassFixture<BlogEndpointsTests.KeyedHost>
{
    [Fact]
    public async Task A_read_or_an_event_that_is_not_one_is_a_400_and_a_save_with_no_date_too()
    {
        var client = host.CreateClient();
        using var notJson = new StringContent("not json", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/blog/reads", notJson)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/blog/events", new { slug = "a-post" })).StatusCode);
        using var save = new HttpRequestMessage(HttpMethod.Post, "/api/admin/blog/posts") { Content = JsonContent.Create(new { slug = "a-post", title = "T" }) };
        save.Headers.Add(AdminKey.HeaderName, BlogEndpointsTests.KeyedHost.Key);
        var answer = await client.SendAsync(save);
        Assert.Equal(HttpStatusCode.BadRequest, answer.StatusCode);
        Assert.Contains("published_at", await answer.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_read_keeps_a_tagged_code_only_in_its_shape_and_a_referrer_only_as_a_web_host()
    {
        var store = host.Services.GetRequiredService<IBlogStore>();
        var post = new BlogPost(0, "an-edge-post", "An edge post", "Two lines.", "Text.", DateTimeOffset.UtcNow, "https://www.linkedin.com/posts/edge", null, [], []);
        await store.SaveAsync(post, PostDerivation.Of(post.Title, post.Summary, post.Body), DateTimeOffset.UtcNow, CancellationToken.None);
        var client = host.CreateClient();
        using var tagged = new HttpRequestMessage(HttpMethod.Post, "/api/blog/reads") { Content = JsonContent.Create(new { slug = "an-edge-post", @ref = "AB12CD", referrer = "ftp://files.example/x" }) };
        tagged.Headers.Add("X-Country", "us");
        var kept = await client.SendAsync(tagged);
        Assert.Equal("ref", JsonDocument.Parse(await kept.Content.ReadAsStringAsync()).RootElement.GetProperty("source").GetString());
        var untagged = await client.PostAsJsonAsync("/api/blog/reads", new { slug = "an-edge-post", @ref = "not-a-code", referrer = "https://www.google.com/search" });
        Assert.Equal("search", JsonDocument.Parse(await untagged.Content.ReadAsStringAsync()).RootElement.GetProperty("source").GetString());
    }
}
