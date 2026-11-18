using Microsoft.EntityFrameworkCore;
using TheYard.Application.Features.Blog;
using TheYard.Data;

namespace TheYard.Infrastructure.Features.Blog;

/// <summary>
/// The blog kept in the relational store (ADR: Every LinkedIn post is also a blog post): the posts with
/// their images and tags, and the read and event logs the numbers are worked out from. Like every table
/// on SQL Server, the blog's are published by a person from the SQL project, so this store asks once
/// whether they are there and says so on the Admin tab rather than throwing on every read.
/// </summary>
/// <param name="factory">The relational store's contexts; null while the store is not attached.</param>
public sealed class EfBlogStore(Func<IDbContextFactory<YardDbContext>?> factory) : IBlogStore
{
    private BlogAvailability? _availability;

    /// <summary>
    /// Whether the blog's tables are there to be read, and a line saying where they are. Asks once by
    /// taking a single row from each of the three, then keeps a yes: a person publishes the schema from
    /// the SQL project, so a database without it is a state to report on the Admin tab rather than an
    /// exception on every read. A no is not kept, so the answer turns to yes the moment the tables land.
    /// </summary>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task<BlogAvailability> AvailabilityAsync(CancellationToken cancellation)
    {
        if (_availability is { Available: true })
        {
            return _availability;
        }
        if (factory() is not { } contexts)
        {
            return new BlogAvailability(false, "the relational store is not attached, and the blog lives in it");
        }
        try
        {
            using var db = await contexts.CreateDbContextAsync(cancellation);
            // Every table a read touches, so a schema published in part reads as not published, never as a failed list.
            await db.Posts.AsNoTracking().Take(1).ToListAsync(cancellation);
            await db.PostImages.AsNoTracking().Take(1).ToListAsync(cancellation);
            await db.PostTags.AsNoTracking().Take(1).ToListAsync(cancellation);
            await db.PostViews.AsNoTracking().Take(1).ToListAsync(cancellation);
            await db.PostEvents.AsNoTracking().Take(1).ToListAsync(cancellation);
            await db.Set<PostWordRow>().AsNoTracking().Take(1).ToListAsync(cancellation);
            await db.Set<PostKeywordRow>().AsNoTracking().Take(1).ToListAsync(cancellation);
            await db.Set<SearchSynonymRow>().AsNoTracking().Take(1).ToListAsync(cancellation);
            return _availability = new BlogAvailability(true, db.Database.IsSqlite() ? "kept in SQLite" : "kept in Azure SQL Database, the blog schema");
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested && ex is not OperationCanceledException)
        {
            return new BlogAvailability(false, "the blog schema is not published to this database yet (ADR: Data first, and the database in source control)");
        }
    }

