namespace TheYard.Api;

/// <summary>
/// What the edge may keep for a day because it can only change on a deploy, and the deploy
/// purges it (ADR-015, the addendum of 20 October): the share card's picture, every document and
/// every diagram. The browser's rule is set beside it in the cache-headers region of
/// RequestPipeline; the hashed bundle files keep their year there and are not repeated here.
/// The resume and About Steven stay out, because the Site activity card counts a visitor by the
/// request that reaches the origin, and an edge copy would hide the two steps it watches
/// (ActivityWho.Steps).
/// </summary>
public static class EdgeKeeps
{
    /// <summary>Files outside /assets that keep their name across deploys and change only with one.</summary>
    public static readonly IReadOnlyList<string> StableFiles = ["/og.png"];

    /// <summary>The browser may keep a stable file for a day, the photo set's rule.</summary>
    public const string BrowserRule = "public, max-age=86400";

    /// <summary>
    /// The edge may keep an answer for a day. No "durable": Netlify reads that word on its own
    /// functions' answers only, and these come through its proxy from the origin.
    /// </summary>
    public const string EdgeRule = "public, max-age=86400";

    /// <summary>The documents and the diagrams, every address under it.</summary>
    public const string DocumentsPrefix = "/api/docs";

    /// <summary>The document addresses the activity card counts, which the edge never keeps.</summary>
    public static readonly IReadOnlyList<string> Counted = ["/api/docs/resume", "/api/docs/author"];

    /// <summary>The browser's rule for a stable file, or null when the address is not one.</summary>
    public static string? BrowserRuleFor(HttpContext context) =>
        context.Response.StatusCode == StatusCodes.Status200OK && StableFiles.Contains(context.Request.Path.Value ?? "", StringComparer.OrdinalIgnoreCase)
            ? BrowserRule
            : null;

    /// <summary>True for an address whose answer changes only on a deploy.</summary>
    public static bool Keeps(PathString path) =>
        StableFiles.Contains(path.Value ?? "", StringComparer.OrdinalIgnoreCase)
        || (path.StartsWithSegments(DocumentsPrefix) && !Counted.Any(c => path.Equals(c, StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Lets the edge keep this answer for <see cref="EdgeRule"/>, on the catalogue reads' terms: a
    /// 200 to a GET from a caller with no cookie and no Authorization header, setting no cookie of
    /// its own, so a session renewed on the way out can never sit in a shared copy.
    /// </summary>
    public static void LetTheEdgeKeep(HttpContext context)
    {
        var request = context.Request;
        var response = context.Response;
        bool anonymous = !request.Headers.ContainsKey("Cookie") && !request.Headers.ContainsKey("Authorization");
        if (!HttpMethods.IsGet(request.Method) || !Keeps(request.Path) || !anonymous
            || response.StatusCode != StatusCodes.Status200OK || response.Headers.ContainsKey("Set-Cookie"))
        {
            return;
        }

        response.Headers["Netlify-CDN-Cache-Control"] = EdgeRule;
    }
}
