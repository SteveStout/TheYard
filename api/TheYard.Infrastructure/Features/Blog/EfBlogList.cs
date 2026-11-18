// The list in SQL (ADR: Every LinkedIn post is also a blog post, the addendum on the read model). The filter is a
// WHERE on the published posts, newest first is an ORDER BY on IX_Post_Published with a keyset for the page, most
// read first orders by a correlated COUNT over IX_PostView_Reads, and the total and every chip's count are COUNTs
// and GROUP BYs. A search asks blog.SearchSynonym what each typed word widens to and blog.PostWord how often each
// term is in each post, a seek on the word's start, and SearchRules turns those counts into the score. No body is
// read, and no row of either log: a card's reads are one GROUP BY over the page's posts.
using Microsoft.EntityFrameworkCore;
using TheYard.Application.Features.Blog;
using TheYard.Data;
using TheYard.Domain.Features.Blog;

namespace TheYard.Infrastructure.Features.Blog;

/// <summary>One page of the list, worked out in SQL.</summary>
internal static class EfBlogList
{
    /// <summary>The page the filter asks for, with its total, its chips and the next cursor.</summary>
    internal static async Task<BlogListPage> PageAsync(YardDbContext db, BlogFilter filter, DateTimeOffset now, CancellationToken cancellation)
    {
        var published = db.Posts.AsNoTracking().Where(p => p.IsPublished);
        var words = KeywordRules.QueryWords(filter.Q);
        var scores = words.Count == 0 ? null : await ScoresAsync(db, published, words, cancellation);
        var ids = scores?.Keys.ToList();
        var month = BlogListRules.MonthRange(filter.Month);
        var year = BlogListRules.YearRange(filter.Year);
        IQueryable<PostRow> Matching(string? except)
        {
            var rows = published;
            if (except != "topic" && filter.Topic is { } topic)
            {
                rows = rows.Where(p => db.PostTags.Any(t => t.PostId == p.Id && t.Tag == topic));
            }
            if (except != "keyword" && filter.Keyword is { } keyword)
            {
                rows = rows.Where(p => db.Set<PostKeywordRow>().Any(k => k.PostId == p.Id && k.Keyword == keyword));
            }
            if (except != "month" && month is { } m)
            {
                rows = rows.Where(p => p.PublishedAt >= m.From && p.PublishedAt < m.To);
            }
            if (except != "month" && filter.Year is not null)
            {
                var (from, to) = year ?? (DateTimeOffset.MinValue, DateTimeOffset.MinValue);
                rows = rows.Where(p => p.PublishedAt >= from && p.PublishedAt < to);
            }
            return ids is null ? rows : rows.Where(p => ids.Contains(p.Id));
        }

        char order = BlogListRules.OrderOf(filter.Sort, words.Count > 0);
        var after = BlogListRules.Read(filter.After, order);
        int total = await Matching(null).CountAsync(cancellation);
        var (page, next) = order switch
        {
            'n' => BlogListShape.Cut(await NewestAsync(Matching(null), after, cancellation)),
            'r' => BlogListShape.Cut(await MostReadAsync(db, Matching(null), after, cancellation)),
            _ => BlogListShape.Page(
                (await Matching(null).Select(p => new { p.Id, p.PublishedAt }).ToListAsync(cancellation)).Select(p => BlogListRules.Place('s', scores![p.Id].Tenths, p.PublishedAt, p.Id)),
                after),
        };
        var cards = await CardsAsync(db, [.. page.Select(p => p.Id)], now, scores, cancellation);

        var forTopics = Matching("topic").Select(p => p.Id);
        var forKeywords = Matching("keyword").Select(p => p.Id);
        var topics = await db.PostTags.Where(t => forTopics.Contains(t.PostId))
            .GroupBy(t => t.Tag).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellation);
        var publishedIds = published.Select(p => p.Id);
        var having = await db.Set<PostKeywordRow>().Where(k => publishedIds.Contains(k.PostId))
            .GroupBy(k => k.Keyword).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellation);
        var keywords = await db.Set<PostKeywordRow>().Where(k => forKeywords.Contains(k.PostId))
            .GroupBy(k => k.Keyword).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, cancellation);
        var allDates = await published.Select(p => p.PublishedAt).ToListAsync(cancellation);
        var monthDates = await Matching("month").Select(p => p.PublishedAt).ToListAsync(cancellation);
        return new BlogListPage(cards, total, BlogListShape.Topics(topics), BlogListShape.Keywords(having, keywords), BlogListShape.Months(allDates, monthDates), next);
    }

    /// <summary>
    /// One post's page: the post with its drawn body and counts, the posts either side of it in the newest-first
    /// order, each one row read off the published index, and up to three that share a topic with it, newest first.
    /// </summary>
    internal static async Task<BlogPostPage?> PostPageAsync(YardDbContext db, BlogPost post, DateTimeOffset now, CancellationToken cancellation)
    {
        var published = db.Posts.AsNoTracking().Where(p => p.IsPublished);
        var row = await published.Where(p => p.Id == post.Id).Select(p => new { p.BodyHtml, p.PublishedAt }).FirstAsync(cancellation);
        var at = row.PublishedAt;
        int? older = await published.Where(p => p.PublishedAt < at || (p.PublishedAt == at && p.Id < post.Id))
            .OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.Id).Select(p => (int?)p.Id).FirstOrDefaultAsync(cancellation);
        int? newer = await published.Where(p => p.PublishedAt > at || (p.PublishedAt == at && p.Id > post.Id))
            .OrderBy(p => p.PublishedAt).ThenBy(p => p.Id).Select(p => (int?)p.Id).FirstOrDefaultAsync(cancellation);
        var topics = BlogListShape.TopicsOf(post.Tags);
        string[] shared = [.. topics];
        var related = await published.Where(p => p.Id != post.Id && db.PostTags.Any(t => t.PostId == p.Id && shared.Contains(t.Tag)))
            .OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.Id).Take(BlogListRules.Related).Select(p => p.Id).ToListAsync(cancellation);
        var cards = (await CardsAsync(db, [post.Id, .. related, .. new[] { older, newer }.OfType<int>()], now, null, cancellation)).ToDictionary(c => c.Id);
        var self = cards[post.Id];
        return new BlogPostPage(post, row.BodyHtml ?? "", self.ReadingMinutes, topics, self.Keywords, self.Reads, self.Reads7d,
            older is { } o ? cards[o] : null, newer is { } n ? cards[n] : null, [.. related.Select(id => cards[id])]);
    }

    /// <summary>Newest first after the cursor, a page and one more, on the published index.</summary>
    private static async Task<IReadOnlyList<BlogCursor>> NewestAsync(IQueryable<PostRow> rows, BlogCursor? after, CancellationToken cancellation)
    {
        if (after is not null)
        {
            var at = new DateTimeOffset(after.Ticks, TimeSpan.Zero);
            rows = rows.Where(p => p.PublishedAt < at || (p.PublishedAt == at && p.Id < after.Id));
        }
        var places = await rows.OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.Id).Take(BlogListRules.PageSize + 1)
            .Select(p => new { p.Id, p.PublishedAt }).ToListAsync(cancellation);
        return [.. places.Select(p => BlogListRules.Place('n', 0, p.PublishedAt, p.Id))];
    }

    /// <summary>Most read first after the cursor, a page and one more: each post's reads a correlated COUNT, people only.</summary>
    private static async Task<IReadOnlyList<BlogCursor>> MostReadAsync(YardDbContext db, IQueryable<PostRow> rows, BlogCursor? after, CancellationToken cancellation)
    {
        var read = rows.Select(p => new { p.Id, p.PublishedAt, Reads = db.PostViews.Count(v => v.PostId == p.Id && !v.IsBot) });
        if (after is not null)
        {
            var at = new DateTimeOffset(after.Ticks, TimeSpan.Zero);
            read = read.Where(p => p.Reads < after.Rank || (p.Reads == after.Rank && (p.PublishedAt < at || (p.PublishedAt == at && p.Id < after.Id))));
        }
        var places = await read.OrderByDescending(p => p.Reads).ThenByDescending(p => p.PublishedAt).ThenByDescending(p => p.Id)
            .Take(BlogListRules.PageSize + 1).ToListAsync(cancellation);
        return [.. places.Select(p => BlogListRules.Place('r', p.Reads, p.PublishedAt, p.Id))];
    }

    /// <summary>
    /// Each published post's score for the typed words: the widening read from blog.SearchSynonym, every widened term's
    /// counts read from blog.PostWord by a seek on the word's start, the posts' topics and keywords beside them.
    /// </summary>
    private static async Task<Dictionary<int, SearchScore>> ScoresAsync(YardDbContext db, IQueryable<PostRow> published, IReadOnlyList<string> words, CancellationToken cancellation)
    {
        var synonyms = (await db.Set<SearchSynonymRow>().AsNoTracking().Where(s => words.Contains(s.Word)).ToListAsync(cancellation)).ToLookup(s => s.Word, s => s.Term, StringComparer.Ordinal);
        IReadOnlyList<string> Expand(string word) => synonyms.Contains(word) ? [.. synonyms[word]] : [word];
        bool local = db.Database.IsSqlite();
        // One statement for every widened term, a UNION ALL of seeks; a row two terms both start (test, tests) comes
        // back twice and is kept once, so each term's count reads it once.
        IQueryable<PostWordRow>? seeks = null;
        foreach (string term in words.SelectMany(Expand).Distinct(StringComparer.Ordinal))
        {
            var seek = Starting(db, term, local);
            seeks = seeks is null ? seek : seeks.Concat(seek);
        }
        var found = (await seeks!.AsNoTracking().ToListAsync(cancellation)).DistinctBy(w => (w.Word, w.PostId, w.Field)).ToList();
        var postIds = await published.Select(p => p.Id).ToListAsync(cancellation);
        var tags = (await db.PostTags.AsNoTracking().Where(t => postIds.Contains(t.PostId)).ToListAsync(cancellation)).ToLookup(t => t.PostId, t => t.Tag);
        var keys = (await db.Set<PostKeywordRow>().AsNoTracking().Where(k => postIds.Contains(k.PostId)).ToListAsync(cancellation)).ToLookup(k => k.PostId, k => k.Keyword);
        var byPost = found.ToLookup(w => w.PostId);
        var scores = new Dictionary<int, SearchScore>();
        foreach (int id in postIds)
        {
            var mine = byPost[id].ToList();
            int Mentions(string term, byte field) => BlogText.Mentions(mine.Where(w => w.Field == field).Select(w => (w.Word, w.Mentions)), term);
            if (SearchRules.Score(words, BlogListShape.TopicsOf(tags[id]), BlogListShape.KeywordsOf(keys[id]), Mentions, Expand) is { } score)
            {
                scores[id] = score;
            }
        }
        return scores;
    }

    /// <summary>
    /// The words that start with a term: LIKE 'term%' on SQL Server, a seek on the key; on SQLite a range from the term
    /// to the term with its last character raised, because SQLite's LIKE ignores case and so reads no index here.
    /// </summary>
    private static IQueryable<PostWordRow> Starting(YardDbContext db, string term, bool local)
    {
        if (!local)
        {
            return db.Set<PostWordRow>().Where(w => w.Word.StartsWith(term));
        }
        string upper = term[..^1] + (char)(term[^1] + 1);
        return db.Set<PostWordRow>().Where(w => string.Compare(w.Word, term) >= 0 && string.Compare(w.Word, upper) < 0);
    }

    /// <summary>The page's cards: the posts' own columns, their slides, topics and keywords, and their reads in one GROUP BY.</summary>
    private static async Task<IReadOnlyList<BlogCard>> CardsAsync(YardDbContext db, IReadOnlyList<int> asked, DateTimeOffset now, Dictionary<int, SearchScore>? scores, CancellationToken cancellation)
    {
        var ids = asked.Distinct().ToList();
        var posts = await db.Posts.AsNoTracking().Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Slug, p.Title, p.Summary, p.PublishedAt, p.ReadingMinutes }).ToDictionaryAsync(p => p.Id, cancellation);
        var images = (await db.PostImages.AsNoTracking().Where(i => ids.Contains(i.PostId)).ToListAsync(cancellation)).ToLookup(i => i.PostId);
        var tags = (await db.PostTags.AsNoTracking().Where(t => ids.Contains(t.PostId)).ToListAsync(cancellation)).ToLookup(t => t.PostId, t => t.Tag);
        var keys = (await db.Set<PostKeywordRow>().AsNoTracking().Where(k => ids.Contains(k.PostId)).ToListAsync(cancellation)).ToLookup(k => k.PostId, k => k.Keyword);
        var weekAgo = now.AddDays(-7);
        var reads = await db.PostViews.Where(v => ids.Contains(v.PostId) && !v.IsBot).GroupBy(v => v.PostId)
            .Select(g => new { g.Key, Reads = g.Count(), Week = g.Count(v => v.ViewedAt > weekAgo) }).ToDictionaryAsync(g => g.Key, cancellation);
        return [.. ids.Select(id =>
        {
            var p = posts[id];
            var cover = images[id].OrderBy(i => i.Position).FirstOrDefault();
            var counted = reads.GetValueOrDefault(id);
            return new BlogCard(p.Id, p.Slug, p.Title, p.Summary, p.PublishedAt,
                cover is null ? null : new BlogImage(cover.Position, cover.Url, cover.AltText, cover.Width, cover.Height),
                images[id].Count(), p.ReadingMinutes ?? 1, BlogListShape.TopicsOf(tags[id]), BlogListShape.KeywordsOf(keys[id]),
                counted?.Reads ?? 0, counted?.Week ?? 0, scores?.GetValueOrDefault(id)?.Why ?? []);
        })];
    }
}
