using TestProject.Application;
using TestProject.Data;
using TestProject.Domain;

namespace TestProject.Tests;

/// <summary>
/// Tests FileBrowser (browse, search, download, upload, create, delete, move, copy) against a
/// store kept in a dictionary instead of a disk. With no temp folder the tests are fast and check
/// only the rules: sort order, totals, limits, and the HTTP status each failure maps to.
/// PhysicalFileStore, the class that reads and writes real files, is tested through the running
/// app in FilesApiTests.
/// </summary>
public sealed class FileBrowserTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\home" : "/home";
    private readonly FakeStore _store = new(Root);
    private readonly FileBrowser _browser;

    public FileBrowserTests()
    {
        _browser = new FileBrowser(new HomePath(Root), _store, new FilesOptions { SearchLimit = 3, SearchCeiling = 5 });
        _store.Folder("docs");
        _store.Folder("docs/notes");
        _store.Folder("Archive");
        _store.File("docs/readme.md", 120);
        _store.File("docs/notes/todo.md", 30);
        _store.File("docs/notes/ideas.md", 45);
        _store.File("Archive/old.zip", 9000);
        _store.File("zebra.txt", 1);
        _store.File("apple.txt", 2);
    }

    [Fact]
    public void Browse_lists_folders_first_and_each_by_name_ignoring_case()
    {
        Listing listing = _browser.Browse("");
        Assert.Equal("", listing.Path);
        Assert.Null(listing.Parent);
        string[] expected1 = ["Archive", "docs"];
        Assert.Equal(expected1, listing.Folders.Select(f => f.Name));
        string[] expected2 = ["apple.txt", "zebra.txt"];
        Assert.Equal(expected2, listing.Files.Select(f => f.Name));
        Assert.Equal(new Totals(2, 2, 3), listing.Totals);
    }

    [Fact]
    public void Browse_of_a_subfolder_names_its_parent()
    {
        Listing listing = _browser.Browse("docs/notes");
        Assert.Equal("docs/notes", listing.Path);
        Assert.Equal("docs", listing.Parent);
        Assert.Equal(new Totals(0, 2, 75), listing.Totals);
    }

    [Fact]
    public void Browse_of_a_missing_folder_is_404()
    {
        var problem = Assert.Throws<BrowserProblemException>(() => _browser.Browse("nowhere"));
        Assert.Equal(404, problem.Status);
        Assert.Contains("nowhere", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Browse_of_a_file_is_404_because_a_file_is_not_a_folder()
    {
        Assert.Equal(404, Assert.Throws<BrowserProblemException>(() => _browser.Browse("apple.txt")).Status);
    }

    [Fact]
    public void Search_finds_by_pattern_at_any_depth_and_sorts_by_path()
    {
        SearchResult result = _browser.Search("", "*.md", null);
        string[] expected3 = ["docs/notes/ideas.md", "docs/notes/todo.md", "docs/readme.md"];
        Assert.Equal(expected3, result.Files.Select(f => f.Path));
        Assert.Empty(result.Folders);
        Assert.False(result.Truncated);
        Assert.Equal(new Totals(0, 3, 195), result.Totals);
    }

    [Fact]
    public void Search_matches_folders_too()
    {
        SearchResult result = _browser.Search("", "notes", null);
        string[] expected4 = ["docs/notes"];
        Assert.Equal(expected4, result.Folders.Select(f => f.Path));
    }

    [Fact]
    public void Search_stops_at_the_limit_and_says_so()
    {
        SearchResult result = _browser.Search("", "*", 2);
        Assert.Equal(2, result.Folders.Count + result.Files.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Search_limit_is_clamped_to_the_ceiling_and_defaults_from_options()
    {
        SearchResult byDefault = _browser.Search("", "*", null);
        Assert.Equal(3, byDefault.Folders.Count + byDefault.Files.Count);
        SearchResult capped = _browser.Search("", "*", 999);
        Assert.Equal(5, capped.Folders.Count + capped.Files.Count);
    }

    [Fact]
    public void Search_with_nothing_to_look_for_is_400()
    {
        Assert.Equal(400, Assert.Throws<BrowserProblemException>(() => _browser.Search("", "  ", null)).Status);
    }

    [Fact]
    public void Download_names_the_file_and_refuses_a_folder()
    {
        (string absolute, FileEntry entry) = _browser.Download("docs/readme.md");
        Assert.EndsWith("readme.md", absolute, StringComparison.Ordinal);
        Assert.Equal("md", entry.Extension);
        Assert.Equal(404, Assert.Throws<BrowserProblemException>(() => _browser.Download("docs")).Status);
    }

    [Fact]
    public async Task Upload_writes_only_the_file_name_never_a_path()
    {
        using var content = new MemoryStream(new byte[10]);
        FileEntry entry = await _browser.UploadAsync("docs", "../../evil.txt", 10, content, overwrite: false);
        Assert.Equal("docs/evil.txt", entry.Path);
        Assert.Equal(EntryKind.File, _store.KindOf(Path.Combine(Root, "docs", "evil.txt")));
    }

    [Fact]
    public async Task Upload_refuses_an_existing_name_unless_told_to_overwrite()
    {
        using var content = new MemoryStream(new byte[5]);
        var problem = await Assert.ThrowsAsync<BrowserProblemException>(() => _browser.UploadAsync("docs", "readme.md", 5, content, overwrite: false));
        Assert.Equal(409, problem.Status);
        content.Position = 0;
        FileEntry entry = await _browser.UploadAsync("docs", "readme.md", 5, content, overwrite: true);
        Assert.Equal(5, entry.SizeBytes);
    }

    [Fact]
    public async Task Upload_past_the_limit_is_413_before_a_byte_is_read()
    {
        var options = new FilesOptions { MaxUploadBytes = 4 };
        var browser = new FileBrowser(new HomePath(Root), _store, options);
        using var content = new MemoryStream(new byte[5]);
        var problem = await Assert.ThrowsAsync<BrowserProblemException>(() => browser.UploadAsync("", "big.bin", 5, content, overwrite: false));
        Assert.Equal(413, problem.Status);
        Assert.Equal(0, content.Position);
    }

    [Fact]
    public void Create_folder_then_delete_it()
    {
        FolderEntry created = _browser.CreateFolder("docs", "drafts");
        Assert.Equal("docs/drafts", created.Path);
        Assert.Equal(409, Assert.Throws<BrowserProblemException>(() => _browser.CreateFolder("docs", "drafts")).Status);
        _browser.Delete("docs/drafts");
        Assert.Equal(EntryKind.None, _store.KindOf(Path.Combine(Root, "docs", "drafts")));
    }

    [Fact]
    public void Delete_refuses_the_home_and_404s_the_missing()
    {
        Assert.Equal(400, Assert.Throws<BrowserProblemException>(() => _browser.Delete("")).Status);
        Assert.Equal(404, Assert.Throws<BrowserProblemException>(() => _browser.Delete("ghost")).Status);
    }

    [Fact]
    public void Move_renames_and_relocates()
    {
        StoreEntry moved = _browser.Move("apple.txt", "Archive/apple-old.txt");
        Assert.Equal(EntryKind.File, moved.Kind);
        Assert.Equal(EntryKind.None, _store.KindOf(Path.Combine(Root, "apple.txt")));
        Assert.Equal(EntryKind.File, _store.KindOf(Path.Combine(Root, "Archive", "apple-old.txt")));
    }

    [Fact]
    public void Copy_leaves_the_source_in_place()
    {
        _browser.Copy("docs", "docs-copy");
        Assert.Equal(EntryKind.Folder, _store.KindOf(Path.Combine(Root, "docs")));
        Assert.Equal(EntryKind.Folder, _store.KindOf(Path.Combine(Root, "docs-copy")));
    }

    [Fact]
    public void A_folder_cannot_be_moved_or_copied_into_itself()
    {
        Assert.Equal(400, Assert.Throws<BrowserProblemException>(() => _browser.Move("docs", "docs/notes/docs")).Status);
        Assert.Equal(400, Assert.Throws<BrowserProblemException>(() => _browser.Copy("docs", "docs/inner")).Status);
    }

    [Fact]
    public void A_transfer_needs_an_existing_parent_and_a_free_destination()
    {
        Assert.Equal(404, Assert.Throws<BrowserProblemException>(() => _browser.Move("apple.txt", "nowhere/apple.txt")).Status);
        Assert.Equal(409, Assert.Throws<BrowserProblemException>(() => _browser.Move("apple.txt", "zebra.txt")).Status);
        Assert.Equal(404, Assert.Throws<BrowserProblemException>(() => _browser.Move("ghost.txt", "x.txt")).Status);
        Assert.Equal(400, Assert.Throws<BrowserProblemException>(() => _browser.Move("", "x")).Status);
        Assert.Equal(400, Assert.Throws<BrowserProblemException>(() => _browser.Move("apple.txt", "")).Status);
    }

    [Fact]
    public void A_path_the_home_refuses_surfaces_as_its_own_exception()
    {
        Assert.Throws<PathRefusedException>(() => _browser.Browse("../up"));
    }
}

/// <summary>
/// An IFileStore kept in a dictionary of absolute paths. It behaves enough like a filesystem for
/// the rules to be tested, and it compares paths the way the OS running the tests does.
/// </summary>
internal sealed class FakeStore(string root) : IFileStore
{
    private readonly Dictionary<string, StoreEntry> _entries = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _bytes = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    public void Folder(string relative) => _entries[Abs(relative)] = new StoreEntry(Abs(relative), EntryKind.Folder, 0, 1_000);

    public void File(string relative, long size) => _entries[Abs(relative)] = new StoreEntry(Abs(relative), EntryKind.File, size, 2_000);

    private string Abs(string relative) => Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));

    public EntryKind KindOf(string absolute)
    {
        if (absolute == root)
        {
            return EntryKind.Folder;
        }
        return _entries.TryGetValue(absolute, out StoreEntry? entry) ? entry.Kind : EntryKind.None;
    }

    public StoreEntry Describe(string absolute) => absolute == root ? new StoreEntry(root, EntryKind.Folder, 0, 0) : _entries[absolute];

    public IEnumerable<StoreEntry> Children(string absoluteFolder) =>
        _entries.Values.Where(e => Path.GetDirectoryName(e.Absolute) == absoluteFolder);

    public IEnumerable<StoreEntry> Descendants(string absoluteFolder) =>
        _entries.Values.Where(e => e.Absolute.StartsWith(absoluteFolder + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    public Stream OpenRead(string absoluteFile) => new MemoryStream(_bytes.GetValueOrDefault(absoluteFile, []));

    public Stream Create(string absoluteFile, bool overwrite)
    {
        var stream = new CapturingStream(bytes =>
        {
            _bytes[absoluteFile] = bytes;
            _entries[absoluteFile] = new StoreEntry(absoluteFile, EntryKind.File, bytes.Length, 3_000);
        });
        return stream;
    }

    public void CreateFolder(string absoluteFolder) => _entries[absoluteFolder] = new StoreEntry(absoluteFolder, EntryKind.Folder, 0, 4_000);

    public void Delete(string absolute)
    {
        foreach (string key in _entries.Keys.Where(k => k == absolute || k.StartsWith(absolute + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToList())
        {
            _entries.Remove(key);
            _bytes.Remove(key);
        }
    }

    public void Move(string source, string destination)
    {
        Copy(source, destination);
        Delete(source);
    }

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
