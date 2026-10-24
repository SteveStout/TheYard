// The Blog slice's handlers, one per route (ADR: The onion cut into vertical slices, rule 7; ADR: Every
// LinkedIn post is also a blog post). Each takes its port and its clock by constructor and is tested with
// the memory store and no host. The Domain's BlogRules decide what a read's source is and what an event
// may carry; these only ask them and keep what they admit.
using TheYard.Application.Shared;
using TheYard.Data;
using TheYard.Domain.Features.Blog;

namespace TheYard.Application.Features.Blog;

/// <summary>Every published post, newest first, each with its reads, for the Blog page and the home page's band.</summary>
/// <param name="store">Where the posts are kept.</param>
/// <param name="clock">The clock the week's reads are counted against.</param>
public sealed class ListPostsHandler(IBlogReader store, TimeProvider clock) : IQueryHandler<ListPostsQuery, (IReadOnlyList<BlogPost> Posts, IReadOnlyList<BlogStat> Stats, string? Unavailable)>
{
    /// <summary>The posts and their numbers, or the store's reason when it cannot read now.</summary>
    /// <param name="query">The list, which takes nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<(IReadOnlyList<BlogPost> Posts, IReadOnlyList<BlogStat> Stats, string? Unavailable)> HandleAsync(ListPostsQuery query, CancellationToken cancellation)
    {
        var availability = await store.AvailabilityAsync(cancellation);
        if (!availability.Available)
        {
            return ([], [], availability.Reason);
        }
        return (await store.PostsAsync(cancellation), await store.StatsAsync(clock.GetUtcNow(), cancellation), null);
    }
}

/// <summary>Every published post, newest first; there is nothing to ask beyond the asking.</summary>
public sealed record ListPostsQuery;

/// <summary>One published post by its slug.</summary>
/// <param name="store">Where the posts are kept.</param>
public sealed class ReadPostHandler(IBlogReader store) : IQueryHandler<ReadPostQuery, BlogPost?>
{
    /// <summary>The post, or null when no published post has the slug or the slug is not one.</summary>
    /// <param name="query">The slug.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<BlogPost?> HandleAsync(ReadPostQuery query, CancellationToken cancellation) =>
        BlogRules.IsSlug(query.Slug) && (await store.AvailabilityAsync(cancellation)).Available ? await store.PostAsync(query.Slug, cancellation) : null;
}

/// <summary>One published post.</summary>
/// <param name="Slug">The post's slug.</param>
public sealed record ReadPostQuery(string Slug);

/// <summary>What a page sends when a post is opened, before the rules have read it: a read to keep, so a command.</summary>
/// <param name="Slug">The post.</param>
/// <param name="ReferrerHost">The referring page's host; null for none.</param>
/// <param name="RefCode">The tagged link's code; null for none.</param>
/// <param name="UtmSource">utm_source; null for none.</param>
/// <param name="UtmMedium">utm_medium; null for none.</param>
/// <param name="UtmCampaign">utm_campaign; null for none.</param>
/// <param name="Device">phone, tablet, desk or bot, from BlogRules.Device.</param>
/// <param name="Country">The edge's two letters; null for none.</param>
/// <param name="Session">The day's keyed hash of the reader.</param>
/// <param name="IsBot">The site's guess at a bot.</param>
public sealed record RecordReadCommand(string Slug, string? ReferrerHost, string? RefCode, string? UtmSource, string? UtmMedium, string? UtmCampaign, string Device, string? Country, byte[] Session, bool IsBot);

/// <summary>Keeps one read of a post.</summary>
/// <param name="store">Where the read log is kept.</param>
/// <param name="clock">The clock a read is stamped with.</param>
public sealed class RecordReadHandler(IBlogStore store, TimeProvider clock) : ICommandHandler<RecordReadCommand, string?>
{
    /// <summary>The source the read was kept under; null when no published post has the slug.</summary>
    /// <param name="open">The open the page sent.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<string?> HandleAsync(RecordReadCommand open, CancellationToken cancellation)
    {
        var post = BlogRules.IsSlug(open.Slug) && (await store.AvailabilityAsync(cancellation)).Available ? await store.PostAsync(open.Slug, cancellation) : null;
        if (post is null)
        {
            return null;
        }
        string source = BlogRules.Source(open.ReferrerHost, open.RefCode, open.UtmSource);
        await store.KeepReadAsync(new BlogRead(post.Id, clock.GetUtcNow(), source, open.RefCode, Cut(open.ReferrerHost, 120), Cut(open.UtmSource, 60), Cut(open.UtmMedium, 60), Cut(open.UtmCampaign, 60), open.Device, open.Country, open.Session, open.IsBot), cancellation);
        return source;
    }

