using System.Globalization;
using System.Text.Json;

namespace JobHunt.Infrastructure.Features.NetworkLog;

/// <summary>
/// The blog's posts from the site's public feed, GET /api/blog/posts, the same list the 4 AM lane picks a post from:
/// the slug, the title and when it went out. Read once a Monday build, and only when a due row names a post. A feed
/// that does not answer gives no posts, so the drafts go out without the line rather than naming a post unchecked.
/// </summary>
public sealed class BlogFeed(IHttpClientFactory http) : IBlogPosts
{
    /// <summary>The feed's address.</summary>
    public const string Url = BlogLine.Site + "/api/blog/posts";

    /// <inheritdoc />
    public async Task<IReadOnlyList<BlogPost>> PostsAsync(CancellationToken ct)
    {
        try
        {
            using var client = http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            using var answer = await client.GetAsync(Url, ct);
            return answer.IsSuccessStatusCode ? Parse(await answer.Content.ReadAsStringAsync(ct)) : [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
        {
            return [];
        }
    }

    /// <summary>The feed's JSON as posts: every entry with a slug, a title and a published time.</summary>
    public static IReadOnlyList<BlogPost> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var posts = new List<BlogPost>();
        if (!doc.RootElement.TryGetProperty("posts", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return posts;
        }
        foreach (var post in list.EnumerateArray())
        {
            if (Text(post, "slug") is { } slug && Text(post, "title") is { } title && Text(post, "published_at") is { } at
                && DateTimeOffset.TryParse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var published))
            {
                posts.Add(new BlogPost(slug, title, published));
            }
        }
        return posts;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;
}
