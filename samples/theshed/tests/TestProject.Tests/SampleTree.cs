using TestProject.Application;
using TestProject.Data;
using TestProject.Domain;

namespace TestProject.Tests;

/// <summary>
/// The tree every FileBrowser test starts from, so each test reads as one rule and not as setup:
/// two folders with a subfolder, five files of known sizes, and names that sort differently with
/// and without letter case (Archive, apple, zebra). The search limit is 3 with a ceiling of 5, so
/// a search over the tree can run past its limit.
/// </summary>
public abstract class SampleTree
{
    /// <summary>The home folder, written the way the OS running the tests writes paths.</summary>
    protected static readonly string Root = OperatingSystem.IsWindows() ? @"C:\home" : "/home";

    /// <summary>The store the browser reads and writes, kept in memory.</summary>
    protected FakeStore Store { get; } = new(Root);

    /// <summary>The use cases under test, over that store and the home guard.</summary>
    protected FileBrowser Browser { get; }

    /// <summary>Builds the tree described above and a browser over it.</summary>
    protected SampleTree()
    {
        Browser = new FileBrowser(new HomePath(Root), Store, new FilesOptions { SearchLimit = 3, SearchCeiling = 5 });
        Store.Folder("docs");
        Store.Folder("docs/notes");
        Store.Folder("Archive");
        Store.File("docs/readme.md", 120);
        Store.File("docs/notes/todo.md", 30);
        Store.File("docs/notes/ideas.md", 45);
        Store.File("Archive/old.zip", 9000);
        Store.File("zebra.txt", 1);
        Store.File("apple.txt", 2);
    }
}

/// <summary>
/// An IFileStore kept in a dictionary of absolute paths. It behaves enough like a filesystem for
/// the rules to be tested, and it compares paths the way the OS running the tests does.
/// </summary>
public sealed class FakeStore(string root) : IFileStore
{
    /// <summary>
    /// Every folder and file by absolute path. The comparer follows the OS running the tests, so a
    /// letter-case rule behaves here the way it does on that machine's disk.
    /// </summary>
    private readonly Dictionary<string, StoreEntry> _entries = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    /// <summary>The contents of files written through Create, by absolute path.</summary>
    private readonly Dictionary<string, byte[]> _bytes = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>Adds a folder at a path relative to the home, so a test can lay out its tree in one line each.</summary>
    public void Folder(string relative) => _entries[Abs(relative)] = new StoreEntry(Abs(relative), EntryKind.Folder, 0, 1_000);

    /// <summary>Adds a file of a given size at a path relative to the home. Only the size is kept, because the rules read sizes, not bytes.</summary>
    public void File(string relative, long size) => _entries[Abs(relative)] = new StoreEntry(Abs(relative), EntryKind.File, size, 2_000);

    /// <summary>Turns a home-relative path with forward slashes into an absolute path for this OS.</summary>
    private string Abs(string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Whether a path is a folder, a file or nothing. The home itself is always a folder.</summary>
    public EntryKind KindOf(string absolute)
    {
        if (absolute == root)
        {
            return EntryKind.Folder;
        }
        return _entries.TryGetValue(absolute, out StoreEntry? entry) ? entry.Kind : EntryKind.None;
    }

    /// <summary>The stored entry for a path, or a bare entry for the home, which is never stored.</summary>
    public StoreEntry Describe(string absolute) => absolute == root ? new StoreEntry(root, EntryKind.Folder, 0, 0) : _entries[absolute];

    /// <summary>The entries directly inside a folder, the way a disk lists one level.</summary>
    public IEnumerable<StoreEntry> Children(string absoluteFolder) =>
        _entries.Values.Where(e => Path.GetDirectoryName(e.Absolute) == absoluteFolder);

    /// <summary>Every entry at any depth under a folder, which is what a search walks.</summary>
    public IEnumerable<StoreEntry> Descendants(string absoluteFolder) =>
        _entries.Values.Where(e => e.Absolute.StartsWith(absoluteFolder + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    /// <summary>The bytes written to a file, or an empty stream for a file added by size only.</summary>
    public Stream OpenRead(string absoluteFile) => new MemoryStream(_bytes.GetValueOrDefault(absoluteFile, []));

    /// <summary>
    /// A stream to write a new file into. The file appears in the tree only when the stream is
    /// closed, the way a real file is complete only once its handle closes.
    /// </summary>
    public Stream Create(string absoluteFile, bool overwrite)
    {
        var stream = new CapturingStream(bytes =>
        {
            _bytes[absoluteFile] = bytes;
            _entries[absoluteFile] = new StoreEntry(absoluteFile, EntryKind.File, bytes.Length, 3_000);
        });
        return stream;
    }

    /// <summary>Adds an empty folder at an absolute path.</summary>
    public void CreateFolder(string absoluteFolder) => _entries[absoluteFolder] = new StoreEntry(absoluteFolder, EntryKind.Folder, 0, 4_000);

    /// <summary>Removes a path and everything under it, as deleting a folder on disk does.</summary>
    public void Delete(string absolute)
    {
        foreach (string key in _entries.Keys.Where(k => k == absolute || k.StartsWith(absolute + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToList())
        {
            _entries.Remove(key);
            _bytes.Remove(key);
        }
    }

    /// <summary>A copy followed by a delete, which is all a move is for the rules under test.</summary>
    public void Move(string source, string destination)
    {
        Copy(source, destination);
        Delete(source);
    }

    /// <summary>Copies a path and everything under it to a new place, keeping each file's bytes.</summary>
    public void Copy(string source, string destination)
    {
        foreach (StoreEntry entry in _entries.Values.Where(e => e.Absolute == source || e.Absolute.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToList())
        {
            string target = destination + entry.Absolute[source.Length..];
            _entries[target] = entry with { Absolute = target };
            if (_bytes.TryGetValue(entry.Absolute, out byte[]? bytes))
            {
                _bytes[target] = bytes;
            }
        }
    }

    /// <summary>
    /// A write stream that passes its bytes to a callback when disposed. The fake file appears only
    /// then, the same way a real file is complete only once its handle closes.
    /// </summary>
    private sealed class CapturingStream(Action<byte[]> onClose) : MemoryStream
    {
        /// <summary>Hands the written bytes to the callback once, when the writer closes the stream.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                onClose(ToArray());
            }
            base.Dispose(disposing);
        }
    }
}
