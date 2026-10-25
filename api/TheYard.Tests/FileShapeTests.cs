using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// Holds every production source file to 300 lines, so a reader can open any file and see
/// what it is for. A longer file is usually doing more than one job, so it is split by job:
/// the file that keeps the name becomes the short list of its parts, the way Program.cs
/// lists what the app is made of. The rule covers the C# under api/ and the TypeScript under
/// src/ and render/, the rendering service beside the API. Tests are left out, because a test says what it checks in its name and a suite is as
/// long as the behaviour it covers; so are the database migrations and their generated
/// snapshots, which a tool writes and nobody edits. The front end's own header rule
/// (FileHeaderTests) holds the stylesheets. From 1.0.4.47 the same 300 lines hold a C# type
/// too, counted across every file a partial type is split into, because a partial class cut
/// into five files of 150 lines is still one class of 750 doing more than one job
/// (ADR: The rules a change has to pass, the addendum of that version).
/// </summary>
public sealed class FileShapeTests
{
    /// <summary>The longest a C# type may be, counted across all of its partial files.</summary>
    private const int LongestType = 300;

    /// <summary>A type declared at a line's start: every type in the API is top-level under a file-scoped namespace.</summary>
    private static readonly Regex Declaration = new(
        @"^(?:(?:public|internal|private|protected|file|sealed|static|abstract|partial|readonly|unsafe|ref)\s+)*(?:class|record(?:\s+struct|\s+class)?|struct|interface|enum)\s+(?<name>\w+)",
        RegexOptions.Multiline);

    /// <summary>The file-scoped namespace, which names the type with its name.</summary>
    private static readonly Regex Namespace = new(@"^namespace\s+(?<name>[\w.]+);", RegexOptions.Multiline);

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
        // The body is tests/rules/LengthRule.cs, the one the job hunt's suite runs over functions/ too.
        List<string> tooLong = TheYard.Rules.LengthRule.TooLong(new[] { "api", "src", "render" }
            .SelectMany(folder => Walk(Path.Combine(root, folder)))
            .Where(path => !IsTest(path) && !IsGenerated(path)), root);

        Assert.True(tooLong.Count == 0, string.Join(Environment.NewLine, tooLong));
    }

    [Fact]
    public void No_production_type_runs_past_three_hundred_lines_across_its_parts()
    {
        string root = Repo.Root();
        var types = new Dictionary<string, (int Lines, int Files)>(StringComparer.Ordinal);
        foreach (string path in Walk(Path.Combine(root, "api")).Where(path => path.EndsWith(".cs", StringComparison.Ordinal) && !IsGenerated(path)))
        {
            foreach (var (name, lines) in TypesIn(File.ReadAllText(path)))
            {
                types.TryGetValue(name, out var sum);
                types[name] = (sum.Lines + lines, sum.Files + 1);
            }
        }
        List<string> tooLong = types
            .Where(type => type.Value.Lines > LongestType)
            .Select(type => $"{type.Key} is {type.Value.Lines} lines across {type.Value.Files} file(s): split it by job")
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(types.Count > 500, $"the scan found {types.Count} types, which is too few to trust");
        Assert.True(tooLong.Count == 0, string.Join(Environment.NewLine, tooLong));
    }

    /// <summary>
    /// Each top-level type a file declares, with its lines from the doc comment and attributes
    /// above it to its closing brace, or to the semicolon of a record with no body.
    /// </summary>
    private static IEnumerable<(string Name, int Lines)> TypesIn(string text)
    {
        string space = Namespace.Match(text) is { Success: true } found ? found.Groups["name"].Value + "." : "";
        string[] lines = text.Split('\n');
        foreach (Match match in Declaration.Matches(text))
        {
            int first = LineOf(text, match.Index);
            while (first > 0 && (lines[first - 1].TrimStart().StartsWith("///", StringComparison.Ordinal) || lines[first - 1].TrimStart().StartsWith('[')))
            {
                first--;
            }
            int at = text.IndexOfAny(['{', ';'], match.Index + match.Length);
            if (at < 0)
            {
                continue;
            }
            if (text[at] == '{')
            {
                int depth = 0;
                for (; at < text.Length; at++)
                {
                    depth += text[at] == '{' ? 1 : text[at] == '}' ? -1 : 0;
                    if (depth == 0)
                    {
                        break;
                    }
                }
            }
            yield return (space + match.Groups["name"].Value, LineOf(text, at) - first + 1);
        }
    }

    /// <summary>The zero-based line a character of the text is on.</summary>
    private static int LineOf(string text, int index) => text.AsSpan(0, Math.Min(index, text.Length)).Count('\n');

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
