namespace TheYard.Api;

/// <summary>
/// The catalogue's two public reads, the listing and the filter values: the
/// addresses the pipeline compresses (RequestPipeline, the compression region)
/// and the only ones the edge may keep for a few seconds (the cache-headers
/// region). Both are anonymous, the same for every visitor, and carry no secret
/// in their bodies, and together they are most of what a visit asks the API for.
/// </summary>
public static class CatalogueReads
{
    /// <summary>The listing and the filter values, by path, without the query.</summary>
    public static readonly IReadOnlyList<string> Paths = ["/api/vehicles", "/api/facets"];

    /// <summary>
    /// How long the edge may answer one of these from its own copy: fifteen
    /// seconds fresh, then fifteen more while it fetches a new one in the
    /// background. The browser is told nothing new and still asks every time
    /// (Cache-Control stays no-cache); this header is read by the edge alone.
    /// </summary>
    public const string EdgeRule = "public, max-age=15, stale-while-revalidate=15";

    /// <summary>True for a GET of the listing or the filter values, and for nothing under them.</summary>
    public static bool Covers(HttpRequest request) =>
        HttpMethods.IsGet(request.Method)
        && Paths.Any(path => string.Equals(request.Path.Value, path, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Lets the edge keep this answer for <see cref="EdgeRule"/>, when it is
    /// one every visitor would get: a 200 to a covered read from a caller with
    /// no cookie and no Authorization header, setting no cookie of its own. A
    /// signed-in request is answered fresh and never stored, so nothing a
    /// session touched can sit in a shared copy. The copy is kept per query
    /// string and per store, because a different filter or a different store
    /// is a different answer.
    /// </summary>
    public static void LetTheEdgeKeep(HttpContext context)
    {
        var request = context.Request;
        var response = context.Response;
        bool anonymous = !request.Headers.ContainsKey("Cookie") && !request.Headers.ContainsKey("Authorization");
        if (!Covers(request) || !anonymous || response.StatusCode != StatusCodes.Status200OK || response.Headers.ContainsKey("Set-Cookie"))
        {
            return;
        }

        response.Headers["Netlify-CDN-Cache-Control"] = EdgeRule;
        response.Headers["Netlify-Vary"] = "query";
        response.Headers.Append("Vary", Backends.HeaderName);
    }
}