    private static string? Cut(string? text, int max) => string.IsNullOrEmpty(text) ? null : text.Length <= max ? text : text[..max];
}

/// <summary>Keeps one thing a reader did on a post.</summary>
/// <param name="store">Where the event log is kept.</param>
/// <param name="clock">The clock an event is stamped with.</param>
public sealed class RecordEventHandler(IBlogStore store, TimeProvider clock) : ICommandHandler<RecordEventCommand, string?>
{
    /// <summary>Null when kept; otherwise the one sentence why not.</summary>
    /// <param name="command">The post, the event and the reader.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<string?> HandleAsync(RecordEventCommand command, CancellationToken cancellation)
    {
        var (slug, type, value, session, isBot) = command;
        if (BlogRules.Wrong(type, value) is { } wrong)
        {
            return wrong;
        }
        var post = BlogRules.IsSlug(slug) && (await store.AvailabilityAsync(cancellation)).Available ? await store.PostAsync(slug, cancellation) : null;
        if (post is null)
        {
            return "no published post has that slug";
        }
        await store.KeepEventAsync(new BlogEvent(post.Id, session, clock.GetUtcNow(), type, value, isBot), cancellation);
        return null;
    }
}

/// <summary>One thing a reader did on a post.</summary>
/// <param name="Slug">The post.</param>
/// <param name="Type">depth or click.</param>
/// <param name="Value">The depth reached or the link clicked.</param>
/// <param name="Session">The day's keyed hash of the reader.</param>
/// <param name="IsBot">The site's guess at a bot.</param>
public sealed record RecordEventCommand(string Slug, string Type, string Value, byte[] Session, bool IsBot);

/// <summary>Every post's numbers and the month's reads by source, for the Admin tab's Blog analytics card.</summary>
/// <param name="store">Where the logs are kept.</param>
/// <param name="clock">The clock the windows are read against.</param>
public sealed class ReadBlogStatsHandler(IBlogReader store, TimeProvider clock) : IQueryHandler<ReadBlogStatsQuery, (IReadOnlyList<BlogStat> Stats, IReadOnlyList<BlogSourceCount> Sources, BlogAvailability Kept)>
{
    /// <summary>The numbers, where they are kept, and the sources over the last thirty days.</summary>
    /// <param name="query">The numbers, which take nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<(IReadOnlyList<BlogStat> Stats, IReadOnlyList<BlogSourceCount> Sources, BlogAvailability Kept)> HandleAsync(ReadBlogStatsQuery query, CancellationToken cancellation)
    {
        var availability = await store.AvailabilityAsync(cancellation);
        if (!availability.Available)
        {
            return ([], [], availability);
        }
        var now = clock.GetUtcNow();
        return (await store.StatsAsync(now, cancellation), await store.SourcesAsync(now.AddDays(-30), cancellation), availability);
    }
}

/// <summary>The Blog analytics card's numbers; there is nothing to ask beyond the asking.</summary>
public sealed record ReadBlogStatsQuery;

/// <summary>Adds a post or replaces the one with its slug, for the operator's one write path.</summary>
/// <param name="store">Where the posts are kept.</param>
/// <param name="clock">The clock the row is stamped with.</param>
public sealed class SavePostHandler(IBlogStore store, TimeProvider clock) : ICommandHandler<SavePostCommand, (int? Id, string? Wrong, string? Unavailable)>
{
    /// <summary>The id kept under; or the one sentence wrong with the post; or the store's reason when it cannot keep posts now.</summary>
    /// <param name="command">The post.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<(int? Id, string? Wrong, string? Unavailable)> HandleAsync(SavePostCommand command, CancellationToken cancellation)
    {
        var post = command.Post;
        string? wrong = !BlogRules.IsSlug(post.Slug) ? "the slug is lower-case words joined by dashes, 3 to 120 characters"
            : post.Title.Trim().Length is 0 or > 200 ? "the title is 1 to 200 characters"
            : post.Summary.Trim().Length is 0 or > 400 ? "the summary is 1 to 400 characters"
            : post.Body.Trim().Length == 0 ? "the body is the post's text"
            : !post.LinkedInUrl.StartsWith("https://www.linkedin.com/", StringComparison.Ordinal) ? "the LinkedIn address starts https://www.linkedin.com/"
            : post.Images.Any(i => i.AltText.Trim().Length == 0) ? "every image says what it shows"
            : null;
        if (wrong is not null)
        {
            return (null, wrong, null);
        }
        var availability = await store.AvailabilityAsync(cancellation);
        return availability.Available ? (await store.SaveAsync(post, clock.GetUtcNow(), cancellation), null, null) : (null, null, availability.Reason);
    }
}

/// <summary>A post to add, or to replace the one with its slug.</summary>
/// <param name="Post">The post.</param>
public sealed record SavePostCommand(BlogPost Post);
