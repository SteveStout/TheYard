using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The clarity rule for the two folders the UI split made, src/app and
/// src/library, and for every stylesheet under src (ADR: The React
/// configuration, explained, the addenda on the split and on the
/// stylesheets). Every file it holds opens with three lines: what it does,
/// what it does not, and which files use it. The first two are for a reader
/// and only their presence is checked; the third is a fact, so it is read
/// against the files that really import each one. A header that still names a
/// file which stopped importing it, or misses one that started, fails here,
/// which is the only way a comment like that stays true.
/// </summary>
public class FileHeaderTests
{
    // #region the-header-rule
    /// <summary>The two folders the rule holds for code and stylesheets alike, relative to the repository root.</summary>
    private static readonly string[] Folders = ["src/app", "src/library"];

    /// <summary>What a file in them can be: code, and the stylesheets beside it.</summary>
    private static readonly string[] Kinds = [".ts", ".tsx", ".css"];

    /// <summary>And every stylesheet anywhere under this folder: a sheet grows a job at a time the way code does.</summary>
    private const string Sheets = "src";

    /// <summary>The three labels, in the order a header gives them.</summary>
    private static readonly string[] Labels = ["Does:", "Does not:", "Used by:"];

    /// <summary>
    /// Past three hundred lines, each with why. One job per file keeps a file
    /// short. The four component sheets named here each hold one
    /// component's look, shared by that component and the parts split out of
    /// it, and are over the line because the look is one piece; each comes
    /// off this list when its sheet is split by job.
    /// </summary>
    private static readonly Dictionary<string, string> LongAllowed = new(StringComparer.Ordinal)
    {
        ["src/components/landing/Landing/Landing.module.css"] = "the landing page's one look (hero, featured tiles, section tiles); split with Landing.tsx in the component split lane",
        ["src/components/layout/SideNav/SideNav.module.css"] = "the side rail's two shapes, one set of rows: one sheet shared by SideNav.tsx and the rows split out of it",
        ["src/components/admin/AdminPanel/AdminPanel.module.css"] = "the Admin workbench's frame: one sheet shared by AdminPanel.tsx and the parts split out of it",
        ["src/components/vehicle/VehicleDetail/VehicleDetail.module.css"] = "a vehicle's own page; split with VehicleDetail.tsx in the component split lane",
    };
    // #endregion the-header-rule

