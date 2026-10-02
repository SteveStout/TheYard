using TestProject.Documentation;

namespace TestProject.Tests;

/// <summary>
/// Checks LiveSamples, which pastes real source code into the served documents. A document can
/// hold an empty code block that names a file and a region, and the app fills it with the lines
/// between that region's start and end markers. The tests cover which paths may be read, how a
/// region is cut out, and what a bad block becomes. The path check uses the same rules as the file
/// browser's home guard on a shorter list of folders, because the document picks the path and must
/// never reach a file such as the development settings.
/// (more in docs/ADR-012-documents-served-by-the-app.md)
/// </summary>
public sealed class LiveSamplesTests
{
    [Theory]
    [InlineData("Domain/HomePath.cs", true)]
    [InlineData("Program.cs", true)]
    [InlineData("src/lib/urlState.ts", true)]
    [InlineData("wwwroot/js/lib/urlState.js", true)]
    [InlineData("../secrets.txt", false)]
    [InlineData("Domain/../Program.cs", false)]
    [InlineData("appsettings.Development.json", false)]
    [InlineData("Domain/", false)]
    [InlineData("", false)]
    public void Only_a_plain_path_under_an_allowed_root_may_be_read(string path, bool allowed)
    {
        Assert.Equal(allowed, LiveSamples.IsAllowedPath(path));
    }

    [Fact]
    public void A_region_is_cut_between_its_markers_and_nested_regions_are_kept_whole()
    {
        string[] source =
        [
            "a",
            "// #region outer",
            "b",
            "// #region inner",
            "c",
            "// #endregion",
            "d",
            "// #endregion",
            "e",
        ];
        string[] expected1 = ["b", "// #region inner", "c", "// #endregion", "d"];
        Assert.Equal(expected1, LiveSamples.Region(source, "outer"));
        string[] expected2 = ["c"];
        Assert.Equal(expected2, LiveSamples.Region(source, "inner"));
        Assert.Empty(LiveSamples.Region(source, "absent"));
    }

    [Fact]
    public void A_fence_expands_to_the_region_and_a_bad_one_to_a_note()
    {
        string expanded = LiveSamples.Expand("before\n```live path=Domain/HomePath.cs region=guard\n```\nafter", ProjectFolder.Root());
        Assert.Contains("```csharp Domain/HomePath.cs", expanded, StringComparison.Ordinal);
        Assert.Contains("public string Resolve(string? relative)", expanded, StringComparison.Ordinal);
        Assert.DoesNotContain("#region guard", expanded, StringComparison.Ordinal);
        Assert.StartsWith("before\n", expanded, StringComparison.Ordinal);
        Assert.EndsWith("\nafter", expanded, StringComparison.Ordinal);
        string note = LiveSamples.Expand("```live path=../x.cs region=y\n```", ProjectFolder.Root());
        Assert.StartsWith("> Sample unavailable", note, StringComparison.Ordinal);
    }
}
