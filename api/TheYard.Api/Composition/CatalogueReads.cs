namespace TheYard.Api;

/// <summary>
/// The catalogue's two public reads, the listing and the filter values: the
/// addresses the pipeline compresses (RequestPipeline, the compression region).
/// Both are anonymous, the same for every visitor, and carry no secret in
/// their bodies, and together they are most of what a visit asks the API for.
/// </summary>
public static class CatalogueReads
{
    /// <summary>The listing and the filter values, by path, without the query.</summary>
    public static readonly IReadOnlyList<string> Paths = ["/api/vehicles", "/api/facets"];

    /// <summary>True for a GET of the listing or the filter values, and for nothing under them.</summary>
    public static bool Covers(HttpRequest request) =>
        HttpMethods.IsGet(request.Method)
        && Paths.Any(path => string.Equals(request.Path.Value, path, StringComparison.OrdinalIgnoreCase));
}
