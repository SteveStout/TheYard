using Microsoft.AspNetCore.Http.HttpResults;

namespace TheYard.Api;

/// <summary>
/// The documents the sidebar opens: every markdown record, the pictures they
/// carry, the diagrams on pages of their own, the Bicep and the resume. Read
/// from the repository root this process found at startup, never from an
/// address a caller supplies.
/// </summary>
public static class DocumentationEndpoints
{
    /// <summary>Maps the document routes under /api/docs.</summary>
    public static IEndpointRouteBuilder MapDocumentationEndpoints(this IEndpointRouteBuilder app)
    {
        #region docs-endpoint
        // One route for every document (ADR-017): the slug is looked up in the catalog
        // (DocsCatalog.cs, the same slugs src/library/records.ts and pages.ts carry), the file
        // is read from the repo root and its live blocks are expanded (ADR-014). A slug
        // missing from the catalog is a 404, never a file read. The Bicep file and the
        // resume keep their own routes below because they are not markdown; a literal
        // route wins over the {slug} pattern.
        app.MapGet("/api/docs/{slug}", Document)
            .WithName("GetDocument")
            .WithTags("Documents")
            .WithSummary("One of the served documents, as markdown with its code samples expanded")
            .WithDescription("The slug is one the sidebar offers: readme, architecture, a record such as adr-openapi. "
                + "Every live block is read from this build at request time.")
            .Produces<string>(StatusCodes.Status200OK, "text/markdown")
            .ProducesProblem(StatusCodes.Status404NotFound);
        #endregion docs-endpoint

        #region docs-images-endpoint
        // A document's picture, from the repository (DocsCatalog.cs, DocImages). The
        // name is held to one shape, so an address cannot climb out of docs/images, and
        // a name that is not there is a 404 with nothing read. Cached for a day, the
        // same as the photographs: a drawing changes with a commit, and a day is the
        // most a reader would see the old one.
        app.MapGet("/api/docs/images/{name}", Picture)
            .WithName("GetDocumentImage")
            .WithTags("Documents")
            .WithSummary("A picture one of the served documents carries")
            .WithDescription("The name is one a served document names, such as app-home.jpg or infrastructure.svg; the file is read from docs/images and nowhere else.")
            .Produces<byte[]>(StatusCodes.Status200OK, "image/png", "image/jpeg", "image/svg+xml", "image/webp")
            .ProducesProblem(StatusCodes.Status404NotFound);
        #endregion docs-images-endpoint

        #region diagram-page
        // A diagram on its own page (ADR-020): the SVG inlined in a small HTML document,
        // so it opens in a new tab, zooms with the browser, and keeps its text
        // selectable. The name is looked up in the catalog; nothing else is read.
        app.MapGet("/api/docs/diagrams/{name}", Diagram)
            .WithName("GetDiagram")
            .WithTags("Documents")
            .WithSummary("One diagram on its own page")
            .WithDescription("The SVG inlined in a small HTML document, so it zooms with the browser and keeps its text selectable.")
            .Produces<string>(StatusCodes.Status200OK, "text/html")
            .ProducesProblem(StatusCodes.Status404NotFound);
        #endregion diagram-page

        #region about-page
        // Who built this, on its own page for the reader who searched his name (AboutPage.cs,
        // ADR-020's pattern). Outside /api because it is a page a search engine lists, not a
        // resource; a literal route wins over the app's fallback. Each container names itself
        // from Site:Url, and without one (a developer's machine, the test host) from the request.
        app.MapGet("/about", About).ExcludeFromDescription();
        #endregion about-page

        app.MapGet("/api/docs/bicep", Bicep)
            .WithName("GetBicep")
            .WithTags("Documents")
            .WithSummary("The infrastructure definition, as a markdown page")
            .Produces<string>(StatusCodes.Status200OK, "text/markdown");

        app.MapGet("/api/docs/resume", Resume)
            .WithName("GetResume")
            .WithTags("Documents")
            .WithSummary("The author's resume")
            .Produces<byte[]>(StatusCodes.Status200OK, "application/pdf");

        return app;
    }

    private static Results<ContentHttpResult, ProblemHttpResult> Document(string slug, HostPaths paths, BuildInfo build) =>
        DocsCatalog.Files.TryGetValue(slug, out var file)
            ? TypedResults.Text(
                // The pictures are named here rather than on GitHub's raw host (DocsCatalog.cs, DocImages).
                DocImages.Rewrite(LiveCounts.Expand(LiveSamples.Expand(File.ReadAllText(Path.Combine(paths.RepoRoot, file)), paths.RepoRoot, build.Commit), paths.RepoRoot), paths.RepoRoot),
                "text/markdown")
            : TypedResults.Problem(detail: "No document has that slug.", statusCode: 404, title: "No such document");

    private static Results<PhysicalFileHttpResult, ProblemHttpResult> Picture(string name, HttpContext http, HostPaths paths)
    {
        string? path = DocImages.PathOf(paths.RepoRoot, name);
        if (path is null || !File.Exists(path))
        {
            return TypedResults.Problem(detail: "No served document carries a picture by that name.", statusCode: 404, title: "No such picture");
        }

        http.Response.Headers.CacheControl = "public, max-age=86400";
        return TypedResults.PhysicalFile(path, DocImages.ContentType(name));
    }

    private static Results<ContentHttpResult, ProblemHttpResult> Diagram(string name, HostPaths paths) =>
        DocsCatalog.Diagrams.TryGetValue(name, out var diagram)
            ? TypedResults.Content(
                DiagramPage.Render(diagram.Title, File.ReadAllText(Path.Combine(paths.RepoRoot, diagram.File)), diagram.File),
                "text/html; charset=utf-8")
            : TypedResults.Problem(detail: "No diagram has that name.", statusCode: 404, title: "No such diagram");

    private static ContentHttpResult About(HttpContext http, IConfiguration configuration)
    {
        string? configured = configuration["Site:Url"];
        string site = string.IsNullOrWhiteSpace(configured) ? $"{http.Request.Scheme}://{http.Request.Host}" : configured;
        return TypedResults.Content(AboutPage.Render(site), "text/html; charset=utf-8");
    }

    private static ContentHttpResult Bicep(HostPaths paths) =>
        TypedResults.Text("# infra/main.bicep" + "\n\nWhat runs, as code: one App Service plan and two web apps, which are the module below it, with Azure Front Door and the origin lock behind a parameter that stays off while the subscription refuses Front Door. Deployed in incremental mode only; the Hosting overview explains both.\n\n```bicep\n" + File.ReadAllText(Path.Combine(paths.RepoRoot, "infra", "main.bicep")) + "\n```\n\n## infra/appservice.bicep\n\nThe plan and the two sites, what differs between them, and every setting they carry.\n\n```bicep\n" + File.ReadAllText(Path.Combine(paths.RepoRoot, "infra", "appservice.bicep")) + "\n```\n", "text/markdown");

    private static PhysicalFileHttpResult Resume(HostPaths paths) =>
        TypedResults.PhysicalFile(paths.ResumePath, "application/pdf");
}
