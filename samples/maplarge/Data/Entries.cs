namespace TestProject.Data;

// Data holds records and nothing else: no behaviour, no dependencies (ADR-002).
// Every path on the wire is relative to the home directory with forward slashes,
// and "" is the home itself. Every instant is milliseconds since the epoch, UTC,
// the unit the browser's clock already uses (ADR-004).

/// <summary>What kind of thing a path points at in the store.</summary>
public enum EntryKind
{
    /// <summary>Nothing is there.</summary>
    None,
    /// <summary>A file.</summary>
    File,
    /// <summary>A folder.</summary>
    Folder,
}

/// <summary>One thing the store found, in the store's own terms: an absolute path and its facts.</summary>
/// <param name="Absolute">The absolute path on the machine.</param>
/// <param name="Kind">File or folder.</param>
/// <param name="SizeBytes">The size in bytes; zero for a folder.</param>
/// <param name="ModifiedMs">When it last changed, milliseconds since the epoch, UTC.</param>
public sealed record StoreEntry(string Absolute, EntryKind Kind, long SizeBytes, long ModifiedMs);

/// <summary>A folder as the browser shows it.</summary>
/// <param name="Name">The folder's own name.</param>
/// <param name="Path">Its path relative to the home directory, forward slashes.</param>
/// <param name="ModifiedMs">When it last changed, milliseconds since the epoch, UTC.</param>
public sealed record FolderEntry(string Name, string Path, long ModifiedMs);

/// <summary>A file as the browser shows it.</summary>
/// <param name="Name">The file's own name, extension included.</param>
/// <param name="Path">Its path relative to the home directory, forward slashes.</param>
/// <param name="SizeBytes">The size in bytes.</param>
/// <param name="ModifiedMs">When it last changed, milliseconds since the epoch, UTC.</param>
/// <param name="Extension">The extension without the dot, lower case, or "" when there is none.</param>
public sealed record FileEntry(string Name, string Path, long SizeBytes, long ModifiedMs, string Extension);

/// <summary>The counts and the size of everything in one view.</summary>
/// <param name="FolderCount">How many folders the view holds.</param>
/// <param name="FileCount">How many files the view holds.</param>
/// <param name="TotalBytes">The files' sizes added up.</param>
public sealed record Totals(int FolderCount, int FileCount, long TotalBytes);

/// <summary>One folder's contents, the reply to a browse.</summary>
/// <param name="Path">The folder shown, relative to home; "" is home.</param>
/// <param name="Parent">The folder above it, or null at home.</param>
/// <param name="Folders">The folders directly inside, sorted by name.</param>
/// <param name="Files">The files directly inside, sorted by name.</param>
/// <param name="Totals">Counts and bytes for exactly what is listed.</param>
/// <param name="TookMs">How long the server spent reading it.</param>
public sealed record Listing(
    string Path,
    string? Parent,
    IReadOnlyList<FolderEntry> Folders,
    IReadOnlyList<FileEntry> Files,
    Totals Totals,
    long TookMs);

/// <summary>Everything under a folder whose name matches a pattern, the reply to a search.</summary>
/// <param name="Query">The pattern as the caller sent it.</param>
/// <param name="Path">The folder the search started in.</param>
/// <param name="Folders">Matching folders, sorted by path.</param>
/// <param name="Files">Matching files, sorted by path.</param>
/// <param name="Totals">Counts and bytes for exactly what matched and was returned.</param>
/// <param name="Truncated">True when the search stopped at its limit before the tree ran out.</param>
/// <param name="TookMs">How long the server spent searching.</param>
public sealed record SearchResult(
    string Query,
    string Path,
    IReadOnlyList<FolderEntry> Folders,
    IReadOnlyList<FileEntry> Files,
    Totals Totals,
    bool Truncated,
    long TookMs);

/// <summary>The reply to an upload: what was written, and the folder's totals afterwards.</summary>
/// <param name="Entries">The files written, in the order they were received.</param>
/// <param name="Totals">The receiving folder's counts and bytes after the write.</param>
public sealed record TransferResult(IReadOnlyList<FileEntry> Entries, Totals Totals);

/// <summary>The body of a move or a copy.</summary>
/// <param name="From">The entry to move or copy, relative to home.</param>
/// <param name="To">Where it goes, relative to home, new name included.</param>
public sealed record MoveRequest(string From, string To);

/// <summary>What the footer shows: the version from the changelog and the commit it was built from.</summary>
/// <param name="Version">Four numbers, read from the top line of docs/CHANGELOG.md.</param>
/// <param name="Commit">The short commit hash, or "unknown" when there is no .git to read.</param>
public sealed record VersionInfo(string Version, string Commit);

/// <summary>One document the app serves, as the sidebar lists it.</summary>
/// <param name="Slug">The address under /api/docs/.</param>
/// <param name="Title">The document's first heading.</param>
/// <param name="Group">Which sidebar group it sits in.</param>
public sealed record DocEntry(string Slug, string Title, string Group);
