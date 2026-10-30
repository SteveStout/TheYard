using TestProject.Data;
using TestProject.Domain;

namespace TestProject.Application;

/// <summary>
/// Turns the paths the API receives into absolute ones and the entries the store describes into
/// the records the API answers with, refusing with the right status when a path names nothing, a
/// folder is missing, or a move would put a folder inside itself.
///
/// <para>Its own type because resolving and describing is a different job from carrying out an
/// operation, which is <see cref="FileBrowser"/>: every method here answers a question about a
/// path and none of them changes anything. It is handed in rather than built, so a test can hold
/// one of these against a store of its own.</para>
/// </summary>
/// <param name="home">The home directory every path is relative to.</param>
/// <param name="store">The file store, asked what is at a path.</param>
public sealed class BrowsePaths(HomePath home, IFileStore store)
{
    /// <summary>
    /// Converts a store entry, which holds an absolute path, into the record the API
    /// returns: a <see cref="FolderEntry"/> for a folder or a <see cref="FileEntry"/> for
    /// a file, both with paths relative to home. The answer is typed as object because the
    /// JSON writer writes the runtime type, so a file keeps its size and extension; a
    /// shared base record would drop them.
    /// </summary>
    /// <param name="entry">The entry the store described.</param>
    public object Describe(StoreEntry entry) => entry.Kind == EntryKind.Folder ? Folder(entry) : File(entry);

    // #region transfer-rules
    /// <summary>
    /// Runs the checks that move and copy both need, in order: the source exists, the
    /// source is not the home directory, the destination is not the home directory, the
    /// destination's parent is an existing folder, nothing is already at the destination,
    /// and a folder is not being put inside itself. That last check runs here because the
    /// filesystem would either fail partway through or, for a copy, keep copying the
    /// new folder into itself and never finish.
    /// </summary>
    public (string Source, string Target) Transfer(string? from, string? to)
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

    /// <summary>Resolves a path from home and returns it, or refuses with a 404 when no folder is there.</summary>
    public string Folder(string? path)
    {
        string absolute = home.Resolve(path);
        if (store.KindOf(absolute) != EntryKind.Folder)
        {
            throw ApiRefusalException.NotFound($"There is no folder at '{home.Relative(absolute)}'.");
        }
        return absolute;
    }

    /// <summary>Resolves a path from home and returns it, or refuses with a 404 when nothing is there.</summary>
    public string Existing(string? path)
    {
        string absolute = home.Resolve(path);
        if (store.KindOf(absolute) == EntryKind.None)
        {
            throw ApiRefusalException.NotFound($"There is nothing at '{home.Relative(absolute)}'.");
        }
        return absolute;
    }

    /// <summary>
    /// Sorts the entries the store handed back into a folder list and a file list by their
    /// kind, converting each on the way into the record the API returns.
    /// </summary>
    /// <param name="entries">The entries, in the order the store handed them back.</param>
    public (List<FolderEntry> Folders, List<FileEntry> Files) SplitByKind(IEnumerable<StoreEntry> entries)
    {
        var folders = new List<FolderEntry>();
        var files = new List<FileEntry>();
        foreach (StoreEntry entry in entries)
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
        return (folders, files);
    }

    /// <summary>A folder's listing entry: its name, its path from home and when it last changed.</summary>
    public FolderEntry Folder(StoreEntry entry)
    {
        string relative = home.Relative(entry.Absolute);
        return new FolderEntry(HomePath.NameOf(relative), relative, entry.ModifiedMs);
    }

    /// <summary>A file's listing entry: its name, path from home, size, time and lower-case extension.</summary>
    public FileEntry File(StoreEntry entry)
    {
        string relative = home.Relative(entry.Absolute);
        string name = HomePath.NameOf(relative);
        return new FileEntry(name, relative, entry.SizeBytes, entry.ModifiedMs, Path.GetExtension(name).TrimStart('.').ToLowerInvariant());
    }
}
