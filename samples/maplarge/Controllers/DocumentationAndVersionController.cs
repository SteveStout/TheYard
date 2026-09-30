using Microsoft.AspNetCore.Mvc;
using TestProject.Application;
using TestProject.Data;
using TestProject.Documentation;

namespace TestProject.Controllers;

/// <summary>
/// Serves the app's own documentation and version. It returns the list of documents for the
/// sidebar, each document as markdown with its code samples filled in from this build, and the
/// version and commit shown in the footer. The markdown is sent as plain text and turned into
/// HTML by the browser, so the server needs no markdown library.
/// </summary>
[ApiController]
[Route("api")]
public sealed class DocumentationAndVersionController(DocumentationCatalog catalog, VersionInfo version, IWebHostEnvironment environment) : ControllerBase
{
    /// <summary>Lists each document's slug, title and sidebar group. GET /api/docs</summary>
    [HttpGet("docs")]
    public IReadOnlyList<DocumentEntry> List() => catalog.List();

    /// <summary>
    /// Returns one document as text/markdown, with each live code block replaced by the current
    /// code from this build. Answers 404 when no document has that slug.
    /// GET /api/docs/adr-003-the-line-a-path-cannot-cross
    /// </summary>
    /// <param name="slug">The document's file name in lower case, without .md.</param>
    [HttpGet("docs/{slug}")]
    public IActionResult Document(string slug)
    {
        string? file = catalog.FileFor(slug.ToLowerInvariant());
        if (file is null)
        {
            throw BrowserProblemException.NotFound($"There is no document called '{slug}'.");
        }
        string markdown = LiveSamples.Expand(System.IO.File.ReadAllText(file), environment.ContentRootPath);
        return Content(markdown, "text/markdown; charset=utf-8");
    }

    /// <summary>
    /// Returns the version from the changelog and the commit the app was built from, both read at
    /// startup. GET /api/version
    /// </summary>
    [HttpGet("version")]
    public VersionInfo Version() => version;
}
