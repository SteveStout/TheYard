using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The run page has to say which test failed, and these hold it to that.
///
/// <para>CI turns a red suite into GitHub annotations by grepping the suite's
/// own output. Annotations are the part a reader without a sign-in to this
/// repository can see, so they are the only public account of a failure
/// (ADR: The exemption that hid a contrast failure, addendum). The weakness of
/// that design is that a pattern which matches nothing fails silently: the
/// step is green, the annotation list is empty, and the run page says
/// "Process completed with exit code 1" exactly as it did before the work.
/// That has now happened twice. The first time the pattern wanted
/// <c>Passed!</c> at the start of a line and dotnet had indented it; the
/// second time the console logger this job asks for did not print that line
/// at all and printed a four-line block instead.</para>
///
/// <para>So the patterns are read out of <c>ci.yml</c> and run against real
/// transcripts kept in <c>Fixtures/</c>: a failing .NET run and a failing
/// browser run, trimmed but otherwise as the suites wrote them. If a pattern
/// stops naming the failure, this suite goes red on a machine somebody is
/// watching rather than on a run page nobody can read.</para>
/// </summary>
public class CiAnnotationTests
{
    // #region reading the workflow
    /// <summary>
    /// A grep in the workflow, as the pattern it applies and the file it
    /// reads. Both are on one line in <c>ci.yml</c>, which is what makes
    /// pairing them safe.
    /// </summary>
    /// <summary>
    /// A grep in the workflow: the pattern it applies, the file it reads, and
    /// how many lines it keeps after a match. The last one matters because the
    /// marker is the label and the detail is the line under it.
    /// </summary>
    private sealed record WorkflowGrep(string Pattern, string Reads, int After);

    /// <summary>
    /// The transcripts live in the runner's temp directory rather than in the
    /// checkout, because a suite that reads every file in the repository will
    /// otherwise read the transcript of the run it is part of. So the path is
    /// optional here and only the file's own name is kept.
    /// </summary>
    private static readonly Regex GrepLine = new(
        @"grep -E (?<flags>(?:-[A-Za-z] \d+ )*)'(?<pattern>[^']*)'\s+""?(?:\$RUNNER_TEMP/)?(?<file>[a-z-]+\.txt)""?",
        RegexOptions.Compiled);

    private static IReadOnlyList<WorkflowGrep> GrepsIn(string workflow) =>
        GrepLine.Matches(workflow)
            .Select(m => new WorkflowGrep(
                m.Groups["pattern"].Value,
                m.Groups["file"].Value,
                After(m.Groups["flags"].Value)))
            .ToList();

    private static int After(string flags)
    {
        var trailing = Regex.Match(flags, @"-A (\d+)");
        return trailing.Success ? int.Parse(trailing.Groups[1].Value) : 0;
    }

    /// <summary>
    /// grep speaks POSIX character classes and .NET does not. Only the two
    /// this workflow uses are translated, and anything else is refused rather
    /// than quietly compiled into a pattern that means something different.
    /// </summary>
    private static Regex AsDotNet(string posix)
    {
        string translated = posix
            .Replace("[[:space:]]", @"\s", StringComparison.Ordinal)
            .Replace("[[:digit:]]", @"\d", StringComparison.Ordinal);

        Assert.DoesNotContain("[[:", translated, StringComparison.Ordinal);
        return new Regex(translated);
    }

    private static string Workflow() =>
        File.ReadAllText(Path.Combine(Repo.Root(), ".github", "workflows", "ci.yml"));

    private static string[] Fixture(string name) =>
        File.ReadAllLines(Path.Combine(Repo.Root(), "api", "TheYard.Tests", "Fixtures", name));

    /// <summary>
    /// Every line of the transcript that at least one of the workflow's
    /// patterns picks up. This is what the run page would carry.
    /// </summary>
    private static List<string> Reported(string transcript, string fixtureName)
    {
        var greps = GrepsIn(Workflow()).Where(g => g.Reads == transcript).ToList();
        Assert.NotEmpty(greps);

        string[] lines = Fixture(fixtureName);
        var reported = new List<string>();
        foreach (var grep in greps)
        {
            var pattern = AsDotNet(grep.Pattern);
            for (int i = 0; i < lines.Length; i++)
            {
                if (!pattern.IsMatch(lines[i]))
                {
                    continue;
                }

                // The match, and the lines grep would keep after it.
                for (int kept = i; kept <= Math.Min(lines.Length - 1, i + grep.After); kept++)
                {
                    reported.Add(lines[kept]);
                }
            }
        }

        return reported;
    }
    // #endregion reading the workflow

