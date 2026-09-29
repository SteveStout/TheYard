using Microsoft.AspNetCore.Mvc;
using TestProject.Application;
using TestProject.Data;
using TestProject.Docs;

namespace TestProject.Controllers;

/// <summary>
/// The documents and the version: what the sidebar lists, each record as
/// markdown with its live code expanded, and the footer's numbers (ADR-012).
/// The markdown is rendered in the browser, never here.
/// </summary>
[ApiController]
[Route("api")]
public sealed class DocsController(DocsCatalog catalog, VersionInfo version, IWebHostEnvironment environment) : ControllerBase
{
    /// <summary>The sidebar: every document with its slug, title and group. GET /api/docs</summary>
    [HttpGet("docs")]
    public IReadOnlyList<DocEntry> List() => catalog.List();

    /// <summary>One document as text/markdown, live fences expanded from this build. GET /api/docs/adr-003-the-line-a-path-cannot-cross</summary>
    /// <param name="slug">The lower-case file name without .md.</param>
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

    /// <summary>The version from the changelog and the commit it was built from. GET /api/version</summary>
    [HttpGet("version")]
    public VersionInfo Version() => version;
}
