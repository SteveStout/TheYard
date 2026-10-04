using System.Text;
using System.Text.RegularExpressions;

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
    /// Transcripts a build writes beside the source. CI keeps these in the
    /// runner's temp directory now, which is the actual fix, and this is the
    /// second line of it: the next person to add a `tee` will not remember why
    /// the first one moved.
    ///
    /// <para>The failure was worth the two defences. The console logger CI asks
    /// for prints each test's name with its parameters, and one of this class's
    /// own parameters is the em dash it forbids, so the transcript being
    /// written by the run contained the character, sat in the repository the
    /// scan reads, and failed the suite on itself. On the runs where the line
    /// was flushed before the scan reached it, and not on the others, which is
    /// what made it look like a flake (ADR: Where a gate lives, addendum).</para>
    /// </summary>
    private static readonly string[] BuildTranscripts =
    [
        "dotnet-output.txt",
        "playwright-output.txt",
    ];

    private static List<string> TextInThisRepository() =>
        Repo.FilesWith(Extensions)
            .Where(path => !BuildTranscripts.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase))
            .ToList();

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

        // Capped. A rewrite that reintroduces the character reintroduces it
        // everywhere at once, and a failure message with four hundred lines in
        // it is one nobody reads to the end of.
        Assert.True(
            found.Count == 0,
            $"{found.Count} lines break the house rule:{Environment.NewLine}"
            + string.Join(Environment.NewLine, found.Take(20)));
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
    /// <summary>
    /// The second house rule the changelog states: every production comment says
    /// what, how and why on its own, so no version number, date or review name
    /// stands in for a reason. Production is the six onion projects and the
    /// frontend under src, without tests or the generated migrations.
    ///
    /// <para>The four allowed lines are examples of a format, not history: a UTC
    /// day the chart reads, the month names and a day as the cost card writes
    /// them, and the opening line of a decision record as the layout matches it.</para>
    /// </summary>
    [Fact]
    public void No_production_comment_leans_on_a_version_a_date_or_a_review()
    {
        string root = Repo.Root();
        string[] production =
        [
            Path.Combine("api", "TheYard.Data"), Path.Combine("api", "TheYard.Domain"),
            Path.Combine("api", "TheYard.Application"), Path.Combine("api", "TheYard.Infrastructure"),
            Path.Combine("api", "TheYard.Infrastructure.Cosmos"), Path.Combine("api", "TheYard.Api"), "src",
        ];
        (string File, string Fragment)[] formatExamples =
        [
            ("activityChart.ts", "`2026-09-13`"),
            ("costWords.ts", "'September',"),
            ("costWords.ts", "\"30 September\""),
            ("docLayout.ts", "shipped as 1.0.0.21."),
        ];
        var history = new Regex(@"1\.0\.\d+\.\d+|September|2026-09|the staff review|the tweaks pass|the self-review");
        var found = new List<string>();

        foreach (string path in Repo.FilesWith(".cs", ".ts", ".tsx", ".css"))
        {
            string relative = Path.GetRelativePath(root, path);
            bool inProduction = production.Any(folder => relative.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal));
            bool generatedOrTest = relative.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || relative.Contains(".test.", StringComparison.Ordinal);
            if (!inProduction || generatedOrTest)
            {
                continue;
            }
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                bool example = formatExamples.Any(allowed =>
                    Path.GetFileName(path) == allowed.File && lines[i].Contains(allowed.Fragment, StringComparison.Ordinal));
                if (!example && history.IsMatch(lines[i]))
                {
                    found.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(
            found.Count == 0,
            $"{found.Count} production lines lean on a version, a date or a review:{Environment.NewLine}"
            + string.Join(Environment.NewLine, found.Take(20)));
    }
    // #endregion the rule
}
