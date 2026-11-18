// The blog kept in this process's memory: for a process with no relational store, and for the tests. It answers the
// list the way the relational store does, from the same rules and the same shaping (BlogListShape), so the shared
// test table holds the two to one answer; only the counting is LINQ here and SQL there.
using TheYard.Data;
using TheYard.Domain.Features.Blog;

namespace TheYard.Application.Features.Blog;

/// <summary>
/// The blog kept in this process's memory. Bounded like every log the site keeps in memory, so a flood of reads cannot
/// grow it without end.
/// </summary>
/// <param name="reason">What the Admin tab says about where the blog is kept.</param>
public sealed class MemoryBlogStore(string reason) : IBlogStore
{
    /// <summary>The most reads and events each kept before the oldest go.</summary>
    public const int MaxRows = 20_000;

    private readonly Lock _gate = new();
    private readonly List<BlogPost> _posts = [];
    private readonly Dictionary<int, BlogDerived> _derived = [];
    private readonly Queue<BlogRead> _reads = new();
    private readonly Queue<BlogEvent> _events = new();
    private IReadOnlyList<(string Word, string Term)> _synonyms = [];

    /// <summary>Always available, and says it is kept in memory.</summary>
    public Task<BlogAvailability> AvailabilityAsync(CancellationToken cancellation) => Task.FromResult(new BlogAvailability(true, reason));