    [Fact]
    public void Every_file_opens_with_what_it_does_what_it_does_not_and_who_uses_it()
    {
        var files = SplitFiles();
        var wrong = new List<string>();
        foreach (string path in files)
        {
            var header = HeaderOf(path);
            if (header is null)
            {
                wrong.Add(
                    $"{Relative(path)} does not open with the header: its first comment, starting on line one, "
                    + "holds Does:, Does not: and Used by: in that order, with no blank line among them");
                continue;
            }

            foreach (string label in Labels)
            {
                if (!header.TryGetValue(label, out string? said) || said.Length == 0)
                {
                    wrong.Add($"{Relative(path)} has no '{label}' line, or an empty one");
                }
            }
        }

        Assert.True(files.Count >= 60, $"only {files.Count} files were found under src/app, src/library and the stylesheets under src");
        Assert.True(files.Count(path => path.EndsWith(".css", StringComparison.Ordinal)) >= 40, "fewer than forty stylesheets were read, so this is reading the wrong folders");
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void Used_by_names_exactly_the_files_that_import_it()
    {
        var importers = ImportersOf();
        var wrong = new List<string>();
        foreach (string path in SplitFiles())
        {
            var header = HeaderOf(path);
            if (header is null || !header.TryGetValue("Used by:", out string? said))
            {
                continue; // the test above names it
            }

            var named = Regex.Matches(said, @"[A-Za-z0-9_.-]+\.(?:tsx|ts|css)\b")
                .Select(match => match.Value)
                .ToHashSet(StringComparer.Ordinal);
            var real = new HashSet<string>(StringComparer.Ordinal);
            if (importers.TryGetValue(Path.GetFullPath(path), out var users))
            {
                real.UnionWith(users.Select(user => Path.GetFileName(user)));

                // A header names its users by file name, so two users with one name would read as one.
                if (real.Count < users.Count)
                {
                    wrong.Add($"{Relative(path)} is imported by two files with the same name; name them apart before listing them");
                }
            }

            if (!named.SetEquals(real))
            {
                wrong.Add(
                    $"{Relative(path)} says it is used by [{string.Join(", ", named.Order(StringComparer.Ordinal))}] "
                    + $"and is imported by [{string.Join(", ", real.Order(StringComparer.Ordinal))}]");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void No_file_runs_past_three_hundred_lines_but_the_ones_named_with_why()
    {
        var wrong = SplitFiles()
            .Where(path => File.ReadAllLines(path).Length > 300 && !LongAllowed.ContainsKey(Relative(path)))
            .Select(path => $"{Relative(path)} is {File.ReadAllLines(path).Length} lines: split it by job, or name it in LongAllowed with why")
            .ToList();

        // An exemption for a file that is gone, or has come back under the line, is one nobody needs.
        foreach ((string file, string why) in LongAllowed)
        {
            string path = Path.Combine(Repo.Root(), file);
            if (!File.Exists(path) || File.ReadAllLines(path).Length <= 300)
            {
                wrong.Add($"{file} is named as allowed past 300 lines ({why}) and is not");
            }
        }

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    /// <summary>Every file the rule holds, in a stable order. Tests are left out: a test says what it checks in its name.</summary>
    private static List<string> SplitFiles() =>
        Folders
            .Select(folder => Path.Combine(Repo.Root(), folder.Replace('/', Path.DirectorySeparatorChar)))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            .Where(path => Kinds.Contains(Path.GetExtension(path), StringComparer.Ordinal))
            .Concat(Directory.EnumerateFiles(Path.Combine(Repo.Root(), Sheets), "*.css", SearchOption.AllDirectories))
            .Where(path => !path.EndsWith(".test.ts", StringComparison.Ordinal)
                && !path.EndsWith(".test.tsx", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string Relative(string path) =>
        Path.GetRelativePath(Repo.Root(), path).Replace('\\', '/');

    // #region reading a header
    /// <summary>
    /// The header's three fields, or null when the file does not open with one.
    /// It is the file's first comment, before anything else: a line reading
    /// <c>/**</c>, then <c> * Does:</c>, <c> * Does not:</c> and <c> * Used by:</c>
    /// in that order, each of which may carry on over lines of its own, then
    /// the closing <c> */</c>.
    /// </summary>
    private static Dictionary<string, string>? HeaderOf(string path)
    {
        string[] lines = File.ReadAllLines(path);
        if (lines.Length == 0 || lines[0].Trim() != "/**")
        {
            return null;
        }

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        string? current = null;
        int next = 0;
        for (int i = 1; i < lines.Length && i < 20; i++)
        {
            string line = lines[i].Trim();
            if (line == "*/")
            {
                return next == Labels.Length ? fields : null;
            }

            string text = line.StartsWith('*') ? line[1..].Trim() : line;
            if (next < Labels.Length && text.StartsWith(Labels[next], StringComparison.Ordinal))
            {
                current = Labels[next];
                fields[current] = text[Labels[next].Length..].Trim();
                next++;
            }
            else if (current is not null && text.Length > 0)
            {
                fields[current] = (fields[current] + " " + text).Trim();
            }
            else
            {
                return null;
            }
        }

        return null;
    }
    // #endregion reading a header

    // #region who imports what
    /// <summary>
    /// For every file under src, the files that import it by a relative path:
    /// <c>import ... from './x'</c>, <c>import './x.css'</c> and
    /// <c>import('./x')</c>. A path without an extension is the file with one,
    /// or a folder's index, the way the bundler resolves it.
    /// </summary>
    private static Dictionary<string, List<string>> ImportersOf()
    {
        string src = Path.Combine(Repo.Root(), "src");
        var importers = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var specifier = new Regex(@"(?:\bfrom\s+|\bimport\s*\(\s*|\bimport\s+)'(\.{1,2}/[^']+)'");
        foreach (string file in Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".ts", StringComparison.Ordinal) || path.EndsWith(".tsx", StringComparison.Ordinal)))
        {
            string folder = Path.GetDirectoryName(file) ?? src;
            foreach (Match match in specifier.Matches(File.ReadAllText(file)))
            {
                string asked = match.Groups[1].Value;
                int query = asked.IndexOf('?', StringComparison.Ordinal);
                if (query >= 0)
                {
                    asked = asked[..query];
                }

                string target = Path.GetFullPath(Path.Combine(folder, asked.Replace('/', Path.DirectorySeparatorChar)));
                string? found = new[]
                {
                    target,
                    target + ".ts",
                    target + ".tsx",
                    Path.Combine(target, "index.ts"),
                    Path.Combine(target, "index.tsx"),
                }.FirstOrDefault(File.Exists);
                if (found is null)
                {
                    continue;
                }

                if (!importers.TryGetValue(found, out var users))
                {
                    importers[found] = users = [];
                }

                if (!users.Contains(file, StringComparer.Ordinal))
                {
                    users.Add(file);
                }
            }
        }

        return importers;
    }
    // #endregion who imports what
}
