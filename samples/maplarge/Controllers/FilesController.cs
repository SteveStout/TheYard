using Microsoft.AspNetCore.Mvc;
using TestProject.Application;
using TestProject.Data;

namespace TestProject.Controllers;

/// <summary>
/// The file API: browse, search, download, upload, create folder, delete, move and copy.
/// Each action is only a few lines, because every rule lives in <see cref="FileBrowser"/>.
/// When a rule refuses a request it throws a <see cref="BrowserProblemException"/>, and
/// the exception handler turns that into a problem document.
/// </summary>
// Every path parameter is nullable on purpose. With a non-nullable parameter, [ApiController]
// would reject a missing value with its own validation response. Leaving it nullable lets the
// rules in FileBrowser decide: an empty path means the home directory, and anything invalid is
// refused with a message a person can read.
[ApiController]
[Route("api/files")]
public sealed class FilesController(FileBrowser browser) : ControllerBase
{
    // #region browse-and-search
    /// <summary>Lists one folder's contents. GET /api/files?path=docs</summary>
    /// <param name="path">A folder, relative to home; empty means home.</param>
    [HttpGet]
    public Listing Browse([FromQuery] string? path) => browser.Browse(path);

    /// <summary>
    /// Finds everything under a folder whose name matches the query.
    /// GET /api/files/search?path=&amp;q=*.md&amp;limit=200
    /// </summary>
    /// <param name="path">The folder to search under; empty means home.</param>
    /// <param name="q">A substring, or a glob pattern using * and ?.</param>
    /// <param name="limit">The maximum number of matches to return.</param>
    [HttpGet("search")]
    public SearchResult Search([FromQuery] string? path, [FromQuery] string? q, [FromQuery] int? limit) => browser.Search(path, q, limit);
    // #endregion browse-and-search

    /// <summary>
    /// Sends a file's bytes as a download. Range requests are enabled so a paused or broken
    /// download can resume where it stopped.
    /// </summary>
    /// <param name="path">A file, relative to home.</param>
    [HttpGet("download")]
    public IActionResult Download([FromQuery] string? path)
    {
        (string absolute, FileEntry entry) = browser.Download(path);
        return PhysicalFile(absolute, "application/octet-stream", entry.Name, enableRangeProcessing: true);
    }

    // #region upload
    /// <summary>
    /// Writes the files in a multipart form into a folder.
    /// POST /api/files/upload?path=docs&amp;overwrite=false
    /// The response lists each file as written plus the folder's new totals,
    /// so the page can update its counts without a second request.
    /// </summary>
    /// <param name="path">The folder that receives the files.</param>
    /// <param name="overwrite">True to replace existing files with the same name.</param>
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

    /// <summary>
    /// Creates a folder and answers 201 with it. POST /api/files/folder?path=docs&amp;name=notes
    /// </summary>
    /// <param name="path">The folder to create it in.</param>
    /// <param name="name">The new folder's name.</param>
    [HttpPost("folder")]
    public ActionResult<FolderEntry> CreateFolder([FromQuery] string? path, [FromQuery] string? name) =>
        StatusCode(StatusCodes.Status201Created, browser.CreateFolder(path, name));

    /// <summary>
    /// Deletes a file, or a folder and everything in it, and answers 204.
    /// DELETE /api/files?path=docs/old.txt
    /// </summary>
    /// <param name="path">The file or folder to delete.</param>
    [HttpDelete]
    public IActionResult Delete([FromQuery] string? path)
    {
        browser.Delete(path);
        return NoContent();
    }

    /// <summary>
    /// Moves a file or folder and returns it at its new location.
    /// POST /api/files/move with {"from": "a/b.txt", "to": "c/b.txt"}
    /// </summary>
    /// <param name="request">The source and destination, both relative to home.</param>
    [HttpPost("move")]
    public object Move([FromBody] MoveRequest request) => browser.Describe(browser.Move(request.From, request.To));

    /// <summary>
    /// Copies a file or folder and returns the new copy.
    /// POST /api/files/copy with {"from": "a/b.txt", "to": "c/b.txt"}
    /// </summary>
    /// <param name="request">The source and destination, both relative to home.</param>
    [HttpPost("copy")]
    public object Copy([FromBody] MoveRequest request) => browser.Describe(browser.Copy(request.From, request.To));
}
