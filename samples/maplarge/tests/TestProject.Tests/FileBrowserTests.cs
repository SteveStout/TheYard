using TestProject.Application;
using TestProject.Data;

namespace TestProject.Tests;

/// <summary>
/// Tests how FileBrowser lists and finds: folders first and each sorted by name ignoring case, a
/// subfolder naming its parent, a search at any depth that stops at its limit and says so, and the
/// HTTP status each refusal maps to. It runs over the sample tree in a dictionary (SampleTree), so
/// it is fast and checks only the rules. PhysicalFileStore, which reads real files, is tested
/// through the running app in FilesApiTests.
/// </summary>
public sealed class FileBrowserTests : SampleTree
{
    [Fact]
    public void Browse_lists_folders_first_and_each_by_name_ignoring_case()
    {
        Listing listing = Browser.Browse("");
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
        Listing listing = Browser.Browse("docs/notes");
        Assert.Equal("docs/notes", listing.Path);
        Assert.Equal("docs", listing.Parent);
        Assert.Equal(new Totals(0, 2, 75), listing.Totals);
    }

    [Fact]
    public void Browse_of_a_missing_folder_is_404()
    {
        var problem = Assert.Throws<ApiRefusalException>(() => Browser.Browse("nowhere"));
        Assert.Equal(404, problem.Status);
        Assert.Contains("nowhere", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Browse_of_a_file_is_404_because_a_file_is_not_a_folder()
    {
        Assert.Equal(404, Assert.Throws<ApiRefusalException>(() => Browser.Browse("apple.txt")).Status);
    }

    [Fact]
    public void Search_finds_by_pattern_at_any_depth_and_sorts_by_path()
    {
        SearchResult result = Browser.Search("", "*.md", null);
        string[] expected3 = ["docs/notes/ideas.md", "docs/notes/todo.md", "docs/readme.md"];
        Assert.Equal(expected3, result.Files.Select(f => f.Path));
        Assert.Empty(result.Folders);
        Assert.False(result.Truncated);
        Assert.Equal(new Totals(0, 3, 195), result.Totals);
    }

    [Fact]
    public void Search_matches_folders_too()
    {
        SearchResult result = Browser.Search("", "notes", null);
        string[] expected4 = ["docs/notes"];
        Assert.Equal(expected4, result.Folders.Select(f => f.Path));
    }

    [Fact]
    public void Search_stops_at_the_limit_and_says_so()
    {
        SearchResult result = Browser.Search("", "*", 2);
        Assert.Equal(2, result.Folders.Count + result.Files.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Search_limit_is_clamped_to_the_ceiling_and_defaults_from_options()
    {
        SearchResult byDefault = Browser.Search("", "*", null);
        Assert.Equal(3, byDefault.Folders.Count + byDefault.Files.Count);
        SearchResult capped = Browser.Search("", "*", 999);
        Assert.Equal(5, capped.Folders.Count + capped.Files.Count);
    }

    [Fact]
    public void Search_with_nothing_to_look_for_is_400()
    {
        Assert.Equal(400, Assert.Throws<ApiRefusalException>(() => Browser.Search("", "  ", null)).Status);
    }
}
