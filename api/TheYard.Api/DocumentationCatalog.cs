using System.Globalization;
using System.Text.RegularExpressions;

namespace TheYard.Api;

/// <summary>
/// Every document the site serves, by the slug the sidebar asks for (ADR-017).
/// src/library/records.ts and src/library/pages.ts carry the same slugs with titles, so
/// a new record is one line here and one line there, and DocumentationCatalogTests holds
/// the two lists to each other. A slug missing from this table is a 404 at
/// /api/docs/{slug}, never a file read.
/// </summary>
public static class DocumentationCatalog
{
    // #region docs-catalog
    /// <summary>Slug to file, relative to the repo root. Every one goes through the live-sample expander (ADR-014).</summary>
    public static readonly IReadOnlyDictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["readme"] = "README.md",
        ["start-here"] = "docs/app-architecture/START-HERE.md",
        ["dataflow"] = "docs/app-architecture/DATAFLOW.md",
        ["sql-vs-cosmos"] = "docs/sql-vs-cosmos/SQL-VS-COSMOS.md",
        ["projects"] = "docs/app-architecture/PROJECTS.md",
        ["hosting"] = "docs/hosting/HOSTING.md",
        ["adr-origin"] = "docs/decisions/ADR-001-front-door-origin.md",
        ["adr-docker"] = "docs/decisions/ADR-002-docker-packaging.md",
        ["adr-naming"] = "docs/decisions/ADR-003-azure-naming.md",
        ["adr-pivots"] = "docs/decisions/ADR-004-deployment-pivots.md",
        ["adr-edge-economics"] = "docs/decisions/ADR-007-edge-economics.md",
        ["adr-linux"] = "docs/decisions/ADR-008-linux-containers.md",
        ["cicd"] = "docs/ci-cd/CICD.md",
        ["adr-pipeline"] = "docs/decisions/ADR-009-deploy-pipeline.md",
        ["practices"] = "docs/best-practices/BEST-PRACTICES.md",
        ["sealed"] = "docs/best-practices/SEALED.md",
        ["onion"] = "docs/best-practices/ONION.md",
        ["adr-versioning"] = "docs/decisions/ADR-005-version-footer.md",
        ["adr-docs"] = "docs/decisions/ADR-006-docs-and-testing.md",
        ["adr-observability"] = "docs/decisions/ADR-010-observability.md",
        ["adr-phone"] = "docs/decisions/ADR-011-phone-header.md",
        // #region docs-changelog
        // The changelog and its record (ADR-012): one file, one sentence per version.
        ["changelog"] = "docs/CHANGELOG.md",
        ["adr-changelog"] = "docs/decisions/ADR-012-changelog.md",
        // #endregion docs-changelog
        ["adr-sidebar"] = "docs/decisions/ADR-013-sidebar.md",
        ["adr-live-samples"] = "docs/decisions/ADR-014-live-samples.md",
        ["adr-caching"] = "docs/decisions/ADR-015-cache-headers.md",
        ["adr-palette"] = "docs/decisions/ADR-016-palette.md",
        ["adr-review"] = "docs/decisions/ADR-017-staff-review.md",
        ["adr-program"] = "docs/decisions/ADR-018-program-cs-explained.md",
        ["adr-react"] = "docs/decisions/ADR-019-react-configuration-explained.md",
        ["adr-diagrams"] = "docs/decisions/ADR-020-diagram-pages.md",
        ["adr-tests"] = "docs/decisions/ADR-021-tests-explained.md",
        ["adr-grouping"] = "docs/decisions/ADR-022-app-architecture-group.md",
        ["adr-errors"] = "docs/decisions/ADR-023-error-handling.md",
        ["adr-telemetry"] = "docs/decisions/ADR-024-telemetry.md",
        ["adr-search"] = "docs/decisions/ADR-025-search-index.md",
        ["adr-keyboard"] = "docs/decisions/ADR-026-keyboard.md",
        ["adr-bidders"] = "docs/decisions/ADR-027-competing-bidders.md",
        ["adr-style"] = "docs/decisions/ADR-028-style-enforced.md",
        ["adr-records"] = "docs/decisions/ADR-029-records-index.md",
        ["adr-exceptions"] = "docs/decisions/ADR-030-exception-handler.md",
        ["adr-version-source"] = "docs/decisions/ADR-031-version-from-changelog.md",
        ["adr-method"] = "docs/decisions/ADR-032-ai-development.md",
        ["adr-store"] = "docs/decisions/ADR-033-relational-store.md",
        ["adr-ef"] = "docs/decisions/ADR-034-entity-framework-explained.md",
        ["adr-a11y-check"] = "docs/decisions/ADR-035-accessibility-check.md",
        ["adr-photos"] = "docs/decisions/ADR-036-responsive-photos.md",
        ["adr-accounts"] = "docs/decisions/ADR-037-accounts.md",
        ["adr-identity"] = "docs/decisions/ADR-038-identity-explained.md",
        ["adr-sql-server"] = "docs/decisions/ADR-039-sql-server-backend.md",
        ["adr-data-first"] = "docs/decisions/ADR-040-database-source-control.md",
        ["adr-providers"] = "docs/decisions/ADR-041-two-providers-explained.md",
        ["adr-exemptions"] = "docs/decisions/ADR-042-exemptions-that-hide.md",
        ["adr-sql-visible"] = "docs/decisions/ADR-043-what-the-database-is-doing.md",
        ["adr-interceptors"] = "docs/decisions/ADR-044-interceptors-explained.md",
        ["adr-self-review"] = "docs/decisions/ADR-045-reviewing-my-own-work.md",
        ["adr-the-name"] = "docs/decisions/ADR-046-the-name.md",
        ["adr-second-manifest"] = "docs/decisions/ADR-047-the-second-manifest.md",
        ["adr-reset"] = "docs/decisions/ADR-048-reset-is-one-persons.md",
        ["adr-room-account"] = "docs/decisions/ADR-049-the-room-needs-an-account.md",
        ["adr-lockout"] = "docs/decisions/ADR-050-a-password-guess-should-cost-something.md",
        ["adr-coverage"] = "docs/decisions/ADR-051-counting-what-the-tests-cover.md",
        ["adr-where-gates-live"] = "docs/decisions/ADR-052-where-a-gate-lives.md",
        ["adr-public-face"] = "docs/decisions/ADR-053-the-public-face.md",
        ["adr-one-write"] = "docs/decisions/ADR-054-the-one-write-a-stranger-can-make.md",
        ["adr-broken-windows"] = "docs/decisions/ADR-055-broken-windows.md",
        ["adr-stale-listing"] = "docs/decisions/ADR-056-the-listing-that-went-stale.md",
        ["adr-record-address"] = "docs/decisions/ADR-057-a-record-with-no-address.md",
        ["adr-partition-key"] = "docs/decisions/ADR-058-the-partition-key.md",
        ["adr-second-store"] = "docs/decisions/ADR-059-a-second-store-priced.md",
        ["adr-ports-wait"] = "docs/decisions/ADR-060-the-ports-learn-to-wait.md",
        ["adr-accounts-documents"] = "docs/decisions/ADR-061-accounts-on-a-document-store.md",
        ["adr-store-visible"] = "docs/decisions/ADR-062-what-the-store-is-actually-doing.md",
        ["adr-backends"] = "docs/decisions/ADR-063-backends-side-by-side.md",
        ["adr-measuring-stores"] = "docs/decisions/ADR-064-measuring-both-stores.md",
        ["adr-cosmos-explained"] = "docs/decisions/ADR-065-cosmos-db-explained.md",
        ["adr-one-container"] = "docs/decisions/ADR-066-one-container-both-stores.md",
        ["adr-proof"] = "docs/decisions/ADR-067-same-performance-proven.md",
        ["adr-five-minute-gate"] = "docs/decisions/ADR-068-the-five-minute-gate.md",
        ["adr-second-address"] = "docs/decisions/ADR-069-a-permanent-address-for-the-second-site.md",
        ["adr-three-readers"] = "docs/decisions/ADR-070-three-readers-with-no-memory.md",
        ["adr-activity"] = "docs/decisions/ADR-071-site-activity.md",
        ["adr-secrets"] = "docs/decisions/ADR-072-the-code-is-public-the-secrets-are-not.md",
        ["adr-kept-logs"] = "docs/decisions/ADR-073-logs-that-outlive-the-container.md",
        ["adr-highlighting"] = "docs/decisions/ADR-074-code-that-reads-like-code.md",
        ["adr-rules"] = "docs/decisions/ADR-075-the-rules-a-change-has-to-pass.md",
        ["adr-openapi"] = "docs/decisions/ADR-076-the-api-describes-itself.md",
        ["adr-page-status"] = "docs/decisions/ADR-077-every-page-checked.md",
        ["adr-machines"] = "docs/decisions/ADR-078-what-the-machines-are-doing.md",
        ["adr-one-plan"] = "docs/decisions/ADR-079-one-plan-two-sites.md",
        ["adr-admin-product"] = "docs/decisions/ADR-080-the-admin-tab-as-a-product.md",
        ["adr-glass-look"] = "docs/decisions/ADR-081-the-glass-look.md",
        ["adr-landing-page"] = "docs/decisions/ADR-082-the-landing-page-and-the-site-map.md",
        ["adr-tweaks"] = "docs/decisions/ADR-083-the-tweaks-pass.md",
        ["adr-component-folders"] = "docs/decisions/ADR-084-one-folder-per-component.md",
        ["adr-kept-awake"] = "docs/decisions/ADR-085-kept-awake.md",
        ["adr-composition-root"] = "docs/decisions/ADR-086-the-composition-root-split-by-job.md",
        ["adr-azure-costs"] = "docs/decisions/ADR-087-what-azure-charges.md",
        ["adr-onion-and-solid"] = "docs/decisions/ADR-088-onion-and-solid.md",
        ["adr-technology-versions"] = "docs/decisions/ADR-089-technology-versions.md",
        ["adr-dotnet-10"] = "docs/decisions/ADR-090-staying-on-dotnet-10.md",
        ["adr-docs-folders"] = "docs/decisions/ADR-091-one-folder-per-sidebar-section.md",
        ["adr-render-at-build-time"] = "docs/decisions/ADR-092-render-at-build-time.md",
        ["style-guide"] = "docs/style/STYLE-GUIDE.md",
        ["color-style"] = "docs/style/COLOR-STYLE.md",
        ["background-ribbon"] = "docs/style/BACKGROUND-RIBBON.md",
        ["ui-architecture"] = "docs/style/UI-ARCHITECTURE.md",
        ["document-style"] = "docs/style/DOCUMENT-STYLE.md",
        ["author"] = "docs/author/AUTHOR.md",
        ["security"] = "docs/best-practices/SECURITY.md",
        ["ai-development"] = "docs/about/AI-DEVELOPMENT.md",
        ["built-with-ai"] = "docs/built-with-ai/BUILT-WITH-AI.md",
        ["infrastructure-overview"] = "docs/performance/INFRASTRUCTURE-OVERVIEW.md",
        ["web-overview"] = "docs/performance/WEB-OVERVIEW.md",
        ["performance"] = "docs/performance/PERFORMANCE.md",
        ["site-traffic"] = "docs/site-traffic/SITE-TRAFFIC.md",
        ["traffic-who"] = "docs/site-traffic/TRAFFIC-WHO.md",
        ["traffic-kept"] = "docs/site-traffic/TRAFFIC-KEPT.md",
        ["traffic-found"] = "docs/site-traffic/TRAFFIC-FOUND.md",
        ["traffic-search-console"] = "docs/site-traffic/TRAFFIC-SEARCH-CONSOLE.md",
        ["architecture"] = "docs/app-architecture/ARCHITECTURE.md",
        ["style"] = "docs/app-architecture/STYLE.md",
    };
    // #endregion docs-catalog

    // #region diagrams
    /// <summary>Diagram name to its SVG file and page title (ADR-020): each opens on its own page at /api/docs/diagrams/{name}.</summary>
    public static readonly IReadOnlyDictionary<string, (string File, string Title)> Diagrams = new Dictionary<string, (string File, string Title)>(StringComparer.Ordinal)
    {
        ["infrastructure"] = ("docs/images/infrastructure.svg", "TheYard infrastructure"),
        ["dataflow"] = ("docs/images/dataflow.svg", "TheYard data flow"),
        ["erd"] = ("docs/images/erd.svg", "TheYard's database"),
        ["two-sites"] = ("docs/images/two-sites.svg", "TheYard's two sites"),
        ["sql-vs-cosmos"] = ("docs/images/sql-vs-cosmos.svg", "SQL Server and Cosmos DB, side by side"),
        ["ui-architecture"] = ("docs/images/ui-architecture.svg", "TheYard's UI, layer by layer"),
        ["rings"] = ("docs/images/rings.svg", "TheYard's rings"),
        ["bid-walk"] = ("docs/images/bid-walk.svg", "One bid through the rings"),
        ["port-adapter"] = ("docs/images/port-adapter.svg", "The inner ring owns the interface"),
    };
    // #endregion diagrams
}

