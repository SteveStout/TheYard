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
            await db.Posts.AsNoTracking().Take(1).ToListAsync(cancellation);
            await db.PostViews.AsNoTracking().Take(1).ToListAsync(cancellation);
            await db.PostEvents.AsNoTracking().Take(1).ToListAsync(cancellation);
            return _availability = new BlogAvailability(true, db.Database.IsSqlite() ? "kept in SQLite" : "kept in Azure SQL Database, the blog schema");
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested && ex is not OperationCanceledException)
        {
            return new BlogAvailability(false, "the blog schema is not published to this database yet (ADR: Data first, and the database in source control)");
        }
    }

    /// <summary>
    /// Every published post, newest first, each with its images in order and its tags. The ordering is
    /// done in memory because the rows are few and the alternative is a second index to keep.
    /// </summary>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task<IReadOnlyList<BlogPost>> PostsAsync(CancellationToken cancellation)
    {
        using var db = await Contexts().CreateDbContextAsync(cancellation);
        var posts = (await db.Posts.AsNoTracking().Where(p => p.IsPublished).ToListAsync(cancellation)).OrderByDescending(p => p.PublishedAt).ToList();
        return await WithPartsAsync(db, posts, cancellation);
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
    /// <param name="at">The moment the save is stamped with, handed in so the store keeps no clock of its own.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task<int> SaveAsync(BlogPost post, DateTimeOffset at, CancellationToken cancellation)
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
        return row.Id;
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
    /// Each published post's numbers, bots left out. Reads the two logs whole and counts in memory through
    /// the rules that own the arithmetic, so the funnel is worked out in one place and not half in SQL:
    /// the logs are small enough for that to be the cheaper honesty.
    /// </summary>
    /// <param name="now">The moment the windows are measured back from, handed in so the store keeps no clock of its own.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task<IReadOnlyList<BlogStat>> StatsAsync(DateTimeOffset now, CancellationToken cancellation)
    {
        using var db = await Contexts().CreateDbContextAsync(cancellation);
        var posts = await PostsAsync(cancellation);
        var reads = (await db.PostViews.AsNoTracking().Where(v => !v.IsBot).ToListAsync(cancellation)).Select(Read).ToList();
        var events = (await db.PostEvents.AsNoTracking().Where(e => !e.IsBot).ToListAsync(cancellation))
            .Select(e => new BlogEvent(e.PostId, e.SessionHash, e.At, e.Type, e.Value, e.IsBot)).ToList();
        return [.. posts.Select(p => BlogNumbers.Of(p, reads.Where(r => r.PostId == p.Id), events.Where(e => e.PostId == p.Id), now))];
    }

    /// <summary>Reads of the blog by where they came from since a moment, most read first, bots left out.</summary>
    /// <param name="since">The start of the window.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task<IReadOnlyList<BlogSourceCount>> SourcesAsync(DateTimeOffset since, CancellationToken cancellation)
    {
        using var db = await Contexts().CreateDbContextAsync(cancellation);
        var reads = await db.PostViews.AsNoTracking().Where(v => !v.IsBot).Select(v => new { v.Source, v.ViewedAt }).ToListAsync(cancellation);
        return [.. reads.Where(r => r.ViewedAt >= since).GroupBy(r => r.Source).Select(g => new BlogSourceCount(g.Key, g.Count())).OrderByDescending(s => s.Reads)];
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
