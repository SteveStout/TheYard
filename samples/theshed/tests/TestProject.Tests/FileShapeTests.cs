namespace TestProject.Tests;

/// <summary>
/// Checks that a reader can open any file and see what it is for. No C#, TypeScript or stylesheet
/// file runs past 300 lines, because a longer file is usually doing more than one job and is split by job
/// instead. The page's entry file, src/main.ts, stays a short list the way Program.cs does (the
/// layering tests hold Program.cs): every line that starts a part names, in a comment beside it,
/// the file that holds the details. (more in docs/ADR-002-one-project-four-folders-dependencies-inward.md)
/// </summary>
public sealed class FileShapeTests
{
    /// <summary>The longest a source file may be before it is split by job.</summary>
    private const int LongestFile = 300;

    /// <summary>The longest the entry file may be: a list of parts, not the parts themselves.</summary>
    private const int LongestEntryFile = 40;

    [Fact]
    public void No_source_file_runs_past_three_hundred_lines()
    {
        List<string> tooLong = ProjectFolder.FilesWith(".cs", ".ts", ".css")
            .Select(file => (file, lines: File.ReadAllLines(file).Length))
            .Where(entry => entry.lines > LongestFile)
            .Select(entry => $"{ProjectFolder.Relative(entry.file)} is {entry.lines} lines; split it by job")
            .ToList();
        Assert.Empty(tooLong);
    }

    [Fact]
    public void The_page_entry_is_a_short_list_with_each_part_beside_its_file()
    {
        string[] lines = File.ReadAllLines(Path.Combine(ProjectFolder.Root(), "src", "main.ts"));
        Assert.True(lines.Length <= LongestEntryFile, $"src/main.ts is {lines.Length} lines; move the details into the file its line names");

        // Every call between the composition markers carries a comment naming the file that holds it.
        int start = Array.FindIndex(lines, line => line.Trim() == "// #region composition");
        int end = Array.FindIndex(lines, line => line.Trim() == "// #endregion composition");
        Assert.True(start >= 0 && end > start, "src/main.ts has no composition region");
        List<string> unnamed = lines[(start + 1)..end]
            .Where(line => line.Trim().Length > 0 && !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .Where(line => !line.Contains(".ts:", StringComparison.Ordinal))
            .ToList();
        Assert.Empty(unnamed);
    }
}
