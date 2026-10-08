namespace TheYard.Tests;

/// <summary>
/// Holds every production source file to 300 lines, so a reader can open any file and see
/// what it is for. A longer file is usually doing more than one job, so it is split by job:
/// the file that keeps the name becomes the short list of its parts, the way Program.cs
/// lists what the app is made of. The rule covers the C# under api/ and the TypeScript under
/// src/ and render/, the rendering service beside the API. Tests are left out, because a test says what it checks in its name and a suite is as
/// long as the behaviour it covers; so are the database migrations and their generated
/// snapshots, which a tool writes and nobody edits. The front end's own header rule
/// (FileHeaderTests) holds the stylesheets.
/// (ADR: The rules a change has to pass)
/// </summary>
public sealed class FileShapeTests
{
    /// <summary>The longest a source file may be before it is split by job.</summary>
    private const int LongestFile = 300;

    /// <summary>The kinds of file the rule reads: C#, and TypeScript with or without JSX.</summary>
    private static readonly string[] Kinds = [".cs", ".ts", ".tsx"];

    /// <summary>
    /// Folders the rule does not read, by name: build output, other people's packages, the
    /// test project and the migrations project.
    /// </summary>
    private static readonly string[] Skipped =
    [
        "bin", "obj", "node_modules", "TheYard.Tests", "TheYard.Migrations.Sqlite",
    ];

    [Fact]
    public void No_production_source_file_runs_past_three_hundred_lines()
    {
        string root = Repo.Root();
        List<string> tooLong = new[] { "api", "src", "render" }
            .SelectMany(folder => Walk(Path.Combine(root, folder)))
            .Where(path => !IsTest(path) && !IsGenerated(path))
            .Select(path => (path, lines: File.ReadAllLines(path).Length))
            .Where(file => file.lines > LongestFile)
            .Select(file => $"{Path.GetRelativePath(root, file.path).Replace('\\', '/')} is {file.lines} lines: split it by job")
            .ToList();

        Assert.True(tooLong.Count == 0, string.Join(Environment.NewLine, tooLong));
    }

    /// <summary>Every source file of the kinds above under a folder, skipping the folders named in Skipped.</summary>
    private static IEnumerable<string> Walk(string folder)
    {
        foreach (string file in Directory.EnumerateFiles(folder))
        {
            if (Kinds.Contains(Path.GetExtension(file), StringComparer.Ordinal))
            {
                yield return file;
            }
        }

        foreach (string child in Directory.EnumerateDirectories(folder))
        {
            if (!Skipped.Contains(Path.GetFileName(child), StringComparer.Ordinal))
            {
                foreach (string file in Walk(child))
                {
                    yield return file;
                }
            }
        }
    }

    /// <summary>A front-end test file, named *.test.ts or *.test.tsx beside the code it checks.</summary>
    private static bool IsTest(string path) =>
        path.EndsWith(".test.ts", StringComparison.Ordinal) || path.EndsWith(".test.tsx", StringComparison.Ordinal);

    /// <summary>A file a tool writes: a type declaration file or an EF designer or snapshot file.</summary>
    private static bool IsGenerated(string path) =>
        path.EndsWith(".d.ts", StringComparison.Ordinal)
        || path.EndsWith(".Designer.cs", StringComparison.Ordinal)
        || path.EndsWith("ModelSnapshot.cs", StringComparison.Ordinal);
}
