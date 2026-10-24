using Microsoft.EntityFrameworkCore;
using TheYard.Application.Features.Blog;
using TheYard.Data;
using TheYard.Infrastructure;
using TheYard.Infrastructure.Features.Blog;

namespace TheYard.Tests;

/// <summary>
/// The blog (ADR: Every LinkedIn post is also a blog post): its rules, its handlers over the memory store
/// with no host, its numbers against a hand count, and its relational store on a real SQLite file, where
/// the migration applies clean, the list comes back newest first, a read is one PostView row with a bot
/// flagged, and each event type is one PostEvent row. Every post here is made up.
/// </summary>
public sealed class BlogTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 21, 0, 0, TimeSpan.Zero);

    private readonly string _file = Path.Combine(Path.GetTempPath(), $"theyard-blog-{Guid.NewGuid():N}.db");

    private static BlogPost Post(string slug, DateTimeOffset at, string title = "A post") =>
        new(0, slug, title, "Two lines about it.", "The text.\n#dotnet", at, $"https://www.linkedin.com/posts/{slug}", "https://github.com/SteveStout/TheYard", [new BlogImage(1, "/blog-images/a.png", "the first slide", 1080, 1350)], ["dotnet"]);

    private static byte[] Session(byte b) => Enumerable.Repeat(b, 32).ToArray();

    // #region blog-rules
    [Theory]
    [InlineData("www.linkedin.com", null, null, "linkedin")]
    [InlineData("lnkd.in", null, null, "linkedin")]
    [InlineData(null, null, "linkedin", "linkedin")]
    [InlineData("www.linkedin.com", "ab12cd", null, "ref")]
    [InlineData("www.google.com", null, null, "search")]
    [InlineData("duckduckgo.com", null, null, "search")]
    [InlineData("example.com", null, null, "direct")]
    [InlineData(null, null, null, "direct")]
    public void A_read_s_source_is_the_tagged_link_then_linkedin_then_search_then_direct(string? host, string? code, string? utm, string source) =>
        Assert.Equal(source, BlogRules.Source(host, code, utm));

    [Theory]
    [InlineData("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) Mobile/15E148", false, "phone")]
    [InlineData("Mozilla/5.0 (iPad; CPU OS 18_0 like Mac OS X)", false, "tablet")]
    [InlineData("Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/141.0", false, "desk")]
    [InlineData("curl/8.0", true, "bot")]
    public void A_device_is_a_class_and_the_agent_goes_no_further(string agent, bool bot, string device) =>
        Assert.Equal(device, BlogRules.Device(agent, bot));

    [Theory]
    [InlineData("depth", "50", null)]
    [InlineData("depth", "60", "a depth is 25, 50, 75 or 100")]
    [InlineData("time", "42", null)]
    [InlineData("time", "0", "a time is whole seconds, from 1 to a day")]
    [InlineData("slide", "3", null)]
    [InlineData("click", "linkedin", null)]
    [InlineData("click", "body:github.com", null)]
    [InlineData("click", "https://evil.example/x", "a click names where it went: linkedin, code, or body: and a host")]
    [InlineData("more", "1", null)]
    [InlineData("copy", "2", "see more and copy carry 1")]
    [InlineData("hover", "1", "the event type must be one of click, copy, depth, more, slide, time")]
    public void An_event_carries_only_what_its_type_allows(string type, string value, string? wrong) =>
        Assert.Equal(wrong, BlogRules.Wrong(type, value));

    [Fact]
    public void A_slug_is_lower_case_words_joined_by_dashes()
    {
        Assert.Equal("solid-in-five-real-files", BlogRules.SlugOf("SOLID in five real files"));
        Assert.True(BlogRules.IsSlug("solid-in-five-real-files"));
        Assert.False(BlogRules.IsSlug("Solid"));
        Assert.False(BlogRules.IsSlug("a--b"));
    }
    // #endregion blog-rules

    // #region blog-handlers
    [Fact]
    public async Task The_handlers_keep_a_post_list_it_newest_first_and_read_it_by_slug()
    {
        var store = new MemoryBlogStore("in memory, for the test");
        var clock = new FixedClock(Now);
        var save = new SavePostHandler(store, clock);
        Assert.Equal((null, "the LinkedIn address starts https://www.linkedin.com/", null), await save.HandleAsync(new SavePostCommand(Post("bad", Now) with { LinkedInUrl = "http://x" }), CancellationToken.None));
        Assert.NotNull((await save.HandleAsync(new SavePostCommand(Post("older-post", Now.AddDays(-9))), CancellationToken.None)).Id);
        Assert.NotNull((await save.HandleAsync(new SavePostCommand(Post("newer-post", Now.AddDays(-2))), CancellationToken.None)).Id);
        var (posts, _, unavailable) = await new ListPostsHandler(store, clock).HandleAsync(new ListPostsQuery(), CancellationToken.None);
        Assert.Null(unavailable);
        Assert.Equal(["newer-post", "older-post"], posts.Select(p => p.Slug));
        Assert.Equal("older-post", (await new ReadPostHandler(store).HandleAsync(new ReadPostQuery("older-post"), CancellationToken.None))?.Slug);
        Assert.Null(await new ReadPostHandler(store).HandleAsync(new ReadPostQuery("no-such-post"), CancellationToken.None));
    }

    [Fact]
    public async Task A_read_and_an_event_are_kept_and_the_stats_count_them_without_the_bots()
    {
        var store = new MemoryBlogStore("in memory, for the test");
        var clock = new FixedClock(Now);
        await new SavePostHandler(store, clock).HandleAsync(new SavePostCommand(Post("a-post", Now.AddDays(-1))), CancellationToken.None);
        var read = new RecordReadHandler(store, clock);
        Assert.Equal("linkedin", await read.HandleAsync(new RecordReadCommand("a-post", "www.linkedin.com", null, null, null, null, "phone", "US", Session(1), false), CancellationToken.None));
        Assert.Equal("ref", await read.HandleAsync(new RecordReadCommand("a-post", null, "ab12cd", null, null, null, "desk", null, Session(2), false), CancellationToken.None));
        Assert.Equal("direct", await read.HandleAsync(new RecordReadCommand("a-post", null, null, null, null, null, "bot", null, Session(3), true), CancellationToken.None));
        Assert.Null(await read.HandleAsync(new RecordReadCommand("no-post", null, null, null, null, null, "desk", null, Session(1), false), CancellationToken.None));
        var events = new RecordEventHandler(store, clock);
        Assert.Null(await events.HandleAsync(new RecordEventCommand("a-post", "depth", "100", Session(1), false), CancellationToken.None));
        Assert.Null(await events.HandleAsync(new RecordEventCommand("a-post", "click", "linkedin", Session(1), false), CancellationToken.None));
        Assert.Equal("a depth is 25, 50, 75 or 100", await events.HandleAsync(new RecordEventCommand("a-post", "depth", "10", Session(1), false), CancellationToken.None));
        var (stats, sources, kept, pictures) = await new ReadBlogStatsHandler(store, clock, new MemoryBlogImageStore()).HandleAsync(new ReadBlogStatsQuery(), CancellationToken.None);
        Assert.True(kept.Available);
        var stat = Assert.Single(stats);
        Assert.Equal((2, 2, 1, 1, 1), (stat.Reads, stat.Readers, stat.FromTaggedLinks, stat.ToLinkedIn, stat.Finished));
        Assert.Equal(2, sources.Sum(s => s.Reads));
    }

    [Fact]
    public void The_numbers_match_a_hand_count()
    {
        var post = Post("a-post", Now.AddDays(-20)) with { Id = 7 };
        BlogRead R(byte who, int daysAgo, string? code = null, bool bot = false) => new(7, Now.AddDays(-daysAgo), code is null ? "direct" : "ref", code, null, null, null, null, "desk", null, Session(who), bot);
        BlogEvent E(byte who, string type, string value, bool bot = false) => new(7, Session(who), Now, type, value, bot);
        var reads = new[] { R(1, 1), R(1, 2), R(2, 10, "ab12cd"), R(3, 3), R(9, 1, bot: true) };
        var events = new[]
        {
            E(1, "depth", "25"), E(1, "depth", "100"), E(2, "depth", "50"), E(3, "depth", "25"),
            E(1, "time", "40"), E(1, "time", "90"), E(2, "time", "30"),
            E(1, "click", "linkedin"), E(1, "click", "linkedin"), E(2, "click", "code"),
            E(9, "depth", "100", bot: true), E(9, "click", "linkedin", bot: true),
        };
        var stat = BlogNumbers.Of(post, reads, events, Now);
        // Reads 4 (the bot's left out); readers 3; three in the last seven days; one by a tagged link.
        Assert.Equal((4, 3, 3, 1), (stat.Reads, stat.Readers, stat.Reads7d, stat.FromTaggedLinks));
        // Deepest per reader 100, 50, 25: average 58.33; longest per reader 90, 30: average 60.
        Assert.Equal(58.33, stat.AverageDepth!.Value, 2);
        Assert.Equal(60, stat.AverageSeconds);
        // Funnel: opened 3, past half 2, finished 1, clicked out 2; LinkedIn 1, code 1.
        Assert.Equal((2, 1, 2, 1, 1), (stat.PastHalf, stat.Finished, stat.ClickedOut, stat.ToLinkedIn, stat.ToCode));
    }
    // #endregion blog-handlers

    // #region blog-sqlite
    private YardDbContext Context() => new(new YardConnection(YardProvider.Sqlite, $"Data Source={_file};Pooling=False").Options());

    [Fact]
    public async Task On_a_fresh_database_the_migration_applies_and_the_store_keeps_posts_reads_and_events()
    {
        using (var db = Context())
        {
            db.Database.Migrate();
        }
        var store = new EfBlogStore(() => new SqliteContexts(Context));
        Assert.Equal("kept in SQLite", (await store.AvailabilityAsync(CancellationToken.None)).Reason);
        int older = await store.SaveAsync(Post("older-post", Now.AddDays(-9)), Now, CancellationToken.None);
        int newer = await store.SaveAsync(Post("newer-post", Now.AddDays(-2), "Newer"), Now, CancellationToken.None);
        Assert.Equal(older, await store.SaveAsync(Post("older-post", Now.AddDays(-9), "Older, edited"), Now, CancellationToken.None));
        var posts = await store.PostsAsync(CancellationToken.None);
        Assert.Equal(["newer-post", "older-post"], posts.Select(p => p.Slug));
        Assert.Equal("Older, edited", posts[1].Title);
        Assert.Equal(["dotnet"], posts[0].Tags);
        Assert.Equal("the first slide", Assert.Single(posts[0].Images).AltText);

        await store.KeepReadAsync(new BlogRead(newer, Now, "linkedin", null, "www.linkedin.com", null, null, null, "phone", "US", Session(1), false), CancellationToken.None);
        await store.KeepReadAsync(new BlogRead(newer, Now, "direct", null, null, null, null, null, "bot", null, Session(2), true), CancellationToken.None);
        foreach (var (type, value) in new[] { ("depth", "75"), ("time", "12"), ("more", "1"), ("slide", "2"), ("click", "code"), ("copy", "1") })
        {
            await store.KeepEventAsync(new BlogEvent(newer, Session(1), Now, type, value, false), CancellationToken.None);
        }
        using (var db = Context())
        {
            Assert.Equal(2, await db.PostViews.CountAsync());
            Assert.Equal(1, await db.PostViews.CountAsync(v => v.IsBot));
            var kinds = await db.PostEvents.Select(e => e.Type).ToListAsync();
            Assert.Equal(["click", "copy", "depth", "more", "slide", "time"], kinds.Order(StringComparer.Ordinal));
        }
        var stat = (await store.StatsAsync(Now, CancellationToken.None)).Single(s => s.PostId == newer);
        Assert.Equal((1, 1, 75d, 12d, 1), (stat.Reads, stat.Readers, stat.AverageDepth, stat.AverageSeconds, stat.ToCode));
        Assert.Equal([new BlogSourceCount("linkedin", 1)], await store.SourcesAsync(Now.AddDays(-1), CancellationToken.None));
    }

    [Fact]
    public async Task A_store_with_no_relational_store_attached_says_so()
    {
        var availability = await new EfBlogStore(() => null).AvailabilityAsync(CancellationToken.None);
        Assert.False(availability.Available);
        Assert.Contains("not attached", availability.Reason, StringComparison.Ordinal);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class SqliteContexts(Func<YardDbContext> make) : IDbContextFactory<YardDbContext>
    {
        public YardDbContext CreateDbContext() => make();
    }
    // #endregion blog-sqlite

    public void Dispose()
    {
        if (File.Exists(_file))
        {
            File.Delete(_file);
        }
    }
}
