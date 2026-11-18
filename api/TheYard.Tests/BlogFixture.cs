using System.Text.Json;
using TheYard.Data;

namespace TheYard.Tests;

/// <summary>
/// The 15 posts on the live blog as a test reads them (Fixtures/BlogPosts.json): each post as the store keeps it,
/// with the topics the mockup gives it and the keywords the keyword rules must find in it.
/// </summary>
internal static class BlogFixture
{
    /// <summary>One fixture post: the post, its topics, the keywords expected of it, and the mockup's own copy of its text and keywords.</summary>
    internal sealed record Entry(BlogPost Post, IReadOnlyList<string> Topics, IReadOnlyList<string> Keywords, int MockupMinutes, string MockupText, IReadOnlyList<string> MockupKeywords);

    private static readonly Lazy<IReadOnlyList<Entry>> Loaded = new(Load);

    /// <summary>The 15 posts, newest first, each with its id from 15 down to 1.</summary>
    internal static IReadOnlyList<Entry> Posts => Loaded.Value;

    private static IReadOnlyList<Entry> Load()
    {
        using var file = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo.Root(), "api", "TheYard.Tests", "Fixtures", "BlogPosts.json")));
        var posts = file.RootElement.GetProperty("posts").EnumerateArray().ToList();
        return [.. posts.Select((p, index) =>
        {
            var topics = p.GetProperty("topics").EnumerateArray().Select(t => t.GetString()!).ToList();
            var images = p.GetProperty("images").EnumerateArray().Select(i => new BlogImage(
                i.GetProperty("position").GetInt32(), i.GetProperty("url").GetString()!, i.GetProperty("alt_text").GetString()!,
                i.GetProperty("width").GetInt32(), i.GetProperty("height").GetInt32())).ToList();
            var post = new BlogPost(posts.Count - index, p.GetProperty("slug").GetString()!, p.GetProperty("title").GetString()!,
                p.GetProperty("summary").GetString()!, p.GetProperty("body").GetString()!, p.GetProperty("published_at").GetDateTimeOffset(),
                p.GetProperty("linkedin_url").GetString()!, p.GetProperty("code_url").GetString(), images, topics);
            return new Entry(post, topics, [.. p.GetProperty("keywords").EnumerateArray().Select(k => k.GetString()!)], p.GetProperty("mockup_minutes").GetInt32(),
                p.GetProperty("mockup_text").GetString()!, [.. p.GetProperty("mockup_keywords").EnumerateArray().Select(k => k.GetString()!)]);
        })];
    }
}