    // #region what a reader needs
    /// <summary>
    /// The patterns are only held to anything if this test can find them, and
    /// it finds them by expecting the pattern and the file it reads to sit on
    /// one line. A future edit that wraps one across two lines would leave this
    /// suite green while testing less than it says it does, so the count of
    /// greps parsed is held to the count of greps present.
    /// </summary>
    [Fact]
    public void Every_grep_over_a_suite_transcript_is_one_this_test_can_read()
    {
        string workflow = Workflow();
        int present = Regex.Matches(workflow, @"grep -E .*\.txt""?").Count;

        Assert.Equal(present, GrepsIn(workflow).Count);
    }

    /// <summary>
    /// Without the test's name a red run says only that something broke, and
    /// without the assertion message the name alone rarely says why.
    /// </summary>
    [Theory]
    [InlineData("Failed TheYard.Tests.AdminObservabilityTests.A_statement_names_the_request_that_caused_it")]
    [InlineData("Assert.Contains() Failure")]
    // The line under the message, which is the one somebody actually needs: the
    // assertion says which assertion, and this says what was in the collection.
    [InlineData("Collection: []")]
    [InlineData("Stack Trace:")]
    [InlineData("Test Run Failed.")]
    [InlineData("Failed: 1")]
    public void A_failing_dotnet_run_says_this_much_on_the_run_page(string needed)
    {
        var reported = Reported("dotnet-output.txt", "dotnet-suite-failure.txt");
        Assert.Contains(reported, line => line.Contains(needed, StringComparison.Ordinal));
    }

    /// <summary>
    /// The browser suite's two failure shapes: an assertion that did not hold,
    /// and a test that ran out of time. The second says nothing about an
    /// expectation, so its own headline is the whole finding.
    /// </summary>
    [Theory]
    [InlineData("a11y.spec.ts:22:1")]
    [InlineData("Locator: locator('article h3 button').first()")]
    [InlineData("Test timeout of 90000ms exceeded.")]
    [InlineData("the room is raising faster than the page can answer")]
    public void A_failing_browser_run_says_this_much_on_the_run_page(string needed)
    {
        var reported = Reported("playwright-output.txt", "playwright-suite-failure.txt");
        Assert.Contains(reported, line => line.Contains(needed, StringComparison.Ordinal));
    }
    // #endregion what a reader needs

    // #region what a reader does not need
    /// <summary>
    /// An annotation list padded with passing tests and application logging is
    /// a list nobody reads, and GitHub shows only the first handful of them.
    /// </summary>
    [Fact]
    public void The_passing_tests_and_the_application_log_stay_out_of_it()
    {
        var reported = Reported("dotnet-output.txt", "dotnet-suite-failure.txt");

        Assert.DoesNotContain(reported, line => line.Contains("  Passed TheYard.Tests.", StringComparison.Ordinal));
        Assert.DoesNotContain(reported, line => line.Contains("\"LogLevel\":", StringComparison.Ordinal));
        Assert.DoesNotContain(reported, line => line.Contains("Discovering:", StringComparison.Ordinal));
    }

    /// <summary>
    /// Same for the browser suite: forty passing tests scroll the two failures
    /// off the top of the page.
    /// </summary>
    [Fact]
    public void The_passing_specs_stay_out_of_the_browser_annotations()
    {
        var reported = Reported("playwright-output.txt", "playwright-suite-failure.txt");

        Assert.DoesNotContain(reported, line => line.Contains("smoke.spec.ts:110:1", StringComparison.Ordinal));
    }
    // #endregion what a reader does not need

    // #region the annotations have to survive the trip
    /// <summary>
    /// An annotation is one line. A carriage return or a newline inside one
    /// truncates it at that point, and dotnet on Windows writes CRLF, so the
    /// workflow strips them; this holds it to that.
    /// </summary>
    [Fact]
    public void Every_annotation_is_stripped_of_carriage_returns_before_it_is_printed()
    {
        string workflow = Workflow();
        int printers = Regex.Matches(workflow, @"printf '::error::%s\\n'").Count;
        int strippers = Regex.Matches(workflow, @"tr -d '\\r'").Count;

        Assert.True(printers > 0, "the workflow prints no annotations at all");
        Assert.Equal(printers, strippers);
    }
    // #endregion the annotations have to survive the trip
}