    /// <summary>Every post, newest first, for the feed.</summary>
    public Task<IReadOnlyList<BlogPost>> PostsAsync(CancellationToken cancellation)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<BlogPost>>([.. _posts.OrderByDescending(p => p.PublishedAt)]);
        }
    }

    /// <summary>One post by its slug, or null.</summary>
    public Task<BlogPost?> PostAsync(string slug, CancellationToken cancellation)
    {
        lock (_gate)
        {
            return Task.FromResult(_posts.FirstOrDefault(p => p.Slug == slug));
        }
    }

    /// <summary>Adds the post or replaces the one with its slug, keeping what was worked out from it beside it.</summary>
    public Task<int> SaveAsync(BlogPost post, BlogDerived derived, DateTimeOffset at, CancellationToken cancellation)
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
            _derived[id] = derived;
            return Task.FromResult(id);
        }
    }

    /// <summary>The posts kept under another version of the rules, or none, each with whether it never was derived.</summary>
    public Task<IReadOnlyList<(BlogPost Post, bool Never)>> StaleAsync(int version, CancellationToken cancellation)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<(BlogPost, bool)>>([.. _posts
                .Where(p => !_derived.TryGetValue(p.Id, out var d) || d.Version != version)
                .Select(p => (p, !_derived.ContainsKey(p.Id)))]);
        }
    }

    /// <summary>One post's page: its drawn body, its counts, the posts either side of it newest first, and up to three that share a topic.</summary>
    public Task<BlogPostPage?> PostPageAsync(string slug, DateTimeOffset now, CancellationToken cancellation)
    {
        lock (_gate)
        {
            var newestFirst = _posts.OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.Id).ToList();
            int at = newestFirst.FindIndex(p => p.Slug == slug);
            if (at < 0)
            {
                return Task.FromResult<BlogPostPage?>(null);
            }
            var post = newestFirst[at];
            var topics = BlogListShape.TopicsOf(post.Tags);
            var related = newestFirst.Where(p => p.Id != post.Id && BlogListShape.TopicsOf(p.Tags).Intersect(topics).Any()).Take(BlogListRules.Related);
            var derived = _derived.GetValueOrDefault(post.Id);
            var card = Card(post, now, []);
            return Task.FromResult<BlogPostPage?>(new BlogPostPage(post, derived?.BodyHtml ?? "", card.ReadingMinutes, topics, card.Keywords, card.Reads, card.Reads7d,
                at + 1 < newestFirst.Count ? Card(newestFirst[at + 1], now, []) : null, at > 0 ? Card(newestFirst[at - 1], now, []) : null,
                [.. related.Select(p => Card(p, now, []))]));
        }
    }

    /// <summary>Keeps a post's derived parts, and its topics when they are given.</summary>
    public Task KeepDerivedAsync(int postId, BlogDerived derived, IReadOnlyList<string>? topics, CancellationToken cancellation)
    {
        lock (_gate)
        {
            int index = _posts.FindIndex(p => p.Id == postId);
            if (index >= 0)
            {
                _derived[postId] = derived;
                if (topics is not null)
                {
                    _posts[index] = _posts[index] with { Tags = topics };
                }
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>Replaces the synonyms whole.</summary>
    public Task KeepSynonymsAsync(IReadOnlyList<(string Word, string Term)> rows, CancellationToken cancellation)
    {
        lock (_gate)
        {
            _synonyms = [.. rows];
        }
        return Task.CompletedTask;
    }

    /// <summary>Keeps a read, dropping the oldest past the bound.</summary>
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

    /// <summary>Keeps an event, dropping the oldest past the bound.</summary>
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

    // #region memory-list
    /// <summary>One page of the list, counted in LINQ the way the relational store counts in SQL.</summary>
    public Task<BlogListPage> ListAsync(BlogFilter filter, DateTimeOffset now, CancellationToken cancellation)
    {
        lock (_gate)
        {
            var words = KeywordRules.QueryWords(filter.Q);
            var synonyms = _synonyms.ToLookup(s => s.Word, s => s.Term, StringComparer.Ordinal);
            IReadOnlyList<string> Expand(string word) => synonyms.Contains(word) ? [.. synonyms[word]] : [word];
            var scores = words.Count == 0 ? null : _posts
                .Select(p => (p.Id, Score: SearchRules.Score(words, BlogListShape.TopicsOf(p.Tags), KeywordsOf(p.Id), Mentions(p.Id), Expand)))
                .Where(s => s.Score is not null).ToDictionary(s => s.Id, s => s.Score!);
            var month = BlogListRules.MonthRange(filter.Month);
            var year = BlogListRules.YearRange(filter.Year);
            bool Matches(BlogPost p, string? except) =>
                (except == "topic" || filter.Topic is null || p.Tags.Contains(filter.Topic, StringComparer.Ordinal))
                && (except == "keyword" || filter.Keyword is null || KeywordsOf(p.Id).Contains(filter.Keyword, StringComparer.Ordinal))
                && (except == "month" || ((month is null || (p.PublishedAt >= month.Value.From && p.PublishedAt < month.Value.To))
                    && (filter.Year is null || (year is not null && p.PublishedAt >= year.Value.From && p.PublishedAt < year.Value.To))))
                && (scores is null || scores.ContainsKey(p.Id));
            var matching = _posts.Where(p => Matches(p, null)).ToList();
            char order = BlogListRules.OrderOf(filter.Sort, words.Count > 0);
            var people = _reads.Where(r => !r.IsBot).ToLookup(r => r.PostId);
            var places = matching.Select(p => BlogListRules.Place(order, order == 'r' ? people[p.Id].Count() : order == 's' ? scores![p.Id].Tenths : 0, p.PublishedAt, p.Id));
            var (page, next) = BlogListShape.Page(places, BlogListRules.Read(filter.After, order));
            var cards = page.Select(place => Card(_posts.Single(post => post.Id == place.Id), now, scores?.GetValueOrDefault(place.Id)?.Why ?? [])).ToList();
            Dictionary<string, int> Count(IEnumerable<string> keys) => keys.GroupBy(k => k, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            return Task.FromResult(new BlogListPage(cards, matching.Count,
                BlogListShape.Topics(Count(_posts.Where(p => Matches(p, "topic")).SelectMany(p => p.Tags))),
                BlogListShape.Keywords(Count(_posts.SelectMany(p => KeywordsOf(p.Id))), Count(_posts.Where(p => Matches(p, "keyword")).SelectMany(p => KeywordsOf(p.Id)))),
                BlogListShape.Months(_posts.Select(p => p.PublishedAt), _posts.Where(p => Matches(p, "month")).Select(p => p.PublishedAt)),
                next));
        }
    }

    /// <summary>A post as a card: its cover, its slides, its minutes, its topics and keywords, and its reads, people only.</summary>
    private BlogCard Card(BlogPost p, DateTimeOffset now, IReadOnlyList<string> why)
    {
        var reads = _reads.Where(r => r.PostId == p.Id && !r.IsBot).ToList();
        return new BlogCard(p.Id, p.Slug, p.Title, p.Summary, p.PublishedAt, p.Images.OrderBy(i => i.Position).FirstOrDefault(), p.Images.Count,
            _derived.GetValueOrDefault(p.Id)?.ReadingMinutes ?? 1, BlogListShape.TopicsOf(p.Tags), KeywordsOf(p.Id), reads.Count, reads.Count(r => r.At > now.AddDays(-7)), why);
    }

    private IReadOnlyList<string> KeywordsOf(int id) => BlogListShape.KeywordsOf(_derived.GetValueOrDefault(id)?.Keywords.Select(k => k.Key) ?? []);

    private Func<string, byte, int> Mentions(int id)
    {
        var words = _derived.GetValueOrDefault(id)?.Words ?? [];
        return (term, field) => BlogText.Mentions(words.Where(w => w.Field == field).Select(w => (w.Word, w.Mentions)), term);
    }
    // #endregion memory-list

    /// <summary>Each post's numbers over the window, through BlogNumbers.</summary>
    public Task<IReadOnlyList<BlogStat>> StatsAsync(DateTimeOffset now, DateTimeOffset since, CancellationToken cancellation)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<BlogStat>>([.. _posts.OrderByDescending(p => p.PublishedAt)
                .Select(p => BlogNumbers.Of(p, _reads.Where(r => r.PostId == p.Id && r.At >= since), _events.Where(e => e.PostId == p.Id && e.At >= since), now))]);
        }
    }

    /// <summary>Reads by source since an instant, bots left out.</summary>
    public Task<IReadOnlyList<BlogSourceCount>> SourcesAsync(DateTimeOffset since, CancellationToken cancellation)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<BlogSourceCount>>([.. _reads.Where(r => !r.IsBot && r.At >= since)
                .GroupBy(r => r.Source).Select(g => new BlogSourceCount(g.Key, g.Count())).OrderByDescending(s => s.Reads)]);
        }
    }
}