// #region docs-images
/// <summary>
/// The pictures a document carries. The markdown names them on
/// GitHub's raw host so the files read on GitHub as they are; read here, that
/// meant every document's pictures came from a third host, and the README on a
/// phone was 1.4 MB, 985 KB of it a PNG rendered from an SVG this repository
/// already serves at 17 KB. So the served markdown names them here instead:
/// the raw address becomes /api/docs/images/{name}, and a PNG whose SVG source
/// stands beside it is served as that SVG. The markdown files themselves are
/// untouched, which is what keeps them right on GitHub.
/// </summary>
public static partial class DocImages
{
    public const string RawHost = "https://raw.githubusercontent.com/SteveStout/TheYard/main/docs/images/";
    public const string Route = "/api/docs/images/";

    /// <summary>A picture's name: letters, digits and hyphens, one of four extensions. Anything else is not a file read.</summary>
    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]*\.(png|jpg|jpeg|svg|webp)$", RegexOptions.IgnoreCase)]
    private static partial Regex NameShape();

    [GeneratedRegex(@"https://raw\.githubusercontent\.com/SteveStout/TheYard/main/docs/images/([a-z0-9][a-z0-9-]*)\.(png|jpg|jpeg|svg|webp)", RegexOptions.IgnoreCase)]
    private static partial Regex RawAddress();

    public static bool IsName(string name) => NameShape().IsMatch(name);

    public static string ContentType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".svg" => "image/svg+xml",
        ".webp" => "image/webp",
        _ => "application/octet-stream",
    };

    /// <summary>The file a name points at, under docs/images only, or null for a name the shape refuses.</summary>
    public static string? PathOf(string repoRoot, string name) =>
        IsName(name) ? Path.Combine(repoRoot, "docs", "images", name) : null;

    /// <summary>
    /// Every raw-host picture address in a served document, pointed here; a PNG with an SVG source beside it
    /// points at the SVG. The address carries the picture's size after a hash, #1280x800, which the request
    /// never sends: the page's renderer turns it into the image's width and height, so the space is held
    /// before the picture arrives and the words under it do not move when it lands (the operator's look,
    /// where a document's lead picture pushed a paragraph off the screen after the first paint).
    /// </summary>
    public static string Rewrite(string markdown, string repoRoot) =>
        RawAddress().Replace(markdown, match =>
        {
            string stem = match.Groups[1].Value;
            string extension = match.Groups[2].Value.ToLowerInvariant();
            bool drawn = extension == "png" && File.Exists(Path.Combine(repoRoot, "docs", "images", stem + ".svg"));
            string name = stem + (drawn ? ".svg" : "." + extension);
            (int Width, int Height)? size = SizeOf(Path.Combine(repoRoot, "docs", "images", name));
            return Route + name + (size is { } known ? $"#{known.Width}x{known.Height}" : "");
        });

    // #region picture-size
    /// <summary>
    /// A picture's width and height in pixels, read from its own header (a PNG's IHDR, a JPEG's frame marker,
    /// an SVG's width and height or its view box), or null for a file that is not there or not read here.
    /// </summary>
    public static (int Width, int Height)? SizeOf(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".svg")
        {
            string head = File.ReadAllText(path);
            int open = head.IndexOf("<svg", StringComparison.OrdinalIgnoreCase);
            int close = open < 0 ? -1 : head.IndexOf('>', open);
            if (close < 0)
            {
                return null;
            }
            string tag = head[open..close];
            Match width = SvgWidth().Match(tag);
            Match height = SvgHeight().Match(tag);
            if (width.Success && height.Success)
            {
                return (Pixels(width.Groups[1].Value), Pixels(height.Groups[1].Value));
            }
            Match box = SvgViewBox().Match(tag);
            return box.Success ? (Pixels(box.Groups[1].Value), Pixels(box.Groups[2].Value)) : null;
        }
        byte[] bytes = File.ReadAllBytes(path);
        if (extension == ".png" && bytes.Length >= 24)
        {
            return ((bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19],
                (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23]);
        }
        if (extension is ".jpg" or ".jpeg")
        {
            int at = 2;
            while (at + 9 < bytes.Length)
            {
                if (bytes[at] != 0xFF)
                {
                    at++;
                    continue;
                }
                byte marker = bytes[at + 1];
                if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
                {
                    return ((bytes[at + 7] << 8) | bytes[at + 8], (bytes[at + 5] << 8) | bytes[at + 6]);
                }
                at += 2 + ((bytes[at + 2] << 8) | bytes[at + 3]);
            }
        }
        return null;
    }

    private static int Pixels(string value) =>
        (int)Math.Round(double.Parse(value, CultureInfo.InvariantCulture), MidpointRounding.AwayFromZero);

    [GeneratedRegex(@"\swidth=""(\d+(?:\.\d+)?)(?:px)?""")]
    private static partial Regex SvgWidth();

    [GeneratedRegex(@"\sheight=""(\d+(?:\.\d+)?)(?:px)?""")]
    private static partial Regex SvgHeight();

    [GeneratedRegex(@"viewBox=""[\d.\-]+[ ,]+[\d.\-]+[ ,]+(\d+(?:\.\d+)?)[ ,]+(\d+(?:\.\d+)?)""")]
    private static partial Regex SvgViewBox();
    // #endregion picture-size
}
// #endregion docs-images