    /// <summary>Every published post, newest first, each with its images in order and its tags; the feed reads it.</summary>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task<IReadOnlyList<BlogPost>> PostsAsync(CancellationToken cancellation)
    {
        using var db = await Contexts().CreateDbContextAsync(cancellation);
        var posts = await db.Posts.AsNoTracking().Where(p => p.IsPublished).OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.Id).ToListAsync(cancellation);
        return await WithPartsAsync(db, posts, cancellation);
    }

    /// <summary>One page of the list, filtered, ordered and paged in SQL (EfBlogList).</summary>
    /// <param name="filter">What the list is asked for.</param>
    /// <param name="now">The moment the week's reads are counted back from.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task<BlogListPage> ListAsync(BlogFilter filter, DateTimeOffset now, CancellationToken cancellation)
    {
        using var db = await Contexts().CreateDbContextAsync(cancellation);
        return await EfBlogList.PageAsync(db, filter, now, cancellation);
    }

    /// <summary>One published post by its address, with its parts, or null when no published post answers at that address.</summary>
    /// <param name="slug">The post's address on the site.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task<BlogPost?> PostAsync(string slug, CancellationToken cancellation)
    {
        using var db = await Contexts().CreateDbContextAsync(cancellation);
        var post = await db.Posts.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug && p.IsPublished, cancellation);
        return post is null ? null : (await WithPartsAsync(db, [post], cancellation))[0];
    }

    /// <summary>
    /// Writes a post, by its address, and gives back its id. A post already there is updated rather than
    /// added, and its images and tags are replaced whole rather than reconciled one by one, because the
    /// post as it went out is the authority and a half-matched set would be neither version. Tags are
    /// deduplicated without regard to case, so a post that says a hashtag twice keeps one row.
    /// </summary>
    /// <param name="post">The post as it went out, with its images and tags.</param>
    /// <param name="derived">What the Domain worked out from it: the drawn body, the minutes, the words and the keywords.</param>
    /// <param name="at">The moment the save is stamped with, handed in so the store keeps no clock of its own.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task<int> SaveAsync(BlogPost post, BlogDerived derived, DateTimeOffset at, CancellationToken cancellation)
    {
        using var db = await Contexts().CreateDbContextAsync(cancellation);
        var row = await db.Posts.FirstOrDefaultAsync(p => p.Slug == post.Slug, cancellation);
        if (row is null)
        {
            row = new PostRow { Slug = post.Slug, Title = post.Title, Summary = post.Summary, Body = post.Body, LinkedInUrl = post.LinkedInUrl, CreatedAt = at };
            db.Posts.Add(row);
        }
        row.Title = post.Title;
        row.Summary = post.Summary;
        row.Body = post.Body;
        row.PublishedAt = post.PublishedAt;
        row.LinkedInUrl = post.LinkedInUrl;
        row.CodeUrl = post.CodeUrl;
        row.IsPublished = true;
        row.UpdatedAt = at;
        await db.SaveChangesAsync(cancellation);
        db.PostImages.RemoveRange(db.PostImages.Where(i => i.PostId == row.Id));
        db.PostTags.RemoveRange(db.PostTags.Where(t => t.PostId == row.Id));
        db.PostImages.AddRange(post.Images.Select(i => new PostImageRow { PostId = row.Id, Position = i.Position, Url = i.Url, AltText = i.AltText, Width = i.Width, Height = i.Height }));
        db.PostTags.AddRange(post.Tags.Distinct(StringComparer.OrdinalIgnoreCase).Select(t => new PostTagRow { PostId = row.Id, Tag = t }));
        await db.SaveChangesAsync(cancellation);
        await EfBlogIndex.KeepAsync(db, row.Id, derived, null, cancellation);
        return row.Id;
    }

    /// <summary>Every post whose derived parts are from another version of the rules, or from none, with its parts and whether it never was.</summary>
    /// <param name="version">The rules' version now.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task<IReadOnlyList<(BlogPost Post, bool Never)>> StaleAsync(int version, CancellationToken cancellation)
    {
        using var db = await Contexts().CreateDbContextAsync(cancellation);
        var rows = await db.Posts.AsNoTracking().Where(p => p.DerivedWith == null || p.DerivedWith != version).OrderBy(p => p.Id).ToListAsync(cancellation);
        var never = rows.Where(p => p.DerivedWith is null).Select(p => p.Id).ToHashSet();
        return [.. (await WithPartsAsync(db, rows, cancellation)).Select(p => (p, never.Contains(p.Id)))];
    }

    /// <summary>One published post as its page shows it (EfBlogList), or null when no published post has the slug.</summary>
    /// <param name="slug">The post's address.</param>
    /// <param name="now">The moment the week's reads are counted back from.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task<BlogPostPage?> PostPageAsync(string slug, DateTimeOffset now, CancellationToken cancellation)
    {
        using var db = await Contexts().CreateDbContextAsync(cancellation);
        var row = await db.Posts.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug && p.IsPublished, cancellation);
        return row is null ? null : await EfBlogList.PostPageAsync(db, (await WithPartsAsync(db, [row], cancellation))[0], now, cancellation);
    }

    /// <summary>Keeps a post's derived parts, and its topics when they are given (EfBlogIndex).</summary>
    /// <param name="postId">The post.</param>
    /// <param name="derived">What the Domain worked out from it.</param>
    /// <param name="topics">Its topics to keep in place of its tags; null to leave them.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task KeepDerivedAsync(int postId, BlogDerived derived, IReadOnlyList<string>? topics, CancellationToken cancellation)
    {
        using var db = await Contexts().CreateDbContextAsync(cancellation);
        await EfBlogIndex.KeepAsync(db, postId, derived, topics, cancellation);
    }

    /// <summary>Replaces blog.SearchSynonym's rows whole (EfBlogIndex).</summary>
    /// <param name="rows">Each typed word with each term it widens to.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task KeepSynonymsAsync(IReadOnlyList<(string Word, string Term)> rows, CancellationToken cancellation)
    {
        using var db = await Contexts().CreateDbContextAsync(cancellation);
        await EfBlogIndex.KeepSynonymsAsync(db, rows, cancellation);
    }

    /// <summary>
    /// Appends one read to the log, bots included: a read thrown away could never be counted another way,
    /// and every number the cards show leaves the bots out when it reads rather than when it writes.
    /// </summary>
    /// <param name="read">The read, already reduced to the day's keyed hash and a device class.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task KeepReadAsync(BlogRead read, CancellationToken cancellation)
    {
        using var db = await Contexts().CreateDbContextAsync(cancellation);
        db.PostViews.Add(new PostViewRow
        {
            PostId = read.PostId,
            ViewedAt = read.At,
            Source = read.Source,
            RefCode = read.RefCode,
            ReferrerHost = read.ReferrerHost,
            UtmSource = read.UtmSource,
            UtmMedium = read.UtmMedium,
            UtmCampaign = read.UtmCampaign,
            Device = read.Device,
            Country = read.Country,
            SessionHash = read.Session,
            IsBot = read.IsBot,
        });
        await db.SaveChangesAsync(cancellation);
    }

    /// <summary>Appends one thing a reader did to the event log, bots included, for the same reason a read is kept that way.</summary>
    /// <param name="blogEvent">The event, under the same day's hash its read was kept under.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task KeepEventAsync(BlogEvent blogEvent, CancellationToken cancellation)
    {
        using var db = await Contexts().CreateDbContextAsync(cancellation);
        db.PostEvents.Add(new PostEventRow { PostId = blogEvent.PostId, SessionHash = blogEvent.Session, At = blogEvent.At, Type = blogEvent.Type, Value = blogEvent.Value, IsBot = blogEvent.IsBot });
        await db.SaveChangesAsync(cancellation);
    }

    /// <summary>
    /// Each published post's numbers over a window, bots left out. The window and the bots are cut in SQL, through
    /// the filtered window indexes, and the funnel's arithmetic stays in BlogNumbers, the one place it lives: the
    /// rows a window holds are the reads and events of thirty days at most, never the whole of either log.
    /// </summary>
    /// <param name="now">The moment the seven-day count is measured back from, handed in so the store keeps no clock of its own.</param>
    /// <param name="since">The start of the window.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task<IReadOnlyList<BlogStat>> StatsAsync(DateTimeOffset now, DateTimeOffset since, CancellationToken cancellation)
    {
        using var db = await Contexts().CreateDbContextAsync(cancellation);
        var posts = await db.Posts.AsNoTracking().Where(p => p.IsPublished).OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.Id).ToListAsync(cancellation);
        var reads = (await db.PostViews.AsNoTracking().Where(v => !v.IsBot && v.ViewedAt >= since).ToListAsync(cancellation)).Select(Read).ToLookup(r => r.PostId);
        var events = (await db.PostEvents.AsNoTracking().Where(e => !e.IsBot && e.At >= since).ToListAsync(cancellation))
            .Select(e => new BlogEvent(e.PostId, e.SessionHash, e.At, e.Type, e.Value, e.IsBot)).ToLookup(e => e.PostId);
        var parts = await WithPartsAsync(db, posts, cancellation);
        return [.. parts.Select(p => BlogNumbers.Of(p, reads[p.Id], events[p.Id], now))];
    }

    /// <summary>Reads of the blog by where they came from since a moment, most read first, bots left out, counted in SQL.</summary>
    /// <param name="since">The start of the window.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task<IReadOnlyList<BlogSourceCount>> SourcesAsync(DateTimeOffset since, CancellationToken cancellation)
    {
        using var db = await Contexts().CreateDbContextAsync(cancellation);
        var counts = await db.PostViews.AsNoTracking().Where(v => !v.IsBot && v.ViewedAt >= since)
            .GroupBy(v => v.Source).Select(g => new { Source = g.Key, Reads = g.Count() }).ToListAsync(cancellation);
        return [.. counts.OrderByDescending(c => c.Reads).ThenBy(c => c.Source, StringComparer.Ordinal).Select(c => new BlogSourceCount(c.Source, c.Reads))];
    }

    private IDbContextFactory<YardDbContext> Contexts() => factory() ?? throw new InvalidOperationException("the relational store is not attached");

    private static BlogRead Read(PostViewRow v) =>
        new(v.PostId, v.ViewedAt, v.Source, v.RefCode, v.ReferrerHost, v.UtmSource, v.UtmMedium, v.UtmCampaign, v.Device, v.Country, v.SessionHash, v.IsBot);

    private static async Task<IReadOnlyList<BlogPost>> WithPartsAsync(YardDbContext db, List<PostRow> posts, CancellationToken cancellation)
    {
        var ids = posts.Select(p => p.Id).ToList();
        var images = await db.PostImages.AsNoTracking().Where(i => ids.Contains(i.PostId)).ToListAsync(cancellation);
        var tags = await db.PostTags.AsNoTracking().Where(t => ids.Contains(t.PostId)).ToListAsync(cancellation);
        return [.. posts.Select(p => new BlogPost(p.Id, p.Slug, p.Title, p.Summary, p.Body, p.PublishedAt, p.LinkedInUrl, p.CodeUrl,
            [.. images.Where(i => i.PostId == p.Id).OrderBy(i => i.Position).Select(i => new BlogImage(i.Position, i.Url, i.AltText, i.Width, i.Height))],
            [.. tags.Where(t => t.PostId == p.Id).Select(t => t.Tag).Order(StringComparer.Ordinal)]))];
    }
}
