using System.Globalization;
using System.Text.RegularExpressions;

namespace TheYard.Api;

/// <summary>
/// Every document the site serves, by the slug the sidebar asks for (ADR-017).
/// src/components/DocsMenu.tsx carries the same slugs with titles and menus, so
/// a new record is one line here and one line there, and DocsCatalogTests holds
/// the two lists to each other. A slug missing from this table is a 404 at
/// /api/docs/{slug}, never a file read.
/// </summary>
public static class DocsCatalog
{
    // #region docs-catalog
    /// <summary>Slug to file, relative to the repo root. Every one goes through the live-sample expander (ADR-014).</summary>
    public static readonly IReadOnlyDictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["readme"] = "README.md",
        ["start-here"] = "docs/START-HERE.md",
        ["dataflow"] = "docs/DATAFLOW.md",
        ["sql-vs-cosmos"] = "docs/SQL-VS-COSMOS.md",
        ["projects"] = "docs/PROJECTS.md",
        ["hosting"] = "docs/HOSTING.md",
        ["adr-origin"] = "docs/ADR-001-front-door-origin.md",
        ["adr-docker"] = "docs/ADR-002-docker-packaging.md",
        ["adr-naming"] = "docs/ADR-003-azure-naming.md",
        ["adr-pivots"] = "docs/ADR-004-deployment-pivots.md",
        ["adr-edge-economics"] = "docs/ADR-007-edge-economics.md",
        ["adr-linux"] = "docs/ADR-008-linux-containers.md",
        ["cicd"] = "docs/CICD.md",
        ["adr-pipeline"] = "docs/ADR-009-deploy-pipeline.md",
        ["practices"] = "docs/BEST-PRACTICES.md",
        ["sealed"] = "docs/SEALED.md",
        ["adr-versioning"] = "docs/ADR-005-version-footer.md",
        ["adr-docs"] = "docs/ADR-006-docs-and-testing.md",
        ["adr-observability"] = "docs/ADR-010-observability.md",
        ["adr-phone"] = "docs/ADR-011-phone-header.md",
        // #region docs-changelog
        // The changelog and its record (ADR-012): one file, one sentence per version.
        ["changelog"] = "docs/CHANGELOG.md",
        ["adr-changelog"] = "docs/ADR-012-changelog.md",
        // #endregion docs-changelog
        ["adr-sidebar"] = "docs/ADR-013-sidebar.md",
        ["adr-live-samples"] = "docs/ADR-014-live-samples.md",
        ["adr-caching"] = "docs/ADR-015-cache-headers.md",
        ["adr-palette"] = "docs/ADR-016-palette.md",
        ["adr-review"] = "docs/ADR-017-staff-review.md",
        ["adr-program"] = "docs/ADR-018-program-cs-explained.md",
        ["adr-react"] = "docs/ADR-019-react-configuration-explained.md",
        ["adr-diagrams"] = "docs/ADR-020-diagram-pages.md",
        ["adr-tests"] = "docs/ADR-021-tests-explained.md",
        ["adr-grouping"] = "docs/ADR-022-app-architecture-group.md",
        ["adr-errors"] = "docs/ADR-023-error-handling.md",
        ["adr-telemetry"] = "docs/ADR-024-telemetry.md",
        ["adr-search"] = "docs/ADR-025-search-index.md",
        ["adr-keyboard"] = "docs/ADR-026-keyboard.md",
        ["adr-bidders"] = "docs/ADR-027-competing-bidders.md",
        ["adr-style"] = "docs/ADR-028-style-enforced.md",
        ["adr-records"] = "docs/ADR-029-records-index.md",
        ["adr-exceptions"] = "docs/ADR-030-exception-handler.md",
        ["adr-version-source"] = "docs/ADR-031-version-from-changelog.md",
        ["adr-method"] = "docs/ADR-032-ai-development.md",
        ["adr-store"] = "docs/ADR-033-relational-store.md",
        ["adr-ef"] = "docs/ADR-034-entity-framework-explained.md",
        ["adr-a11y-check"] = "docs/ADR-035-accessibility-check.md",
        ["adr-photos"] = "docs/ADR-036-responsive-photos.md",
        ["adr-accounts"] = "docs/ADR-037-accounts.md",
        ["adr-identity"] = "docs/ADR-038-identity-explained.md",
        ["adr-sql-server"] = "docs/ADR-039-sql-server-backend.md",
        ["adr-data-first"] = "docs/ADR-040-database-source-control.md",
        ["adr-providers"] = "docs/ADR-041-two-providers-explained.md",
        ["adr-exemptions"] = "docs/ADR-042-exemptions-that-hide.md",
        ["adr-sql-visible"] = "docs/ADR-043-what-the-database-is-doing.md",
        ["adr-interceptors"] = "docs/ADR-044-interceptors-explained.md",
        ["adr-self-review"] = "docs/ADR-045-reviewing-my-own-work.md",
        ["adr-the-name"] = "docs/ADR-046-the-name.md",
        ["adr-second-manifest"] = "docs/ADR-047-the-second-manifest.md",
        ["adr-reset"] = "docs/ADR-048-reset-is-one-persons.md",
        ["adr-room-account"] = "docs/ADR-049-the-room-needs-an-account.md",
        ["adr-lockout"] = "docs/ADR-050-a-password-guess-should-cost-something.md",
        ["adr-coverage"] = "docs/ADR-051-counting-what-the-tests-cover.md",
        ["adr-where-gates-live"] = "docs/ADR-052-where-a-gate-lives.md",
        ["adr-public-face"] = "docs/ADR-053-the-public-face.md",
        ["adr-one-write"] = "docs/ADR-054-the-one-write-a-stranger-can-make.md",
        ["adr-broken-windows"] = "docs/ADR-055-broken-windows.md",
        ["adr-stale-listing"] = "docs/ADR-056-the-listing-that-went-stale.md",
        ["adr-record-address"] = "docs/ADR-057-a-record-with-no-address.md",
        ["adr-partition-key"] = "docs/ADR-058-the-partition-key.md",
        ["adr-second-store"] = "docs/ADR-059-a-second-store-priced.md",
        ["adr-ports-wait"] = "docs/ADR-060-the-ports-learn-to-wait.md",
        ["adr-accounts-documents"] = "docs/ADR-061-accounts-on-a-document-store.md",
        ["adr-store-visible"] = "docs/ADR-062-what-the-store-is-actually-doing.md",
        ["adr-backends"] = "docs/ADR-063-backends-side-by-side.md",
        ["adr-measuring-stores"] = "docs/ADR-064-measuring-both-stores.md",
        ["adr-cosmos-explained"] = "docs/ADR-065-cosmos-db-explained.md",
        ["adr-one-container"] = "docs/ADR-066-one-container-both-stores.md",
        ["adr-proof"] = "docs/ADR-067-same-performance-proven.md",
        ["adr-five-minute-gate"] = "docs/ADR-068-the-five-minute-gate.md",
        ["adr-second-address"] = "docs/ADR-069-a-permanent-address-for-the-second-site.md",
        ["adr-three-readers"] = "docs/ADR-070-three-readers-with-no-memory.md",
        ["adr-activity"] = "docs/ADR-071-site-activity.md",
        ["adr-secrets"] = "docs/ADR-072-the-code-is-public-the-secrets-are-not.md",
        ["adr-kept-logs"] = "docs/ADR-073-logs-that-outlive-the-container.md",
        ["adr-highlighting"] = "docs/ADR-074-code-that-reads-like-code.md",
        ["adr-rules"] = "docs/ADR-075-the-rules-a-change-has-to-pass.md",
        ["adr-openapi"] = "docs/ADR-076-the-api-describes-itself.md",
        ["adr-page-status"] = "docs/ADR-077-every-page-checked.md",
        ["adr-machines"] = "docs/ADR-078-what-the-machines-are-doing.md",
        ["adr-one-plan"] = "docs/ADR-079-one-plan-two-sites.md",
        ["adr-admin-product"] = "docs/ADR-080-the-admin-tab-as-a-product.md",
        ["adr-glass-look"] = "docs/ADR-081-the-glass-look.md",
        ["adr-landing-page"] = "docs/ADR-082-the-landing-page-and-the-site-map.md",
        ["color-style"] = "docs/COLOR-STYLE.md",
        ["author"] = "docs/AUTHOR.md",
        ["security"] = "docs/SECURITY.md",
        ["ai-development"] = "docs/AI-DEVELOPMENT.md",
        ["built-with-ai"] = "docs/BUILT-WITH-AI.md",
        ["infrastructure-overview"] = "docs/INFRASTRUCTURE-OVERVIEW.md",
        ["web-overview"] = "docs/WEB-OVERVIEW.md",
        ["performance"] = "docs/PERFORMANCE.md",
        ["architecture"] = "docs/ARCHITECTURE.md",
        ["style"] = "docs/STYLE.md",
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
    };
    // #endregion diagrams
}

// #region docs-images
/// <summary>
/// The pictures a document carries (1.0.3.5). The markdown names them on
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
