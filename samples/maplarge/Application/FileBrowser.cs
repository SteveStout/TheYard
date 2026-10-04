using System.Diagnostics;
using TestProject.Data;
using TestProject.Domain;

namespace TestProject.Application;

/// <summary>
/// Carries out every file operation the API offers: browse, search, download, upload,
/// new folder, delete, move and copy. Each method takes paths as the API receives them
/// (relative to the home directory, forward slashes), turns them into absolute paths
/// through <see cref="HomePath"/>, asks the file store to do the work, and builds the
/// reply records. Nothing here knows about HTTP, and nothing here touches the disk
/// except through the <see cref="IFileStore"/> interface, so the rules can be tested
/// with an in-memory store (more in docs/ADR-002-one-project-four-folders-dependencies-inward.md).
/// </summary>
public sealed class FileBrowser(HomePath home, IFileStore store, FilesOptions options)
{
    // #region browse
    /// <summary>
    /// Lists one folder's direct contents with totals and timing. Folders come first,
    /// then files, and each group is sorted by name ignoring case.
    /// </summary>
    /// <param name="path">The folder, relative to home; "" means home itself.</param>
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
        string relative = home.Relative(absolute);
        return new Listing(relative, HomePath.ParentOf(relative), folders, files, ViewTotals.Of(folders, files), Elapsed(started));
    }
    // #endregion browse

    // #region search
    /// <summary>
    /// Finds every file and folder under a folder, at any depth, whose name matches the
    /// query, and stops once it has the maximum number of matches. The store hands back
    /// entries one at a time as it walks, so a search that fills up early never reads
    /// the rest of the tree. That early stop and the cap on results keep a search on a
    /// large tree fast (more in docs/ADR-008-performance-measured.md).
    /// </summary>
    /// <param name="path">The folder to search under, relative to home; "" means home.</param>
    /// <param name="query">A plain substring, or a glob using * or ?; empty is refused.</param>
    /// <param name="limit">
    /// The most matches to return. Null uses the configured default, and any value is
    /// clamped between 1 and the configured ceiling.
    /// </param>
    public SearchResult Search(string? path, string? query, int? limit)
    {
        long started = Stopwatch.GetTimestamp();
        var pattern = new NamePattern(query);
        if (pattern.IsEmpty)
        {
            throw ApiRefusalException.Refused("A search needs something to look for.");
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
        return new SearchResult(pattern.Query, home.Relative(absolute), folders, files, ViewTotals.Of(folders, files), truncated, Elapsed(started));
    }
    // #endregion search

    /// <summary>Checks that a path names a file and opens it through the store, so any store can
    /// serve downloads and no absolute path leaves this class. Returns the bytes and the entry.</summary>
    /// <param name="path">The file, relative to home.</param>
    public (Stream Content, FileEntry Entry) Download(string? path)
    {
        string absolute = home.Resolve(path);
        if (store.KindOf(absolute) != EntryKind.File)
        {
            throw ApiRefusalException.NotFound($"There is no file at '{home.Relative(absolute)}'.");
        }
        FileEntry entry = File(store.Describe(absolute));
        return (store.OpenRead(absolute), entry);
    }

    /// <summary>Returns true when the home directory is there to browse, for the health check.</summary>
    public bool HomeIsThere() => store.KindOf(home.Root) == EntryKind.Folder;

    // #region upload
    /// <summary>
    /// Writes one uploaded file into a folder. The bytes are copied straight from the
    /// request stream to the file stream, so a large upload is never held in memory.
    /// The declared size is checked before any byte is read, so an upload over the limit
    /// is refused without reading its content.
    /// </summary>
    /// <param name="folder">The folder that receives the file, relative to home.</param>
    /// <param name="name">
    /// The file name the browser sent. Only the last segment is kept, so a name that
    /// carries a path cannot place the file anywhere else.
    /// </param>
    /// <param name="length">The size in bytes the request declared for this file.</param>
    /// <param name="content">The stream of the file's bytes.</param>
    /// <param name="overwrite">True to replace an existing file with the same name.</param>
    public async Task<FileEntry> UploadAsync(string? folder, string? name, long length, Stream content, bool overwrite)
    {
        if (length > options.MaxUploadBytes)
        {
            throw ApiRefusalException.TooLarge($"'{name}' is {length:N0} bytes; the limit is {options.MaxUploadBytes:N0}.");
        }
        string absoluteFolder = Folder(folder);
        string target = Path.Combine(absoluteFolder, HomePath.ValidName(Path.GetFileName(name)));
        if (!overwrite && store.KindOf(target) != EntryKind.None)
        {
            throw ApiRefusalException.Conflict($"'{home.Relative(target)}' already exists.");
        }
        if (store.KindOf(target) == EntryKind.Folder)
        {
            throw ApiRefusalException.Conflict($"'{home.Relative(target)}' is a folder.");
        }
        await using (Stream file = store.Create(target, overwrite))
        {
            await content.CopyToAsync(file);
        }
        return File(store.Describe(target));
    }
    // #endregion upload

    /// <summary>
    /// Creates a new, empty folder inside an existing one. Refuses when anything with
    /// that name is already there.
    /// </summary>
    /// <param name="parent">The folder to create it in, relative to home.</param>
    /// <param name="name">The new folder's name; a single name, never a path.</param>
    public FolderEntry CreateFolder(string? parent, string? name)
    {
        string target = Path.Combine(Folder(parent), HomePath.ValidName(name));
        if (store.KindOf(target) != EntryKind.None)
        {
            throw ApiRefusalException.Conflict($"'{home.Relative(target)}' already exists.");
        }
        store.CreateFolder(target);
        return Folder(store.Describe(target));
    }

    /// <summary>
    /// Deletes a file, or a folder with everything inside it. Deleting the home directory
    /// itself is refused, because that would leave the app with nothing to browse.
    /// </summary>
    /// <param name="path">What to delete, relative to home.</param>
    public void Delete(string? path)
    {
        string absolute = Existing(path);
        if (home.IsRoot(absolute))
        {
            throw ApiRefusalException.Refused("The home directory cannot be deleted.");
        }
        store.Delete(absolute);
    }

    /// <summary>
    /// Moves a file or folder to a new location, which can also give it a new name, and
    /// returns it as the API sends it. The checks it shares with copy run first
    /// (see <see cref="Transfer"/>).
    /// </summary>
    /// <param name="from">What to move, relative to home.</param>
    /// <param name="to">The full new path, relative to home, new name included.</param>
    public object Move(string? from, string? to)
    {
        (string source, string target) = Transfer(from, to);
        store.Move(source, target);
        return Describe(store.Describe(target));
    }

    /// <summary>
    /// Copies a file, or a folder with everything inside it, to a new location, which can
    /// also give the copy a new name, and returns the copy as the API sends it. The checks
    /// it shares with move run first (see <see cref="Transfer"/>).
    /// </summary>
    /// <param name="from">What to copy, relative to home.</param>
    /// <param name="to">The full path of the copy, relative to home, new name included.</param>
    public object Copy(string? from, string? to)
    {
        (string source, string target) = Transfer(from, to);
        store.Copy(source, target);
        return Describe(store.Describe(target));
    }

    /// <summary>
    /// Converts a store entry, which holds an absolute path, into the record the API
    /// returns: a <see cref="FolderEntry"/> for a folder or a <see cref="FileEntry"/> for
    /// a file, both with paths relative to home. The answer is typed as object because the
    /// JSON writer writes the runtime type, so a file keeps its size and extension; a
    /// shared base record would drop them.
    /// </summary>
    /// <param name="entry">The entry the store described.</param>
    private object Describe(StoreEntry entry) => entry.Kind == EntryKind.Folder ? Folder(entry) : File(entry);

    // #region transfer-rules
    /// <summary>
    /// Runs the checks that move and copy both need, in order: the source exists, the
    /// source is not the home directory, the destination is not the home directory, the
    /// destination's parent is an existing folder, nothing is already at the destination,
    /// and a folder is not being put inside itself. That last check runs here because the
    /// filesystem would either fail partway through or, for a copy, keep copying the
    /// new folder into itself and never finish.
    /// </summary>
    private (string Source, string Target) Transfer(string? from, string? to)
    {
        string source = Existing(from);
        if (home.IsRoot(source))
        {
            throw ApiRefusalException.Refused("The home directory cannot be moved or copied.");
        }
        string target = home.Resolve(to);
        if (home.IsRoot(target))
        {
            throw ApiRefusalException.Refused("A destination needs a name.");
        }
        string? parent = Path.GetDirectoryName(target);
        if (parent is null || store.KindOf(parent) != EntryKind.Folder)
        {
            throw ApiRefusalException.NotFound($"There is no folder to put '{home.Relative(target)}' in.");
        }
        if (store.KindOf(target) != EntryKind.None)
        {
            throw ApiRefusalException.Conflict($"'{home.Relative(target)}' already exists.");
        }
        if (store.KindOf(source) == EntryKind.Folder && new HomePath(source).IsInside(target))
        {
            throw ApiRefusalException.Refused("A folder cannot be moved or copied into itself.");
        }
        return (source, target);
    }
    // #endregion transfer-rules

    private string Folder(string? path)
    {
        string absolute = home.Resolve(path);
        if (store.KindOf(absolute) != EntryKind.Folder)
        {
            throw ApiRefusalException.NotFound($"There is no folder at '{home.Relative(absolute)}'.");
        }
        return absolute;
    }

    private string Existing(string? path)
    {
        string absolute = home.Resolve(path);
        if (store.KindOf(absolute) == EntryKind.None)
        {
            throw ApiRefusalException.NotFound($"There is nothing at '{home.Relative(absolute)}'.");
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
        string relative = home.Relative(entry.Absolute);
        return new FolderEntry(HomePath.NameOf(relative), relative, entry.ModifiedMs);
    }

    private FileEntry File(StoreEntry entry)
    {
        string relative = home.Relative(entry.Absolute);
        string name = HomePath.NameOf(relative);
        return new FileEntry(name, relative, entry.SizeBytes, entry.ModifiedMs, Path.GetExtension(name).TrimStart('.').ToLowerInvariant());
    }

    /// <summary>Milliseconds since a Stopwatch timestamp, the took_ms every answer reports so a slow folder shows itself.</summary>
    private static long Elapsed(long started) => (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
}
