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
        ["dataflow"] = "docs/DATAFLOW.md",
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
        ["ai-development"] = "docs/AI-DEVELOPMENT.md",
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
    };
    // #endregion diagrams
}
