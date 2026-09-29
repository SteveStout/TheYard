using Microsoft.AspNetCore.Mvc;
using TestProject.Application;
using TestProject.Data;

namespace TestProject.Controllers;

/// <summary>
/// The file API: browse, search, download, upload, new folder, delete, move,
/// copy. Each action is a line or three, because every rule is in
/// <see cref="FileBrowser"/> and every refusal is a <see cref="BrowserProblemException"/>
/// the handler turns into a problem document (ADR-004).
/// </summary>
// Every path parameter is nullable on purpose: [ApiController] would answer a missing one with
// its own validation document, and the rules in FileBrowser say what an empty path means (home)
// and refuse it in a sentence a person can read.
[ApiController]
[Route("api/files")]
public sealed class FilesController(FileBrowser browser) : ControllerBase
{
    // #region browse-and-search
    /// <summary>One folder's contents. GET /api/files?path=docs</summary>
    /// <param name="path">A folder, relative to home; empty is home.</param>
    [HttpGet]
    public Listing Browse([FromQuery] string? path) => browser.Browse(path);

    /// <summary>Everything under a folder whose name matches. GET /api/files/search?path=&amp;q=*.md&amp;limit=200</summary>
    /// <param name="path">Where to start; empty is home.</param>
    /// <param name="q">A substring, or a glob with * and ?.</param>
    /// <param name="limit">The most matches to return.</param>
    [HttpGet("search")]
    public SearchResult Search([FromQuery] string? path, [FromQuery] string? q, [FromQuery] int? limit) => browser.Search(path, q, limit);
    // #endregion browse-and-search

    /// <summary>The file's bytes as an attachment, with range requests on so a paused download resumes.</summary>
    /// <param name="path">A file, relative to home.</param>
    [HttpGet("download")]
    public IActionResult Download([FromQuery] string? path)
    {
        (string absolute, FileEntry entry) = browser.Download(path);
        return PhysicalFile(absolute, "application/octet-stream", entry.Name, enableRangeProcessing: true);
    }

    // #region upload
    /// <summary>
    /// Writes the files in a multipart form into a folder. POST /api/files/upload?path=docs&amp;overwrite=false
    /// The reply carries each file as written and the folder's totals afterwards,
    /// so the page can update its counts without a second request.
    /// </summary>
    /// <param name="path">The receiving folder.</param>
    /// <param name="overwrite">True to replace files of the same name.</param>
    [HttpPost("upload")]
    public async Task<TransferResult> Upload([FromQuery] string? path, [FromQuery] bool overwrite = false)
    {
        if (!Request.HasFormContentType || Request.Form.Files.Count == 0)
        {
            throw BrowserProblemException.Refused("An upload needs at least one file.");
        }
        var written = new List<FileEntry>(Request.Form.Files.Count);
        foreach (IFormFile file in Request.Form.Files)
        {
            await using Stream content = file.OpenReadStream();
            written.Add(await browser.UploadAsync(path, file.FileName, file.Length, content, overwrite));
        }
        return new TransferResult(written, browser.Browse(path).Totals);
    }
    // #endregion upload

    /// <summary>Creates a folder. POST /api/files/folder?path=docs&amp;name=notes</summary>
    /// <param name="path">The parent folder.</param>
    /// <param name="name">The new folder's name.</param>
    [HttpPost("folder")]
    public ActionResult<FolderEntry> CreateFolder([FromQuery] string? path, [FromQuery] string? name) =>
        StatusCode(StatusCodes.Status201Created, browser.CreateFolder(path, name));

    /// <summary>Deletes a file, or a folder and its contents. DELETE /api/files?path=docs/old.txt</summary>
    /// <param name="path">What to delete.</param>
    [HttpDelete]
    public IActionResult Delete([FromQuery] string? path)
    {
        browser.Delete(path);
        return NoContent();
    }

    /// <summary>Moves a file or folder. POST /api/files/move with {"from": "a/b.txt", "to": "c/b.txt"}</summary>
    /// <param name="request">Source and destination, both relative to home.</param>
    [HttpPost("move")]
    public object Move([FromBody] MoveRequest request) => browser.Describe(browser.Move(request.From, request.To));

    /// <summary>Copies a file or folder. POST /api/files/copy with {"from": "a/b.txt", "to": "c/b.txt"}</summary>
    /// <param name="request">Source and destination, both relative to home.</param>
    [HttpPost("copy")]
    public object Copy([FromBody] MoveRequest request) => browser.Describe(browser.Copy(request.From, request.To));
}
