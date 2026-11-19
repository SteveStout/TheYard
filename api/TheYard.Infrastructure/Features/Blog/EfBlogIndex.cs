// What a save keeps beside a post in the relational store: the drawn body, the minutes and the rules' version on the
// post's row, its words in blog.PostWord and its keywords in blog.PostKeyword, each set replaced whole, because the
// rules' answer for the post as it is now is the authority and a half-matched set would be neither answer. And the
// synonyms the search widens through, replaced whole the same way. Its own type beside EfBlogStore, because these
// writes change when the rules change and the posts' own columns do not.
using Microsoft.EntityFrameworkCore;
using TheYard.Data;

namespace TheYard.Infrastructure.Features.Blog;

/// <summary>Keeps a post's derived parts and the synonyms.</summary>
internal static class EfBlogIndex
{
    /// <summary>
    /// Replaces a post's derived parts, and its topics when they are given, in one transaction run through the
    /// context's retry strategy, so a dropped connection retries the whole and never leaves half a set.
    /// </summary>
    internal static async Task KeepAsync(YardDbContext db, int postId, BlogDerived derived, IReadOnlyList<string>? topics, CancellationToken cancellation)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellation);
            await WriteAsync(db, postId, derived, topics, cancellation);
            await transaction.CommitAsync(cancellation);
        });
    }

    /// <summary>
    /// The writes of <see cref="KeepAsync"/> without a transaction of their own, for a caller that already holds one, so a
    /// save keeps its post and its derived parts in one commit.
    /// </summary>
    internal static async Task WriteAsync(YardDbContext db, int postId, BlogDerived derived, IReadOnlyList<string>? topics, CancellationToken cancellation)
    {
        var row = await db.Posts.FirstOrDefaultAsync(p => p.Id == postId, cancellation);
        if (row is null)
        {
            return;
        }
        row.BodyHtml = derived.BodyHtml;
        row.ReadingMinutes = derived.ReadingMinutes;
        row.DerivedWith = derived.Version;
        await db.Set<PostWordRow>().Where(w => w.PostId == postId).ExecuteDeleteAsync(cancellation);
        await db.Set<PostKeywordRow>().Where(k => k.PostId == postId).ExecuteDeleteAsync(cancellation);
        db.Set<PostWordRow>().AddRange(derived.Words.Select(w => new PostWordRow { Word = w.Word, PostId = postId, Field = w.Field, Mentions = w.Mentions }));
        db.Set<PostKeywordRow>().AddRange(derived.Keywords.Select(k => new PostKeywordRow { PostId = postId, Keyword = k.Key, Mentions = k.Mentions }));
        if (topics is not null)
        {
            await db.PostTags.Where(t => t.PostId == postId).ExecuteDeleteAsync(cancellation);
            db.PostTags.AddRange(topics.Distinct(StringComparer.Ordinal).Select(t => new PostTagRow { PostId = postId, Tag = t }));
        }
        await db.SaveChangesAsync(cancellation);
    }

    /// <summary>Replaces blog.SearchSynonym whole, in one transaction, so a search never reads half the list.</summary>
    internal static async Task KeepSynonymsAsync(YardDbContext db, IReadOnlyList<(string Word, string Term)> rows, CancellationToken cancellation)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(cancellation);
            await db.Set<SearchSynonymRow>().ExecuteDeleteAsync(cancellation);
            db.Set<SearchSynonymRow>().AddRange(rows.Distinct().Select(r => new SearchSynonymRow { Word = r.Word, Term = r.Term }));
            await db.SaveChangesAsync(cancellation);
            await transaction.CommitAsync(cancellation);
        });
    }
}
