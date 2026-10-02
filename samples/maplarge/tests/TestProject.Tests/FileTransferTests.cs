using TestProject.Application;
using TestProject.Data;
using TestProject.Domain;

namespace TestProject.Tests;

/// <summary>
/// Tests the FileBrowser use cases that change the tree or hand a file out: download, create a
/// folder, delete, move and copy. The rules under test are the ones that protect the home: it is
/// never deleted or moved, a folder never goes inside itself, a transfer needs an existing parent
/// and a free destination, and a path the home guard refuses keeps its own exception.
/// </summary>
public sealed class FileTransferTests : SampleTree
{
    [Fact]
    public void Download_names_the_file_and_refuses_a_folder()
    {
        (string absolute, FileEntry entry) = Browser.Download("docs/readme.md");
        Assert.EndsWith("readme.md", absolute, StringComparison.Ordinal);
        Assert.Equal("md", entry.Extension);
        Assert.Equal(404, Assert.Throws<ApiRefusalException>(() => Browser.Download("docs")).Status);
    }

    [Fact]
    public void Create_folder_then_delete_it()
    {
        FolderEntry created = Browser.CreateFolder("docs", "drafts");
        Assert.Equal("docs/drafts", created.Path);
        Assert.Equal(409, Assert.Throws<ApiRefusalException>(() => Browser.CreateFolder("docs", "drafts")).Status);
        Browser.Delete("docs/drafts");
        Assert.Equal(EntryKind.None, Store.KindOf(Path.Combine(Root, "docs", "drafts")));
    }

    [Fact]
    public void Delete_refuses_the_home_and_404s_the_missing()
    {
        Assert.Equal(400, Assert.Throws<ApiRefusalException>(() => Browser.Delete("")).Status);
        Assert.Equal(404, Assert.Throws<ApiRefusalException>(() => Browser.Delete("ghost")).Status);
    }

    [Fact]
    public void Move_renames_and_relocates()
    {
        StoreEntry moved = Browser.Move("apple.txt", "Archive/apple-old.txt");
        Assert.Equal(EntryKind.File, moved.Kind);
        Assert.Equal(EntryKind.None, Store.KindOf(Path.Combine(Root, "apple.txt")));
        Assert.Equal(EntryKind.File, Store.KindOf(Path.Combine(Root, "Archive", "apple-old.txt")));
    }

    [Fact]
    public void Copy_leaves_the_source_in_place()
    {
        Browser.Copy("docs", "docs-copy");
        Assert.Equal(EntryKind.Folder, Store.KindOf(Path.Combine(Root, "docs")));
        Assert.Equal(EntryKind.Folder, Store.KindOf(Path.Combine(Root, "docs-copy")));
    }

    [Fact]
    public void A_folder_cannot_be_moved_or_copied_into_itself()
    {
        Assert.Equal(400, Assert.Throws<ApiRefusalException>(() => Browser.Move("docs", "docs/notes/docs")).Status);
        Assert.Equal(400, Assert.Throws<ApiRefusalException>(() => Browser.Copy("docs", "docs/inner")).Status);
    }

    [Fact]
    public void A_transfer_needs_an_existing_parent_and_a_free_destination()
    {
        Assert.Equal(404, Assert.Throws<ApiRefusalException>(() => Browser.Move("apple.txt", "nowhere/apple.txt")).Status);
        Assert.Equal(409, Assert.Throws<ApiRefusalException>(() => Browser.Move("apple.txt", "zebra.txt")).Status);
        Assert.Equal(404, Assert.Throws<ApiRefusalException>(() => Browser.Move("ghost.txt", "x.txt")).Status);
        Assert.Equal(400, Assert.Throws<ApiRefusalException>(() => Browser.Move("", "x")).Status);
        Assert.Equal(400, Assert.Throws<ApiRefusalException>(() => Browser.Move("apple.txt", "")).Status);
    }

    [Fact]
    public void A_path_the_home_refuses_surfaces_as_its_own_exception()
    {
        Assert.Throws<PathRefusedException>(() => Browser.Browse("../up"));
    }
}
