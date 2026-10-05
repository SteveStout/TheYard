// The list of every address the page sweep checks, built from the documentation catalogue
// plus the fixed addresses that are not documents. It is its own file because the list is
// what changes when the site grows, and the sweep that walks it does not.
namespace TheYard.Api;

// #region served-addresses
/// <summary>
/// Every address this container answers a page, a document or a drawing at,
/// derived from the catalogue rather than written out beside it (ADR: Every
/// page, checked at every roll). A list would be a second place to remember,
/// and the first document somebody adds without remembering is the one that
/// goes out broken, which is exactly the shape the README's two raw-markdown
/// links had: every link answered, and nothing asked what it answered with.
/// The fixed rows below are the addresses that are not documents: the app
/// itself, the API's own front pages, and the files the build copies to the
/// root of the domain.
///
/// <para>The ones that come out of the frontend build are checked only when
/// the frontend is in this container. In the image it always is; on a
/// developer's machine and under the test host the API runs on its own with
/// the dev server in front of it, and calling addresses this container was
/// never given down would be a false reading rather than a strict one.</para>
/// </summary>
public static class ServedAddresses
{
    /// <summary>The addresses the frontend build puts in this container's web root.</summary>
    public static readonly IReadOnlyList<ServedAddress> FromTheBuild =
    [
        new("/", "The app", "page"),
        new("/robots.txt", "robots.txt", "file"),
        new("/sitemap.xml", "sitemap.xml", "file"),
        new("/og.png", "The preview card", "file"),
        new("/about-lead.svg", "The drawing beside How I lead", "file"),
    ];

    /// <summary>
    /// Every address to check, in sweep order: the frontend's own files when
    /// this container serves the frontend, the fixed API addresses, then every
    /// document and every drawing in the catalogue, each sorted by key.
    /// </summary>
    public static IReadOnlyList<ServedAddress> All(bool frontendServed)
    {
        var addresses = new List<ServedAddress>
        {
            new("/api/reference", "API reference", "page"),
            new("/about", "About Steven Stout", "page"),
            new("/api/version", "The build this container was made from", "api"),
            new("/api/health", "Health, both stores", "api"),
            new("/api/vehicles?limit=1", "The listing", "api"),
            new("/api/facets", "The filter values", "api"),
            new("/api/stores", "The stores this container runs", "api"),
            // The Admin tab's own readings, because an endpoint that throws is
            // a page that is down, and a sweep that does not ask for them
            // cannot notice when one of them answers 500 (ADR: What the
            // machines are doing, the addendum on the cast).
            new("/api/admin/machines", "What the machines are doing", "api"),
            new("/api/admin/metrics", "Timing", "api"),
            // A card's window is a query in the store behind a public address, which is two ways to be down.
            new("/api/admin/kept?card=errors&window=24h", "Recent errors, the last 24 hours as kept", "api"),
            new("/api/admin/pages", "This check itself", "api"),
            // What Azure charges is read from the kept days, so a store that has gone away is a card that is down.
            new("/api/admin/costs?window=30d", "What Azure charges, the last 30 days", "api"),
            new("/api/docs/resume", "Steven's resume (PDF)", "file"),
            new("/api/docs/bicep", "Infrastructure (Bicep)", "document"),
        };

        if (frontendServed)
        {
            addresses.InsertRange(0, FromTheBuild);
        }

        addresses.AddRange(DocumentationCatalog.Files
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new ServedAddress($"/api/docs/{entry.Key}", entry.Value, "document")));

        addresses.AddRange(DocumentationCatalog.Diagrams
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new ServedAddress($"/api/docs/diagrams/{entry.Key}", entry.Value.Title, "drawing")));

        return addresses;
    }
}

/// <summary>One address the sweep checks: where it is, what a reader would call it, and which kind of thing it is.</summary>
/// <param name="Address">The path the sweep requests.</param>
/// <param name="What">What a reader would call the address.</param>
/// <param name="Kind">Which kind of thing it is, such as page, api, document or drawing.</param>
public sealed record ServedAddress(string Address, string What, string Kind);
// #endregion served-addresses
