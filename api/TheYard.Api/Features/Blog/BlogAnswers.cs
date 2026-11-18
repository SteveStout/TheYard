// The host's copy of the blog's public answers (ADR: Every LinkedIn post is also a blog post, the addendum on the
// read model): each list page and each post, as the bytes that went out and their ETag, kept a minute in the
// process's memory and dropped whole on every save, so a burst of readers costs the database one read a minute and a
// saved post is on the page at once. A browser keeps the same answer a minute too and checks its ETag after, so a
// repeat visit is a 304 with no body. Answers are the same bytes for every reader: none carries a cookie, a session or
// anything about who asked, which is what lets one copy serve them all.
using System.Security.Cryptography;

namespace TheYard.Api.Features.Blog;

/// <summary>One kept answer: its body and its ETag.</summary>
/// <param name="Json">The bytes that went out.</param>
/// <param name="ETag">The quoted tag a repeat request sends back in If-None-Match.</param>
public sealed record BlogAnswer(byte[] Json, string ETag);

/// <summary>The kept answers, bounded by count and by bytes, a minute each, and the rule a browser is given for them.</summary>
public sealed class BlogAnswers
{
    /// <summary>What a browser and the edge may do with a list or a post: keep it a minute, and serve it ten more while it checks again.</summary>
    public const string CacheRule = "public, max-age=60, stale-while-revalidate=600";

    /// <summary>The most answers kept: every filter a reader is likely to ask in a minute, and a stop on a flood of made-up ones.</summary>
    public const int MaxAnswers = 512;

    private readonly BoundedCache<string, BlogAnswer> _kept = new(MaxAnswers, 8L * 1024 * 1024, TimeSpan.FromSeconds(60), answer => answer.Json.LongLength);

    /// <summary>The answer kept under a key, or the one the factory reads, kept for next time.</summary>
    /// <param name="key">The answer's key: which route and the filter it was asked with.</param>
    /// <param name="read">Reads the answer's bytes, or null when there is nothing to keep (an error, a post not found).</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<BlogAnswer?> GetAsync(string key, Func<CancellationToken, Task<byte[]?>> read, CancellationToken cancellation)
    {
        if (_kept.TryGet(key, out var kept))
        {
            return kept;
        }
        byte[]? json = await read(cancellation);
        if (json is null)
        {
            return null;
        }
        var answer = new BlogAnswer(json, $"\"b-{Convert.ToHexStringLower(SHA256.HashData(json))[..16]}\"");
        _kept.Set(key, answer);
        return answer;
    }

    /// <summary>Drops every kept answer: a post was saved, a picture put, or the posts derived again.</summary>
    public void Clear() => _kept.Clear();

    /// <summary>Writes an answer with its rule and its ETag, or a 304 with no body when the request already holds it.</summary>
    /// <param name="http">The request and its response.</param>
    /// <param name="answer">The answer.</param>
    public static IResult Send(HttpContext http, BlogAnswer answer)
    {
        http.Response.Headers.CacheControl = CacheRule;
        http.Response.Headers.ETag = answer.ETag;
        return http.Request.Headers.IfNoneMatch.Any(tag => tag is not null && tag.Split(',').Any(one => one.Trim() == answer.ETag || one.Trim() == "*"))
            ? Results.StatusCode(StatusCodes.Status304NotModified)
            : Results.Bytes(answer.Json, "application/json; charset=utf-8");
    }
}
