using System.Diagnostics;
using TestProject.Data;
using TestProject.Domain;

namespace TestProject.Application;

/// <summary>
/// The use cases: browse, search, download, upload, new folder, delete, move,
/// copy. Every one takes wire paths, resolves them through the home, asks the
/// store, and shapes the reply. Nothing here knows about HTTP, and nothing here
/// touches the disk except through the port (ADR-002).
/// </summary>
public sealed class FileBrowser(HomePath home, IFileStore store, FilesOptions options)
{
    /// <summary>The home directory these use cases are confined to.</summary>
    public HomePath Home { get; } = home;

    // #region browse
    /// <summary>One folder's direct contents, folders first, each sorted by name.</summary>
    /// <param name="path">A wire path to a folder; "" is home.</param>
    public Listing Browse(string? path)
    {
        long started = Stopwatch.GetTimestamp();
        string absolute = Folder(path);
        var folders = new List<FolderEntry>();
        var files = new List<FileEntry>();
        foreach (StoreEntry entry in store.Children(absolute))
        {
            Sort(entry, folders, files);
        }
        folders.Sort((a, b) => ViewTotals.NameOrder.Compare(a.Name, b.Name));
        files.Sort((a, b) => ViewTotals.NameOrder.Compare(a.Name, b.Name));
        string relative = Home.Relative(absolute);
        return new Listing(relative, HomePath.ParentOf(relative), folders, files, ViewTotals.Of(folders, files), Elapsed(started));
    }
    // #endregion browse

    // #region search
    /// <summary>
    /// Everything under a folder whose name matches, stopping at the limit. The
    /// walk is lazy, so a search that hits its limit in the first subfolder never
    /// reads the rest of the tree; that, and the limit itself, are what keep a
    /// search on a large tree cheap (ADR-008).
    /// </summary>
    /// <param name="path">Where to start; "" is home.</param>
    /// <param name="query">A substring or a glob; empty is refused.</param>
    /// <param name="limit">The most matches to return; null takes the configured default.</param>
    public SearchResult Search(string? path, string? query, int? limit)
    {
        long started = Stopwatch.GetTimestamp();
        var pattern = new NamePattern(query);
        if (pattern.IsEmpty)
        {
            throw BrowserProblemException.Refused("A search needs something to look for.");
        }
        int cap = Math.Clamp(limit ?? options.SearchLimit, 1, options.SearchCeiling);
        string absolute = Folder(path);
        var folders = new List<FolderEntry>();
        var files = new List<FileEntry>();
        bool truncated = false;
        foreach (StoreEntry entry in store.Descendants(absolute))
        {
            if (!pattern.Matches(Path.GetFileName(entry.Absolute)))
            {
                continue;
            }
            if (folders.Count + files.Count >= cap)
            {
                truncated = true;
                break;
            }
            Sort(entry, folders, files);
        }
        folders.Sort((a, b) => ViewTotals.NameOrder.Compare(a.Path, b.Path));
        files.Sort((a, b) => ViewTotals.NameOrder.Compare(a.Path, b.Path));
        return new SearchResult(pattern.Query, Home.Relative(absolute), folders, files, ViewTotals.Of(folders, files), truncated, Elapsed(started));
    }
    // #endregion search

    /// <summary>A file to send back: its absolute path for streaming and its entry for the headers.</summary>
    /// <param name="path">A wire path to a file.</param>
    public (string Absolute, FileEntry Entry) Download(string? path)
    {
        string absolute = Home.Resolve(path);
        if (store.KindOf(absolute) != EntryKind.File)
        {
            throw BrowserProblemException.NotFound($"There is no file at '{Home.Relative(absolute)}'.");
        }
        return (absolute, File(store.Describe(absolute)));
    }

    // #region upload
    /// <summary>
    /// Writes one uploaded file into a folder, streamed from the request to the
    /// disk so a large upload never sits in memory. The size is checked before a
    /// byte is read, so a refused upload costs nothing.
    /// </summary>
    /// <param name="folder">The receiving folder, a wire path.</param>
    /// <param name="name">The file's name as the browser sent it; only the name is kept, never a path.</param>
    /// <param name="length">The declared size in bytes.</param>
    /// <param name="content">The bytes.</param>
    /// <param name="overwrite">True to replace a file of the same name.</param>
    public async Task<FileEntry> UploadAsync(string? folder, string? name, long length, Stream content, bool overwrite)
    {
        if (length > options.MaxUploadBytes)
        {
            throw BrowserProblemException.TooLarge($"'{name}' is {length:N0} bytes; the limit is {options.MaxUploadBytes:N0}.");
        }
        string absoluteFolder = Folder(folder);
        string target = Path.Combine(absoluteFolder, HomePath.ValidName(Path.GetFileName(name)));
        if (!overwrite && store.KindOf(target) != EntryKind.None)
        {
            throw BrowserProblemException.Conflict($"'{Home.Relative(target)}' already exists.");
        }
        if (store.KindOf(target) == EntryKind.Folder)
        {
            throw BrowserProblemException.Conflict($"'{Home.Relative(target)}' is a folder.");
        }
        await using (Stream file = store.Create(target, overwrite))
        {
            await content.CopyToAsync(file);
        }
        return File(store.Describe(target));
    }
    // #endregion upload

