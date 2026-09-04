using System.Text;

namespace TheYard.Tests;

/// <summary>
/// The house rule is that nothing here contains an em dash, and until now the
/// only thing enforcing it across the repository was a PowerShell scan in a
/// script on one developer's machine.
///
/// <para>That is a gate in the worst possible place. It runs when somebody
/// remembers to ship through that script, it does not run on the CI runner, and
/// it cannot run for anybody who clones this repository, so a commit made any
/// other way passes every check the project can actually perform. The one test
/// that did enforce it read the changelog and nothing else, while the rule
/// covers every decision record, the README, the security page, and every code
/// comment, which the site displays as live samples. Em dashes have reached the
/// code comments once already (1.0.0.35).</para>
///
/// <para>So the scan moved here, where CI runs it, a clone runs it, and the
/// failure names the file and the line instead of a path on one machine.</para>
/// </summary>
public class HouseVoiceTests
{
    // #region what is scanned
    /// <summary>
    /// The text this project writes. Binaries and vendored trees are not in
    /// it; everything a reader of this repository can read is.
    /// </summary>
    private static readonly string[] Extensions =
    [
        ".md", ".cs", ".ts", ".tsx", ".css", ".yml", ".yaml", ".json",
        ".sql", ".sqlproj", ".csproj", ".slnx", ".mjs", ".svg", ".txt", ".html",
    ];

    /// <summary>
    /// Directories that hold somebody else's text or this build's output. The
    /// walk skips them rather than filtering them afterwards, because
    /// descending into node_modules to throw the result away costs more than
    /// the rest of this test put together.
    /// </summary>
    private static readonly string[] NotOurs =
    [
        "node_modules", "bin", "obj", ".git", "dist", "playwright-report",
        "test-results", "TestResults", "coverage", ".vs", ".idea",
    ];

    private static List<string> TextInThisRepository()
    {
        var found = new List<string>();
        Walk(Repo.Root(), found);
        return found;
    }

    private static void Walk(string directory, List<string> found)
    {
        foreach (string file in Directory.EnumerateFiles(directory))
        {
            if (Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
            {
                found.Add(file);
            }
        }

        foreach (string child in Directory.EnumerateDirectories(directory))
        {
            if (!NotOurs.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase))
            {
                Walk(child, found);
            }
        }
    }
    // #endregion what is scanned

    // #region the rule
    /// <summary>
    /// Every way an em dash can arrive: the character itself, and the two HTML
    /// spellings, which render as one in served markdown and would sail past a
    /// scan looking only for the character.
    ///
    /// <para>Spelled as an escape and as two halves on purpose. A test that
    /// asserts no file here contains these strings cannot contain them, and a
    /// literal in this array would make the suite fail on its own source, which
    /// is a confusing way to learn that the check works.</para>
    /// </summary>
    private static readonly (string Written, string Called)[] EmDashes =
    [
        ("\u2014", "an em dash"),
        ("&" + "mdash;", "an em dash written as an HTML entity"),
        ("&#" + "8212;", "an em dash written as an HTML character reference"),
    ];

    [Fact]
    public void No_file_in_this_repository_contains_an_em_dash()
    {
        var found = new List<string>();
        string root = Repo.Root();

        foreach (string path in TextInThisRepository())
        {
            // Read as UTF-8 explicitly. The reason is the one that made the
            // PowerShell version report phantom hits before it was told: a
            // reader that guesses the encoding turns one multi-byte character
            // into two single-byte ones and then finds whatever it likes in
            // the pieces.
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach ((string written, string called) in EmDashes)
                {
                    if (lines[i].Contains(written, StringComparison.Ordinal))
                    {
                        string where = $"{Path.GetRelativePath(root, path)}:{i + 1}";
                        found.Add($"{where} has {called}: {lines[i].Trim()}");
                    }
                }
            }
        }

        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found));
    }

    /// <summary>
    /// A scan that reads nothing passes, which is the failure this class exists
    /// to make impossible somewhere else, so it is worth an assertion here too.
    /// The number is a floor rather than a count: it moves when the project
    /// grows, and it should never move down by much.
    /// </summary>
    [Fact]
    public void The_scan_actually_reads_the_repository()
    {
        var scanned = TextInThisRepository();
        string ci = Path.Combine(".github", "workflows", "ci.yml");

        Assert.True(scanned.Count > 150, $"only {scanned.Count} files were scanned");
        Assert.Contains(scanned, path => path.EndsWith("CHANGELOG.md", StringComparison.Ordinal));
        Assert.Contains(scanned, path => path.EndsWith("Program.cs", StringComparison.Ordinal));
        Assert.Contains(scanned, path => path.EndsWith(ci, StringComparison.Ordinal));
    }

    /// <summary>
    /// And the rule has to be able to fail, which is not obvious from a test
    /// that asserts an empty list. This runs the same comparison over text that
    /// does break the rule, each way it can be written.
    /// </summary>
    [Theory]
    [InlineData("a sentence \u2014 interrupted")]
    [InlineData("a sentence &" + "mdash; interrupted")]
    [InlineData("a sentence &#" + "8212; interrupted")]
    public void The_rule_catches_a_line_that_breaks_it(string line)
    {
        Assert.Contains(EmDashes, dash => line.Contains(dash.Written, StringComparison.Ordinal));
    }
    // #endregion the rule
}
