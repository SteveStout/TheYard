using System.Text;
using System.Text.RegularExpressions;
using TheYard.Application.Features.Blog;

namespace TheYard.Api.Features.Blog;

/// <summary>
/// The plain index for AI tools at /llms.txt (ADR: Search engines are told about every post): the site's own words from
/// data/llms.txt, with a Blog section read from the store at request time, one line per published post at its canonical
/// address with its own summary, so a post saved by the operator is listed with no commit. The section goes before
/// Optional, which the format keeps last. A store that cannot be read leaves the section out and the rest stands.
/// </summary>
public static partial class LlmsText
{
    /// <summary>The heading the posts are listed under.</summary>
    public const string BlogHeading = "## Blog";

    private const string OptionalHeading = "\n## Optional";

    /// <summary>Maps the index's route.</summary>
    public static IEndpointRouteBuilder MapLlmsText(this IEndpointRouteBuilder app)
    {
        // #region llms-text
        app.MapGet("/llms.txt", Index)
            .WithName("GetLlmsText").WithTags("Blog")
            .WithSummary("The plain index for AI tools: the site's documents, and every published post at its canonical address")
            .Produces<string>(StatusCodes.Status200OK, "text/plain");
        // #endregion llms-text
        return app;
    }

    private static async Task<IResult> Index(HostPaths paths, CanonicalSite site, ReadFeedHandler handler, CancellationToken cancellation)
    {
        string words = await File.ReadAllTextAsync(paths.LlmsPath, cancellation);
        var (posts, _) = await handler.HandleAsync(new ReadFeedQuery(), cancellation);
        return Results.Text(Write(words, posts, site.Origin), "text/plain; charset=utf-8");
    }

    /// <summary>The index: the site's words, with the Blog section before Optional, or at the end when there is no Optional.</summary>
    /// <param name="words">data/llms.txt as it is.</param>
    /// <param name="posts">The published posts, newest first; none leaves the section out.</param>
    /// <param name="site">The canonical origin, with no trailing slash.</param>
    public static string Write(string words, IReadOnlyList<BlogPost> posts, string site)
    {
        string text = words.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (posts.Count == 0)
        {
            return text;
        }
        var section = new StringBuilder(BlogHeading).Append("\n\n");
        foreach (var post in posts.DistinctBy(post => post.Slug, StringComparer.Ordinal))
        {
            section.Append(Line(post, site)).Append('\n');
        }
        int optional = text.IndexOf(OptionalHeading, StringComparison.Ordinal);
        return optional < 0
            ? text.TrimEnd('\n') + "\n\n" + section
            : text[..(optional + 1)] + section + text[optional..];
    }

    /// <summary>One post's line: its title linked to its canonical address, then its summary on one line.</summary>
    /// <param name="post">The post.</param>
    /// <param name="site">The canonical origin, with no trailing slash.</param>
    public static string Line(BlogPost post, string site) =>
        $"- [{OneLine(post.Title).Replace('[', '(').Replace(']', ')')}]({BlogSitemap.PostAddress(site, post.Slug)}): {OneLine(post.Summary)}";

    private static string OneLine(string text) => Spaces().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