    /// <summary>Creates a folder inside another.</summary>
    /// <param name="parent">The folder to create it in, a wire path.</param>
    /// <param name="name">The new folder's name.</param>
    public FolderEntry CreateFolder(string? parent, string? name)
    {
        string target = Path.Combine(Folder(parent), HomePath.ValidName(name));
        if (store.KindOf(target) != EntryKind.None)
        {
            throw BrowserProblemException.Conflict($"'{Home.Relative(target)}' already exists.");
        }
        store.CreateFolder(target);
        return Folder(store.Describe(target));
    }

    /// <summary>Deletes a file, or a folder and its contents. Home itself is refused.</summary>
    /// <param name="path">A wire path.</param>
    public void Delete(string? path)
    {
        string absolute = Existing(path);
        if (absolute == Home.Root)
        {
            throw BrowserProblemException.Refused("The home directory cannot be deleted.");
        }
        store.Delete(absolute);
    }

    /// <summary>Moves a file or folder to a new place, new name included.</summary>
    /// <param name="from">A wire path to what moves.</param>
    /// <param name="to">A wire path to where it goes.</param>
    public StoreEntry Move(string? from, string? to)
    {
        (string source, string target) = Transfer(from, to);
        store.Move(source, target);
        return store.Describe(target);
    }

    /// <summary>Copies a file or folder to a new place, new name included.</summary>
    /// <param name="from">A wire path to what is copied.</param>
    /// <param name="to">A wire path to where the copy goes.</param>
    public StoreEntry Copy(string? from, string? to)
    {
        (string source, string target) = Transfer(from, to);
        store.Copy(source, target);
        return store.Describe(target);
    }

    /// <summary>The wire entry for a store entry of either kind.</summary>
    /// <param name="entry">What the store described.</param>
    public object Describe(StoreEntry entry) => entry.Kind == EntryKind.Folder ? Folder(entry) : File(entry);

    // #region transfer-rules
    /// <summary>
    /// The checks a move and a copy share: the source exists, home itself is not
    /// moved, the destination's parent is a folder, nothing is at the destination
    /// already, and a folder is not put inside itself, which a filesystem would
    /// either refuse late or, for a copy, never finish.
    /// </summary>
    private (string Source, string Target) Transfer(string? from, string? to)
    {
        string source = Existing(from);
        if (source == Home.Root)
        {
            throw BrowserProblemException.Refused("The home directory cannot be moved or copied.");
        }
        string target = Home.Resolve(to);
        if (target == Home.Root)
        {
            throw BrowserProblemException.Refused("A destination needs a name.");
        }
        string? parent = Path.GetDirectoryName(target);
        if (parent is null || store.KindOf(parent) != EntryKind.Folder)
        {
            throw BrowserProblemException.NotFound($"There is no folder to put '{Home.Relative(target)}' in.");
        }
        if (store.KindOf(target) != EntryKind.None)
        {
            throw BrowserProblemException.Conflict($"'{Home.Relative(target)}' already exists.");
        }
        if (store.KindOf(source) == EntryKind.Folder && new HomePath(source).IsInside(target))
        {
            throw BrowserProblemException.Refused("A folder cannot be moved or copied into itself.");
        }
        return (source, target);
    }
    // #endregion transfer-rules

    private string Folder(string? path)
    {
        string absolute = Home.Resolve(path);
        if (store.KindOf(absolute) != EntryKind.Folder)
        {
            throw BrowserProblemException.NotFound($"There is no folder at '{Home.Relative(absolute)}'.");
        }
        return absolute;
    }

    private string Existing(string? path)
    {
        string absolute = Home.Resolve(path);
        if (store.KindOf(absolute) == EntryKind.None)
        {
            throw BrowserProblemException.NotFound($"There is nothing at '{Home.Relative(absolute)}'.");
        }
        return absolute;
    }

    private void Sort(StoreEntry entry, List<FolderEntry> folders, List<FileEntry> files)
    {
        if (entry.Kind == EntryKind.Folder)
        {
            folders.Add(Folder(entry));
        }
        else
        {
            files.Add(File(entry));
        }
    }

    private FolderEntry Folder(StoreEntry entry)
    {
        string relative = Home.Relative(entry.Absolute);
        return new FolderEntry(HomePath.NameOf(relative), relative, entry.ModifiedMs);
    }

    private FileEntry File(StoreEntry entry)
    {
        string relative = Home.Relative(entry.Absolute);
        string name = HomePath.NameOf(relative);
        return new FileEntry(name, relative, entry.SizeBytes, entry.ModifiedMs, Path.GetExtension(name).TrimStart('.').ToLowerInvariant());
    }

    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
