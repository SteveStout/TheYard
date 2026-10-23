// The blog's port (ADR: Every LinkedIn post is also a blog post): the posts, newest first; one post by its
// slug; the read and event logs; and each post's numbers. The relational store is the one adapter that
// keeps them (the logs join the posts), and a process with no relational store keeps them in memory and
// says so, the way the activity counters do.
using TheYard.Data;

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

    /// <summary>Each published post's numbers, bots left out, newest post first.</summary>
    Task<IReadOnlyList<BlogStat>> StatsAsync(DateTimeOffset now, CancellationToken cancellation);

    /// <summary>Reads by source since an instant, bots left out.</summary>
    Task<IReadOnlyList<BlogSourceCount>> SourcesAsync(DateTimeOffset since, CancellationToken cancellation);
}
// #endregion blog-reader

// #region blog-store
/// <summary>Where the blog's posts and its two logs are kept: the reader, and the three writes.</summary>
public interface IBlogStore : IBlogReader
{
    /// <summary>Adds a post, or replaces the one with the same slug; returns its id.</summary>
    Task<int> SaveAsync(BlogPost post, DateTimeOffset at, CancellationToken cancellation);

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
            ClickedOut: acts.Where(e => e.Type == "click").Select(e => Key(e.Session)).Distinct().Count());
    }
}

/// <summary>
/// The blog kept in this process's memory: for a process with no relational store, and for the tests.
/// Bounded like every log the site keeps in memory, so a flood of reads cannot grow it without end.
/// </summary>
/// <param name="reason">What the Admin tab says about where the blog is kept.</param>
public sealed class MemoryBlogStore(string reason) : IBlogStore
{
    /// <summary>The most reads and events each kept before the oldest go.</summary>
    public const int MaxRows = 20_000;

    private readonly Lock _gate = new();
    private readonly List<BlogPost> _posts = [];
    private readonly Queue<BlogRead> _reads = new();
    private readonly Queue<BlogEvent> _events = new();

    public Task<BlogAvailability> AvailabilityAsync(CancellationToken cancellation) => Task.FromResult(new BlogAvailability(true, reason));

    public Task<IReadOnlyList<BlogPost>> PostsAsync(CancellationToken cancellation)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<BlogPost>>([.. _posts.OrderByDescending(p => p.PublishedAt)]);
        }
    }

    public Task<BlogPost?> PostAsync(string slug, CancellationToken cancellation)
    {
        lock (_gate)
        {
            return Task.FromResult(_posts.FirstOrDefault(p => p.Slug == slug));
        }
    }

    public Task<int> SaveAsync(BlogPost post, DateTimeOffset at, CancellationToken cancellation)
    {
        lock (_gate)
        {
            int index = _posts.FindIndex(p => p.Slug == post.Slug);
            int id = index >= 0 ? _posts[index].Id : _posts.Count == 0 ? 1 : _posts.Max(p => p.Id) + 1;
            var kept = post with { Id = id };
            if (index >= 0)
            {
                _posts[index] = kept;
            }
            else if (_posts.Count < MaxRows)
            {
                _posts.Add(kept);
            }
            return Task.FromResult(id);
        }
    }

    public Task KeepReadAsync(BlogRead read, CancellationToken cancellation)
    {
        lock (_gate)
        {
            _reads.Enqueue(read);
            while (_reads.Count > MaxRows)
            {
                _reads.Dequeue();
            }
        }
        return Task.CompletedTask;
    }

    public Task KeepEventAsync(BlogEvent blogEvent, CancellationToken cancellation)
    {
        lock (_gate)
        {
            _events.Enqueue(blogEvent);
            while (_events.Count > MaxRows)
            {
                _events.Dequeue();
            }
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BlogStat>> StatsAsync(DateTimeOffset now, CancellationToken cancellation)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<BlogStat>>([.. _posts.OrderByDescending(p => p.PublishedAt)
                .Select(p => BlogNumbers.Of(p, _reads.Where(r => r.PostId == p.Id), _events.Where(e => e.PostId == p.Id), now))]);
        }
    }

    public Task<IReadOnlyList<BlogSourceCount>> SourcesAsync(DateTimeOffset since, CancellationToken cancellation)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<BlogSourceCount>>([.. _reads.Where(r => !r.IsBot && r.At >= since)
                .GroupBy(r => r.Source).Select(g => new BlogSourceCount(g.Key, g.Count())).OrderByDescending(s => s.Reads)]);
        }
    }
}
