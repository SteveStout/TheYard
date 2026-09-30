namespace TestProject.Data;

// The records the app passes between its layers and sends as JSON. They hold values
// only: no behaviour and no references to other parts of the app, so every other
// folder can use them without pulling anything else in.
// Two conventions hold for every record here. A path sent to or from the API is
// relative to the home directory, uses forward slashes, and "" means home itself.
// A time is a count of milliseconds since 1 January 1970 UTC, because that is the
// number a browser's Date already uses, so the page needs no parsing.

/// <summary>What a path points at in the file store.</summary>
public enum EntryKind
{
    /// <summary>Nothing exists at the path.</summary>
    None,
    /// <summary>The path is a file.</summary>
    File,
    /// <summary>The path is a folder.</summary>
    Folder,
}

/// <summary>
/// One file or folder as the file store reports it: its absolute path on the machine and
/// its basic facts. It stays inside the server; the API sends the relative-path records below.
/// </summary>
/// <param name="Absolute">The full absolute path on the machine.</param>
/// <param name="Kind">Whether it is a file or a folder.</param>
/// <param name="SizeBytes">The size in bytes; zero for a folder.</param>
/// <param name="ModifiedMs">When it last changed, in milliseconds since 1 January 1970 UTC.</param>
public sealed record StoreEntry(string Absolute, EntryKind Kind, long SizeBytes, long ModifiedMs);

/// <summary>A folder as the API sends it to the page.</summary>
/// <param name="Name">The folder's own name, without the folders above it.</param>
/// <param name="Path">Its path relative to the home directory, with forward slashes.</param>
/// <param name="ModifiedMs">When it last changed, in milliseconds since 1 January 1970 UTC.</param>
public sealed record FolderEntry(string Name, string Path, long ModifiedMs);

/// <summary>A file as the API sends it to the page.</summary>
/// <param name="Name">The file's own name, extension included.</param>
/// <param name="Path">Its path relative to the home directory, with forward slashes.</param>
/// <param name="SizeBytes">The size in bytes.</param>
/// <param name="ModifiedMs">When it last changed, in milliseconds since 1 January 1970 UTC.</param>
/// <param name="Extension">The extension, lower case and without the dot; "" if none.</param>
public sealed record FileEntry(string Name, string Path, long SizeBytes, long ModifiedMs, string Extension);

/// <summary>
/// The folder count, file count and total file size for one listing or search result.
/// The server computes these so the page shows the same numbers the server returned.
/// </summary>
/// <param name="FolderCount">How many folders are in the result.</param>
/// <param name="FileCount">How many files are in the result.</param>
/// <param name="TotalBytes">The sizes of all the files in the result added together.</param>
public sealed record Totals(int FolderCount, int FileCount, long TotalBytes);

/// <summary>The API's reply to a browse: one folder's direct contents.</summary>
/// <param name="Path">The folder shown, relative to home; "" means home itself.</param>
/// <param name="Parent">The folder above it, or null when the folder shown is home.</param>
/// <param name="Folders">The folders directly inside, sorted by name.</param>
/// <param name="Files">The files directly inside, sorted by name.</param>
/// <param name="Totals">Counts and total size for exactly the entries listed.</param>
/// <param name="TookMs">How many milliseconds the server spent reading the folder.</param>
public sealed record Listing(
    string Path,
    string? Parent,
    IReadOnlyList<FolderEntry> Folders,
    IReadOnlyList<FileEntry> Files,
    Totals Totals,
    long TookMs);

/// <summary>The API's reply to a search: every entry under a folder whose name matches.</summary>
/// <param name="Query">The query as the caller sent it, with spaces at the ends removed.</param>
/// <param name="Path">The folder the search started in, relative to home.</param>
/// <param name="Folders">The matching folders, sorted by path.</param>
/// <param name="Files">The matching files, sorted by path.</param>
/// <param name="Totals">Counts and total size for exactly the matches returned.</param>
/// <param name="Truncated">True when the search hit its limit with more matches left over.</param>
/// <param name="TookMs">How many milliseconds the server spent searching.</param>
public sealed record SearchResult(
    string Query,
    string Path,
    IReadOnlyList<FolderEntry> Folders,
    IReadOnlyList<FileEntry> Files,
    Totals Totals,
    bool Truncated,
    long TookMs);

/// <summary>
/// The API's reply to an upload: the files written, and the receiving folder's totals
/// afterwards, so the page can show the new totals without a second request.
/// </summary>
/// <param name="Entries">The files written, in the order the request carried them.</param>
/// <param name="Totals">The receiving folder's counts and total size after the upload.</param>
public sealed record TransferResult(IReadOnlyList<FileEntry> Entries, Totals Totals);

/// <summary>The JSON body of a move or copy request.</summary>
/// <param name="From">The file or folder to move or copy, relative to home.</param>
/// <param name="To">The full path it goes to, relative to home, new name included.</param>
public sealed record MoveRequest(string From, string To);

/// <summary>The version and commit shown in the page footer.</summary>
/// <param name="Version">The four-part version from the top entry of docs/CHANGELOG.md.</param>
/// <param name="Commit">The short commit hash, or "unknown" when there is no .git to read.</param>
public sealed record VersionInfo(string Version, string Commit);

/// <summary>One document the app serves, as the documents sidebar lists it.</summary>
/// <param name="Slug">The name used in the document's address under /api/docs/.</param>
/// <param name="Title">The document's first heading.</param>
/// <param name="Group">The sidebar group the document is listed under.</param>
public sealed record DocEntry(string Slug, string Title, string Group);
