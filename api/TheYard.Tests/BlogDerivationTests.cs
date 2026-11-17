using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TheYard.Api.Features.Blog;
using TheYard.Application.Features.Blog;
using TheYard.Infrastructure;
using TheYard.Infrastructure.Features.Blog;

namespace TheYard.Tests;

/// <summary>
/// The pass that derives the posts again (ADR: Every LinkedIn post is also a blog post, the addendum on the read
/// model): a post kept before the rules existed is drawn, cut into words and given its keywords, and takes its topics
/// from the backfill the first time only; a post under an older version is derived again and keeps its topics; the
/// synonyms are written; and the pass's loop tries again until the store answers. With no host.
/// </summary>
public sealed class BlogDerivationTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 12, 0, 0, TimeSpan.Zero);

    private readonly string _file = Path.Combine(Path.GetTempPath(), $"theyard-blogderive-{Guid.NewGuid():N}.db");

    private YardDbContext Context() => new(new YardConnection(YardProvider.Sqlite, $"Data Source={_file};Pooling=False").Options());

    private sealed class SqliteContexts(Func<YardDbContext> make) : IDbContextFactory<YardDbContext>
    {
        public YardDbContext CreateDbContext() => make();
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Backfill =
        new Dictionary<string, IReadOnlyList<string>> { ["solid-in-five-real-files"] = ["architecture", "dotnet"], ["kept-and-derived"] = ["career"] };

    [Fact]
    public async Task A_post_kept_before_the_rules_is_derived_and_takes_the_backfill_s_topics_the_first_time_only()
    {
        using (var db = Context())
        {
            await db.Database.MigrateAsync();
            var solid = BlogFixture.Posts.Single(e => e.Post.Slug == "solid-in-five-real-files").Post;
            db.Posts.Add(new PostRow { Slug = solid.Slug, Title = solid.Title, Summary = solid.Summary, Body = solid.Body, PublishedAt = solid.PublishedAt, LinkedInUrl = solid.LinkedInUrl, IsPublished = true, CreatedAt = Now, UpdatedAt = Now });
            await db.SaveChangesAsync();
            db.PostTags.AddRange(new PostTagRow { PostId = 1, Tag = "csharp" }, new PostTagRow { PostId = 1, Tag = "softwarearchitecture" });
            await db.SaveChangesAsync();
        }
        var store = new EfBlogStore(() => new SqliteContexts(Context));
        var kept = new BlogPost(0, "kept-and-derived", "Kept and derived", "Two lines.", "About Claude, Claude and Claude.", Now, "https://www.linkedin.com/posts/kept", null, [], ["testing"]);
        await store.SaveAsync(kept, PostDerivation.Of(kept.Title, kept.Summary, kept.Body), Now, CancellationToken.None);

        var handler = new DerivePostsHandler(store);
        Assert.Equal(1, await handler.HandleAsync(new DerivePostsCommand(Backfill), CancellationToken.None));
        Assert.Equal(0, await handler.HandleAsync(new DerivePostsCommand(Backfill), CancellationToken.None));

        var page = await store.PostPageAsync("solid-in-five-real-files", Now, CancellationToken.None);
        Assert.StartsWith("<", page!.BodyHtml, StringComparison.Ordinal);
        Assert.Equal(["architecture", "dotnet"], page.Topics);
        Assert.Contains("solid", page.Keywords);
        Assert.Equal(["testing"], (await store.PostPageAsync("kept-and-derived", Now, CancellationToken.None))!.Topics);
        using (var db = Context())
        {
            Assert.Equal(KeywordRules.Version, (await db.Posts.SingleAsync(p => p.Slug == "solid-in-five-real-files")).DerivedWith);
            Assert.Equal(KeywordRules.SynonymRows().Count, await db.Set<SearchSynonymRow>().CountAsync());
            Assert.True(await db.Set<PostWordRow>().CountAsync(w => w.PostId == 1) > 100);
        }
        Assert.Equal(["solid-in-five-real-files"], (await store.ListAsync(new BlogFilter("solid", "architecture", null, null, null, "new", null), Now, CancellationToken.None)).Cards.Select(c => c.Slug));
    }

    [Fact]
    public async Task A_post_under_an_older_version_is_derived_again_and_keeps_its_topics()
    {
        var store = new MemoryBlogStore("in memory, for the test");
        var post = new BlogPost(0, "kept-and-derived", "Kept and derived", "Two lines.", "The text.", Now, "https://www.linkedin.com/posts/kept", null, [], ["testing"]);
        int id = await store.SaveAsync(post, PostDerivation.Of(post.Title, post.Summary, post.Body) with { Version = KeywordRules.Version - 1 }, Now, CancellationToken.None);
        Assert.Equal([(id, false)], (await store.StaleAsync(KeywordRules.Version, CancellationToken.None)).Select(s => (s.Post.Id, s.Never)));
        Assert.Equal(1, await new DerivePostsHandler(store).HandleAsync(new DerivePostsCommand(Backfill), CancellationToken.None));
        Assert.Empty(await store.StaleAsync(KeywordRules.Version, CancellationToken.None));
        Assert.Equal(["testing"], (await store.PostPageAsync("kept-and-derived", Now, CancellationToken.None))!.Topics);
    }

    [Fact]
    public async Task The_pass_tries_again_until_the_store_answers_and_stops_after_its_tries()
    {
        var answers = new Queue<Func<int?>>([() => null, () => throw new InvalidOperationException("the store is rolling"), () => 15]);
        int waits = 0;
        Task Wait(TimeSpan _, CancellationToken __)
        {
            waits++;
            return Task.CompletedTask;
        }
        Assert.Equal(15, await BlogDerivationPass.RunAsync(_ => Task.FromResult(answers.Dequeue()()), TimeSpan.FromMinutes(1), 5, Wait, NullLogger.Instance, CancellationToken.None));
        Assert.Equal(2, waits);
        Assert.Null(await BlogDerivationPass.RunAsync(_ => Task.FromResult<int?>(null), TimeSpan.FromMinutes(1), 3, Wait, NullLogger.Instance, CancellationToken.None));
        Assert.Equal(5, waits);
        Assert.Null(await new DerivePostsHandler(new UnavailableStore()).HandleAsync(new DerivePostsCommand(Backfill), CancellationToken.None));
    }

    [Fact]
    public async Task The_backfill_gives_every_live_post_the_mockup_s_topics_and_both_security_posts_security_and_job_search()
    {
        var topics = await BlogDerivationPass.TopicsAsync(Path.Combine(Repo.Root(), "data", "blog", "topics.json"), CancellationToken.None);
        Assert.Equal(BlogFixture.Posts.Select(e => (e.Post.Slug, string.Join(',', e.Topics))), topics.Select(t => (t.Key, string.Join(',', t.Value))));
        Assert.Equal(["security", "job-search"], topics["my-resume-came-back-as-a-job"]);
        Assert.Equal(["security", "job-search"], topics["scammers-read-the-job-market-too"]);
        Assert.All(topics.Values.SelectMany(t => t), topic => Assert.Contains(topic, KeywordRules.Topics.Select(k => k.Key)));
        Assert.Empty(await BlogDerivationPass.TopicsAsync(Path.Combine(Repo.Root(), "data", "blog", "no-such-file.json"), CancellationToken.None));
    }

    /// <summary>A store that says it cannot be written, as one with no relational store attached does.</summary>
    private sealed class UnavailableStore : IBlogStore
    {
        public Task<BlogAvailability> AvailabilityAsync(CancellationToken cancellation) => Task.FromResult(new BlogAvailability(false, "not attached, for the test"));

        public Task<IReadOnlyList<BlogPost>> PostsAsync(CancellationToken cancellation) => throw new InvalidOperationException();

        public Task<BlogPost?> PostAsync(string slug, CancellationToken cancellation) => throw new InvalidOperationException();

        public Task<BlogListPage> ListAsync(BlogFilter filter, DateTimeOffset now, CancellationToken cancellation) => throw new InvalidOperationException();

        public Task<BlogPostPage?> PostPageAsync(string slug, DateTimeOffset now, CancellationToken cancellation) => throw new InvalidOperationException();

        public Task<IReadOnlyList<BlogStat>> StatsAsync(DateTimeOffset now, DateTimeOffset since, CancellationToken cancellation) => throw new InvalidOperationException();

        public Task<IReadOnlyList<BlogSourceCount>> SourcesAsync(DateTimeOffset since, CancellationToken cancellation) => throw new InvalidOperationException();

        public Task<int> SaveAsync(BlogPost post, BlogDerived derived, DateTimeOffset at, CancellationToken cancellation) => throw new InvalidOperationException();

        public Task<IReadOnlyList<(BlogPost Post, bool Never)>> StaleAsync(int version, CancellationToken cancellation) => throw new InvalidOperationException();

        public Task KeepDerivedAsync(int postId, BlogDerived derived, IReadOnlyList<string>? topics, CancellationToken cancellation) => throw new InvalidOperationException();

        public Task KeepSynonymsAsync(IReadOnlyList<(string Word, string Term)> rows, CancellationToken cancellation) => throw new InvalidOperationException();

        public Task KeepReadAsync(BlogRead read, CancellationToken cancellation) => throw new InvalidOperationException();

        public Task KeepEventAsync(BlogEvent blogEvent, CancellationToken cancellation) => throw new InvalidOperationException();
    }

    public void Dispose()
    {
        if (File.Exists(_file))
        {
            File.Delete(_file);
        }
    }
}
