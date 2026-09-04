using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The windows that are cheap to check (ADR: Broken windows, and the rule that
/// answers them).
///
/// <para>A broken window is not a bug. It is a small visible piece of disrepair
/// that tells the next reader nobody is watching, and the reason to care about
/// it is what it licenses rather than what it costs. This class holds the
/// handful that a machine can see. The ones that matter more, a comment that
/// has outlived its premise or a check that asks an easier question than the
/// one it was written for, are not in here, because no regular expression finds
/// them.</para>
///
/// <para>Every assertion here passes today. That is the point of writing them:
/// the first one is free and the fiftieth is a rewrite.</para>
/// </summary>
public class BrokenWindowsTests
{
    private static readonly string[] Source = [".cs", ".ts", ".tsx", ".css"];

    /// <summary>
    /// A finding as a reader would want it: the file, the line, and the line.
    /// </summary>
    private static List<string> Lines(Func<string, bool> ofInterest, params string[] extensions)
    {
        string root = Repo.Root();
        var found = new List<string>();

        foreach (string path in Repo.FilesWith(extensions))
        {
            string relative = Path.GetRelativePath(root, path);
            // This file names every marker it forbids, so it cannot be one of
            // the files it reads. HouseVoiceTests solves the same problem by
            // spelling what it forbids in halves; here the file is the
            // exception, which is simpler to read and just as honest as long as
            // it is said out loud.
            if (relative.EndsWith("BrokenWindowsTests.cs", StringComparison.Ordinal))
            {
                continue;
            }

            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                if (ofInterest(lines[i]))
                {
                    found.Add($"{relative}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        return found;
    }

    private static void NoneOf(List<string> found, string what) =>
        Assert.True(
            found.Count == 0,
            $"{found.Count} {what}:{Environment.NewLine}"
            + string.Join(Environment.NewLine, found.Take(20)));

    // #region markers
    /// <summary>
    /// A marker is a note to somebody who is not coming. This repository has
    /// never had one, and the reason is not discipline: a thing worth doing
    /// later is a line in a record, where it has a reason and a reader, and a
    /// thing not worth doing is not worth a comment either.
    /// </summary>
    [Fact]
    public void No_source_file_carries_a_marker_for_work_that_is_not_happening()
    {
        var markers = new Regex(@"\b(TODO|FIXME|HACK|XXX)\b");
        NoneOf(Lines(line => markers.IsMatch(line), Source), "markers left in the source");
    }

    /// <summary>
    /// A focused test is the most expensive broken window available, because
    /// the suite goes greener rather than redder: one test runs, everything
    /// else is silently skipped, and the run page says it passed. Both runners
    /// are configured to refuse it on CI; this catches it a step earlier, on
    /// the machine that wrote it.
    /// </summary>
    [Fact]
    public void No_test_is_focused_or_skipped()
    {
        var focused = new Regex(@"\b(test|it|describe)\.(only|skip)\s*\(|Skip\s*=\s*""");
        NoneOf(Lines(line => focused.IsMatch(line), Source), "focused or skipped tests");
    }

    /// <summary>
    /// A left-behind debugger statement stops a browser on somebody else's
    /// machine, and console output on a page whose whole error story is that a
    /// crash reaches the same ring the server's crashes do is a report going
    /// somewhere nobody reads.
    /// </summary>
    [Fact]
    public void The_browser_code_does_not_log_to_the_console_or_stop_at_a_debugger()
    {
        string root = Repo.Root();
        var noisy = new Regex(@"\bconsole\.[a-z]+\(|^\s*debugger;");
        var found = Repo.FilesWith(".ts", ".tsx")
            .Where(path => Path.GetRelativePath(root, path)
                .StartsWith("src" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .SelectMany(path => File.ReadAllLines(path)
                .Select((line, index) => (line, index))
                .Where(entry => noisy.IsMatch(entry.line))
                .Select(entry => $"{Path.GetRelativePath(root, path)}:{entry.index + 1}: {entry.line.Trim()}"))
            .ToList();

        NoneOf(found, "console calls or debugger statements under src/");
    }
    // #endregion markers

    // #region the runners agree
    /// <summary>
    /// Playwright allows a focused test by default, so it is told not to on CI.
    /// </summary>
    [Fact]
    public void The_browser_runner_refuses_a_focused_test_on_ci() =>
        Assert.Contains(
            "forbidOnly: !!process.env.CI",
            File.ReadAllText(Path.Combine(Repo.Root(), "playwright.config.ts")),
            StringComparison.Ordinal);

    /// <summary>
    /// Vitest already refuses one on CI, because its allowOnly defaults to
    /// !process.env.CI. So the check is not that the rule is written down, it
    /// is that nothing turns it off. Writing the default out explicitly would
    /// need @types/node in a project that has never needed it, for one boolean,
    /// which is a dependency bought to restate something already true.
    /// </summary>
    [Fact]
    public void The_unit_runner_is_never_told_to_allow_one()
    {
        string config = File.ReadAllText(Path.Combine(Repo.Root(), "vite.config.ts"));

        Assert.DoesNotContain("allowOnly: true", config, StringComparison.Ordinal);
        Assert.DoesNotContain("allowOnly:true", config, StringComparison.Ordinal);
    }
    // #endregion the runners agree
}
