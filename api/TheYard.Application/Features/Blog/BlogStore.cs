// The blog's port (ADR: Every LinkedIn post is also a blog post): the posts, newest first; one post by its
// slug; the read and event logs; and each post's numbers. The relational store is the one adapter that
// keeps them (the logs join the posts), and a process with no relational store keeps them in memory and
// says so, the way the activity counters do.
using TheYard.Data;
using TheYard.Domain.Features.Blog;

namespace TheYard.Application.Features.Blog;

// #region blog-reader
/// <summary>
/// The read side of the blog's store: what a query may ask, and nothing that writes (ADR-115: A request is a
/// query or a command, Interface Segregation). The query handlers take this; the command handlers take the
/// whole store.
/// </summary>
public interface IBlogReader
{
    /// <summary>Whether the store can keep posts and reads now, and why not when it cannot.</summary>
    Task<BlogAvailability> AvailabilityAsync(CancellationToken cancellation);

    /// <summary>Every published post, newest first.</summary>
    Task<IReadOnlyList<BlogPost>> PostsAsync(CancellationToken cancellation);

    /// <summary>One published post by its slug, or null.</summary>
    Task<BlogPost?> PostAsync(string slug, CancellationToken cancellation);

    /// <summary>
    /// One page of the list (ADR: Every LinkedIn post is also a blog post, the addendum on the read model): the cards
    /// the filter matches in its order, how many match in all, the chips' counts, and the next page's cursor. A card
    /// carries no body, and its reads are counted, never read row by row.
    /// </summary>
    Task<BlogListPage> ListAsync(BlogFilter filter, DateTimeOffset now, CancellationToken cancellation);

    /// <summary>One published post as its page shows it, with its older, newer and related posts; null when no published post has the slug.</summary>
    Task<BlogPostPage?> PostPageAsync(string slug, DateTimeOffset now, CancellationToken cancellation);

    /// <summary>Each published post's numbers over the reads and events since an instant, bots left out, newest post first.</summary>
    Task<IReadOnlyList<BlogStat>> StatsAsync(DateTimeOffset now, DateTimeOffset since, CancellationToken cancellation);

    /// <summary>Reads by source since an instant, bots left out.</summary>
    Task<IReadOnlyList<BlogSourceCount>> SourcesAsync(DateTimeOffset since, CancellationToken cancellation);
}
// #endregion blog-reader

// #region blog-store
/// <summary>Where the blog's posts and its two logs are kept: the reader, and the writes.</summary>
public interface IBlogStore : IBlogReader
{
    /// <summary>Adds a post, or replaces the one with the same slug, with what was worked out from it; returns its id.</summary>
    Task<int> SaveAsync(BlogPost post, BlogDerived derived, DateTimeOffset at, CancellationToken cancellation);

    /// <summary>Every post whose derived parts were worked out with rules other than this version, each with whether they never were.</summary>
    Task<IReadOnlyList<(BlogPost Post, bool Never)>> StaleAsync(int version, CancellationToken cancellation);

    /// <summary>Keeps a post's derived parts, and its topics too when they are given.</summary>
    Task KeepDerivedAsync(int postId, BlogDerived derived, IReadOnlyList<string>? topics, CancellationToken cancellation);

    /// <summary>Replaces the synonyms the search widens a typed word through.</summary>
    Task KeepSynonymsAsync(IReadOnlyList<(string Word, string Term)> rows, CancellationToken cancellation);

    /// <summary>Keeps one read.</summary>
    Task KeepReadAsync(BlogRead read, CancellationToken cancellation);

    /// <summary>Keeps one event.</summary>
    Task KeepEventAsync(BlogEvent blogEvent, CancellationToken cancellation);
}
// #endregion blog-store

/// <summary>Whether the blog's store can keep and read now.</summary>
/// <param name="Available">True when it can.</param>
/// <param name="Reason">Where it keeps things, or why it cannot.</param>
public sealed record BlogAvailability(bool Available, string Reason);

/// <summary>
/// The numbers blog.PostStats and blog.Funnel give, worked out from the logs in code, so a store on either
/// provider answers the same: the views are the database's own copy of this method, for a reader in SSMS,
/// and BlogStatsTests holds this one to a hand count.
/// </summary>
public static class BlogNumbers
{
    /// <summary>One post's numbers from its reads and events.</summary>
    public static BlogStat Of(BlogPost post, IEnumerable<BlogRead> reads, IEnumerable<BlogEvent> events, DateTimeOffset now)
    {
        var people = reads.Where(r => !r.IsBot).ToList();
        var acts = events.Where(e => !e.IsBot).ToList();
        static string Key(byte[] session) => Convert.ToHexString(session);
        static int? Number(string value) => int.TryParse(value, out int n) ? n : null;
        var deepest = acts.Where(e => e.Type == "depth").GroupBy(e => Key(e.Session)).Select(g => g.Max(e => Number(e.Value) ?? 0)).ToList();
        var longest = acts.Where(e => e.Type == "time").GroupBy(e => Key(e.Session)).Select(g => g.Max(e => Number(e.Value) ?? 0)).ToList();
        int Clicked(string where) => acts.Where(e => e.Type == "click" && e.Value == where).Select(e => Key(e.Session)).Distinct().Count();
        return new BlogStat(
            post.Id, post.Title, post.PublishedAt,
            Reads: people.Count,
            Readers: people.Select(r => Key(r.Session)).Distinct().Count(),
            Reads7d: people.Count(r => r.At > now.AddDays(-7)),
            FromTaggedLinks: people.Count(r => r.RefCode is not null),
            LastRead: people.Count == 0 ? null : people.Max(r => r.At),
            AverageDepth: deepest.Count == 0 ? null : deepest.Average(),
            AverageSeconds: longest.Count == 0 ? null : longest.Average(),
            ToLinkedIn: Clicked("linkedin"),
            ToCode: Clicked("code"),
            PastHalf: deepest.Count(d => d >= 50),
            Finished: deepest.Count(d => d >= 100),
            ClickedOut: acts.Where(e => e.Type == "click" && BlogRules.IsWayOut(e.Value)).Select(e => Key(e.Session)).Distinct().Count());
    }
}
