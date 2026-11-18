using Microsoft.EntityFrameworkCore;
using TheYard.Application.Features.Blog;
using TheYard.Data;
using TheYard.Infrastructure;
using TheYard.Infrastructure.Features.Blog;

namespace TheYard.Tests;

/// <summary>
/// The list's read model (ADR: Every LinkedIn post is also a blog post, the addendum on the read model), one table of
/// cases run against both stores: the memory store and the relational store on a real SQLite file. The 15 live posts
/// are the seed, saved through the store with what PostDerivation works out for each, and every expected answer is
/// read off the fixture by plain LINQ, never off either store. The two must answer every case the same.
/// </summary>
public sealed class BlogListTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly string _file = Path.Combine(Path.GetTempPath(), $"theyard-bloglist-{Guid.NewGuid():N}.db");

    private static byte[] Session(int who) => [.. Enumerable.Repeat((byte)(who % 250), 32)];

    /// <summary>Each post's reads by its place in the fixture: some read this week, some only before, and a bot that never counts.</summary>
    private static int ReadsOf(int index) => (index * 7) % 5;

    private YardDbContext Context() => new(new YardConnection(YardProvider.Sqlite, $"Data Source={_file};Pooling=False").Options());

    private sealed class SqliteContexts(Func<YardDbContext> make) : IDbContextFactory<YardDbContext>
    {
        public YardDbContext CreateDbContext() => make();
    }

    /// <summary>A store of the named kind holding the 15 posts, their reads, and the synonyms.</summary>
    private async Task<IBlogStore> SeededAsync(string kind)
    {
        IBlogStore store;
        if (kind == "memory")
        {
            store = new MemoryBlogStore("kept in memory");
        }
        else
        {
            using (var db = Context())
            {
                await db.Database.MigrateAsync();
            }
            store = new EfBlogStore(() => new SqliteContexts(Context));
        }
        await store.KeepSynonymsAsync(KeywordRules.SynonymRows(), CancellationToken.None);
        var ids = new Dictionary<string, int>();
        foreach (var entry in BlogFixture.Posts.Reverse())
        {
            var post = entry.Post;
            ids[post.Slug] = await store.SaveAsync(post with { Id = 0 }, PostDerivation.Of(post.Title, post.Summary, post.Body), Now, CancellationToken.None);
        }
        for (int i = 0; i < BlogFixture.Posts.Count; i++)
        {
            int id = ids[BlogFixture.Posts[i].Post.Slug];
            for (int r = 0; r < ReadsOf(i); r++)
            {
                // Every other read is older than the week, so reads and reads7d differ.
                var at = Now.AddDays(r % 2 == 0 ? -1 : -10);
                await store.KeepReadAsync(new BlogRead(id, at, "direct", null, null, null, null, null, "desk", null, Session((i * 10) + r), false), CancellationToken.None);
            }
            await store.KeepReadAsync(new BlogRead(id, Now.AddHours(-1), "direct", null, null, null, null, null, "bot", null, Session(249), true), CancellationToken.None);
        }
        return store;
    }

    private static BlogFilter Filter(string? q = null, string? topic = null, string? keyword = null, string? month = null, int? year = null, string sort = "new", string? after = null) =>
        new(q, topic, keyword, month, year, sort, after);

    private static IEnumerable<BlogFixture.Entry> Fixture => BlogFixture.Posts;

    // #region list-table
    /// <summary>The cases: a filter, and the slugs the whole answer holds across its pages, in order.</summary>
    public static TheoryData<string, string, string> Cases()
    {
        var data = new TheoryData<string, string, string>();
        foreach (string kind in new[] { "memory", "sqlite" })
        {
            data.Add(kind, "every post, newest first", string.Join(' ', Fixture.Select(e => e.Post.Slug)));
            data.Add(kind, "topic=architecture", string.Join(' ', Fixture.Where(e => e.Topics.Contains("architecture")).Select(e => e.Post.Slug)));
            data.Add(kind, "topic=security", "my-resume-came-back-as-a-job scammers-read-the-job-market-too");
            data.Add(kind, "keyword=copilot", "microsoft-ignite-2026-all-in-on-ai-light-on-everything-else");
            data.Add(kind, "keyword=claude", string.Join(' ', Fixture.Where(e => e.Keywords.Contains("claude")).Select(e => e.Post.Slug)));
            data.Add(kind, "month=2026-10", string.Join(' ', Fixture.Where(e => e.Post.PublishedAt.UtcDateTime.ToString("yyyy-MM") == "2026-10").Select(e => e.Post.Slug)));
            data.Add(kind, "year=2026", string.Join(' ', Fixture.Select(e => e.Post.Slug)));
            data.Add(kind, "year=2025", "");
            data.Add(kind, "year=1999", "");
            data.Add(kind, "month=2026-11 year=2026", string.Join(' ', Fixture.Where(e => e.Post.PublishedAt.UtcDateTime.ToString("yyyy-MM") == "2026-11").Select(e => e.Post.Slug)));
            data.Add(kind, "month=2026-11 keyword=claude", string.Join(' ', Fixture.Where(e => e.Keywords.Contains("claude") && e.Post.PublishedAt.UtcDateTime.ToString("yyyy-MM") == "2026-11").Select(e => e.Post.Slug)));
            data.Add(kind, "q=scams sort=read", string.Join(' ', new[] { "scammers-read-the-job-market-too", "my-resume-came-back-as-a-job", "solid-in-five-real-files" }
                .Select(slug => (slug, i: Fixture.ToList().FindIndex(e => e.Post.Slug == slug))).OrderByDescending(x => ReadsOf(x.i)).ThenByDescending(x => Fixture.ElementAt(x.i).Post.PublishedAt).Select(x => x.slug)));
            data.Add(kind, "q=copilot", "microsoft-ignite-2026-all-in-on-ai-light-on-everything-else");
            data.Add(kind, "q=scams", "scammers-read-the-job-market-too my-resume-came-back-as-a-job solid-in-five-real-files");
            data.Add(kind, "q=copil", "microsoft-ignite-2026-all-in-on-ai-light-on-everything-else");
            data.Add(kind, "q=kubernetes", "");
            data.Add(kind, "q=scams topic=security", "scammers-read-the-job-market-too my-resume-came-back-as-a-job");
            data.Add(kind, "sort=read", string.Join(' ', Fixture.Select((e, i) => (e, i)).OrderByDescending(x => ReadsOf(x.i)).ThenByDescending(x => x.e.Post.PublishedAt).Select(x => x.e.Post.Slug)));
            data.Add(kind, "sort=read topic=ai", string.Join(' ', Fixture.Select((e, i) => (e, i)).Where(x => x.e.Topics.Contains("ai")).OrderByDescending(x => ReadsOf(x.i)).ThenByDescending(x => x.e.Post.PublishedAt).Select(x => x.e.Post.Slug)));
        }
        return data;
    }

    private static BlogFilter Parse(string asked)
    {
        var parts = asked.Split(' ').Where(p => p.Contains('=')).ToDictionary(p => p[..p.IndexOf('=')], p => p[(p.IndexOf('=') + 1)..]);
        return Filter(parts.GetValueOrDefault("q"), parts.GetValueOrDefault("topic"), parts.GetValueOrDefault("keyword"), parts.GetValueOrDefault("month"),
            parts.TryGetValue("year", out string? year) ? int.Parse(year, System.Globalization.CultureInfo.InvariantCulture) : null, parts.GetValueOrDefault("sort") ?? "new");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Each_filter_gives_the_same_posts_in_the_same_order_on_both_stores_a_page_at_a_time(string kind, string asked, string slugs)
    {
        var store = await SeededAsync(kind);
        var want = slugs.Length == 0 ? [] : slugs.Split(' ');
        var filter = Parse(asked);
        var got = new List<string>();
        var page = await store.ListAsync(filter, Now, CancellationToken.None);
        Assert.Equal(want.Length, page.Total);
        got.AddRange(page.Cards.Select(c => c.Slug));
        int pages = 1;
        while (page.Next is { } next)
        {
            Assert.Equal(10, page.Cards.Count);
            page = await store.ListAsync(filter with { After = next }, Now, CancellationToken.None);
            got.AddRange(page.Cards.Select(c => c.Slug));
            pages++;
        }
        Assert.Equal(want, got);
        Assert.Equal(Math.Max(1, (want.Length + 9) / 10), pages);
    }
    // #endregion list-table

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task A_card_carries_no_body_and_its_cover_slides_minutes_topics_keywords_and_reads(string kind)
    {
        var store = await SeededAsync(kind);
        var page = await store.ListAsync(Filter(), Now, CancellationToken.None);
        for (int i = 0; i < page.Cards.Count; i++)
        {
            var card = page.Cards[i];
            var entry = Fixture.Single(e => e.Post.Slug == card.Slug);
            int index = BlogFixture.Posts.ToList().IndexOf(entry);
            Assert.Equal(entry.Post.Images.FirstOrDefault()?.Url, card.Cover?.Url);
            Assert.Equal(entry.Post.Images.Count, card.Slides);
            Assert.Equal(BlogText.ReadingMinutes(BlogMarkdown.Plain(entry.Post.Body)), card.ReadingMinutes);
            Assert.Equal(KeywordRules.Topics.Select(t => t.Key).Where(entry.Topics.Contains), card.Topics);
            Assert.Equal(entry.Keywords, card.Keywords);
            Assert.Equal((ReadsOf(index), (ReadsOf(index) + 1) / 2), (card.Reads, card.Reads7d));
            Assert.Empty(card.Matched);
        }
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task A_search_says_where_each_post_matched(string kind)
    {
        var store = await SeededAsync(kind);
        var card = Assert.Single((await store.ListAsync(Filter(q: "copilot"), Now, CancellationToken.None)).Cards);
        Assert.Equal("Microsoft Copilot", card.Matched[0]);
        Assert.Matches(@"^\d+ mentions in the text$", card.Matched[^1]);
        var title = (await store.ListAsync(Filter(q: "onion"), Now, CancellationToken.None)).Cards[0];
        Assert.Equal(["title", "Onion architecture"], title.Matched.Take(2));
    }

    [Theory]
    [InlineData("memory")]
    [InlineData("sqlite")]
    public async Task Each_chip_counts_the_posts_the_other_filters_leave(string kind)
    {
        var store = await SeededAsync(kind);
        var page = await store.ListAsync(Filter(topic: "architecture", month: "2026-11"), Now, CancellationToken.None);
        var november = Fixture.Where(e => e.Post.PublishedAt.UtcDateTime.ToString("yyyy-MM") == "2026-11").ToList();
        var architecture = Fixture.Where(e => e.Topics.Contains("architecture")).ToList();
        // Topics: counted over November's posts, the topic itself set aside.
        Assert.Equal(KeywordRules.Topics.Select(t => (t.Key, t.Label, november.Count(e => e.Topics.Contains(t.Key)))), page.Topics.Select(f => (f.Key, f.Label, f.Count)));
        // Keywords: every keyword some post has, most posts first, each counted over November's architecture posts.
        var having = KeywordRules.Keywords.Where(k => Fixture.Any(e => e.Keywords.Contains(k.Key)))
            .OrderByDescending(k => Fixture.Count(e => e.Keywords.Contains(k.Key))).ThenBy(k => k.Label, StringComparer.Ordinal);
        Assert.Equal(having.Select(k => (k.Key, november.Intersect(architecture).Count(e => e.Keywords.Contains(k.Key)))), page.Keywords.Select(f => (f.Key, f.Count)));
        // Months: every month a post went out in, newest first, each counted over the architecture posts.
        Assert.Equal([("2026-11", "November 2026", 2), ("2026-10", "October 2026", 1)], page.Months.Select(f => (f.Key, f.Label, f.Count)));
    }

    // #region large-log
    [Fact]
    public async Task The_memory_store_s_list_is_the_same_size_with_a_full_read_log_as_with_none()
    {
        var store = new MemoryBlogStore("kept in memory");
        var post = BlogFixture.Posts[0].Post;
        int id = await store.SaveAsync(post with { Id = 0 }, PostDerivation.Of(post.Title, post.Summary, post.Body), Now, CancellationToken.None);
        string empty = System.Text.Json.JsonSerializer.Serialize(await store.ListAsync(Filter(), Now, CancellationToken.None));
        for (int i = 0; i < MemoryBlogStore.MaxRows; i++)
        {
            await store.KeepReadAsync(new BlogRead(id, Now.AddMinutes(-i), "direct", null, null, null, null, null, "desk", null, Session(i), i % 10 == 0), CancellationToken.None);
        }
        var full = await store.ListAsync(Filter(), Now, CancellationToken.None);
        Assert.Equal(MemoryBlogStore.MaxRows * 9 / 10, full.Cards[0].Reads);
        // The answer grows by the digits of two counts and nothing else: no read rides along with the list.
        Assert.InRange(System.Text.Json.JsonSerializer.Serialize(full).Length - empty.Length, 0, 12);
    }

    [Fact]
    public async Task The_relational_list_only_counts_the_logs_and_never_reads_their_rows()
    {
        await SeededAsync("sqlite");
        using (var db = Context())
        {
            var post = await db.Posts.FirstAsync();
            db.PostViews.AddRange(Enumerable.Range(0, 5_000).Select(i => new PostViewRow { PostId = post.Id, ViewedAt = Now.AddMinutes(-i), Source = "direct", Device = "desk", SessionHash = Session(i), IsBot = false }));
            db.PostEvents.AddRange(Enumerable.Range(0, 5_000).Select(i => new PostEventRow { PostId = post.Id, At = Now.AddMinutes(-i), Type = "depth", Value = "50", SessionHash = Session(i), IsBot = false }));
            await db.SaveChangesAsync();
        }
        var seen = new SqlSeen();
        var watched = new EfBlogStore(() => new SqliteContexts(() => new YardDbContext(new DbContextOptionsBuilder<YardDbContext>(new YardConnection(YardProvider.Sqlite, $"Data Source={_file};Pooling=False").Options()).AddInterceptors(seen).Options)));
        foreach (var filter in new[] { Filter(), Filter(sort: "read"), Filter(q: "copilot"), Filter(topic: "ai") })
        {
            await watched.ListAsync(filter, Now, CancellationToken.None);
        }
        var logs = seen.Statements.Where(sql => sql.Contains("\"PostView\"", StringComparison.Ordinal) || sql.Contains("\"PostEvent\"", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(logs);
        Assert.All(logs, sql => Assert.Contains("COUNT(", sql, StringComparison.Ordinal));
        Assert.DoesNotContain(seen.Statements, sql => sql.Contains("\"PostEvent\"", StringComparison.Ordinal));
    }

    /// <summary>Every statement a context sends, so a test can read what the list asked the database.</summary>
    private sealed class SqlSeen : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public List<string> Statements { get; } = [];

        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<System.Data.Common.DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Statements.Add(command.CommandText);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
    // #endregion large-log

    public void Dispose()
    {
        if (File.Exists(_file))
        {
            File.Delete(_file);
        }
    }
}
