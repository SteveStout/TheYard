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

    public async Task<IReadOnlyList<BlogPost>> PostsAsync(CancellationToken cancellation)
    {
        using var db = await Contexts().CreateDbContextAsync(cancellation);
        var posts = (await db.Posts.AsNoTracking().Where(p => p.IsPublished).ToListAsync(cancellation)).OrderByDescending(p => p.PublishedAt).ToList();
        return await WithPartsAsync(db, posts, cancellation);
    }

    public async Task<BlogPost?> PostAsync(string slug, CancellationToken cancellation)
    {
        using var db = await Contexts().CreateDbContextAsync(cancellation);
        var post = await db.Posts.AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug && p.IsPublished, cancellation);
        return post is null ? null : (await WithPartsAsync(db, [post], cancellation))[0];
    }

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

    public async Task KeepEventAsync(BlogEvent blogEvent, CancellationToken cancellation)
    {
        using var db = await Contexts().CreateDbContextAsync(cancellation);
        db.PostEvents.Add(new PostEventRow { PostId = blogEvent.PostId, SessionHash = blogEvent.Session, At = blogEvent.At, Type = blogEvent.Type, Value = blogEvent.Value, IsBot = blogEvent.IsBot });
        await db.SaveChangesAsync(cancellation);
    }

    public async Task<IReadOnlyList<BlogStat>> StatsAsync(DateTimeOffset now, CancellationToken cancellation)
    {
        using var db = await Contexts().CreateDbContextAsync(cancellation);
        var posts = await PostsAsync(cancellation);
        var reads = (await db.PostViews.AsNoTracking().Where(v => !v.IsBot).ToListAsync(cancellation)).Select(Read).ToList();
        var events = (await db.PostEvents.AsNoTracking().Where(e => !e.IsBot).ToListAsync(cancellation))
            .Select(e => new BlogEvent(e.PostId, e.SessionHash, e.At, e.Type, e.Value, e.IsBot)).ToList();
        return [.. posts.Select(p => BlogNumbers.Of(p, reads.Where(r => r.PostId == p.Id), events.Where(e => e.PostId == p.Id), now))];
    }

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
