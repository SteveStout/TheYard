using System.Text;

namespace TheYard.Tests;

/// <summary>
/// The house rule that every production comment says what, how and why on its own, so that nothing
/// in it stands in for a reason: not a date, not a version number, not a review by its name, not a
/// note about who asked.
///
/// <para>It lived beside the em dash scan while it was one fact, and it is a file of its own from
/// the version that gave it a rule body and a second area to ask about. The body is
/// `tests/rules/HistoryRule.cs`, compiled into both test projects by a link, so what this suite
/// refuses and what `.github/quality.py` counts cannot drift into describing two codebases.</para>
///
/// <para>The rule is asked in two strengths, because the same word is history in one part of this
/// repository and the subject in another. The site's own code has no reason to write a month name or
/// the owner's first name, so there both are provenance. The job hunt's Function reads his inbox,
/// his calendar and his tracker, and its clock turns over when the clocks do, so there his name and
/// the word November are what the code is about. What is provenance in both is a date, a version
/// number, a named review, and his name beside a date or beside an ask.</para>
/// </summary>
public class ProductionCommentTests
{
    /// <summary>
    /// The first area: the site's own code, the six onion projects and the frontend
    /// under src, without tests or the generated migrations, asked at the full
    /// strength. A bare month name and a bare first name are provenance here,
    /// because nothing the site itself does has a reason to write either one.
    /// </summary>
    [Fact]
    public void No_production_comment_leans_on_a_version_a_date_or_a_review()
    {
        string[] production =
        [
            Path.Combine("api", "TheYard.Data"), Path.Combine("api", "TheYard.Domain"),
            Path.Combine("api", "TheYard.Application"), Path.Combine("api", "TheYard.Infrastructure"),
            Path.Combine("api", "TheYard.Infrastructure.Cosmos"), Path.Combine("api", "TheYard.Api"), "src",
        ];

        var found = HistoryUnder(production, siteStrength: true);

        Assert.True(
            found.Count == 0,
            $"{found.Count} production lines lean on a version, a date or a review:{Environment.NewLine}"
            + string.Join(Environment.NewLine, found.Take(20)));
    }

    /// <summary>
    /// The second area: the job hunt's Function, which this rule reached by no
    /// test at all until now. That gap is the whole reason the comments there had
    /// drifted into explaining by date and by who asked, so closing it is the fix
    /// and rewriting them is only the arrears.
    ///
    /// <para>Asked at the lower strength, and the reason is in the code rather
    /// than in a preference. The job hunt reads his inbox, his calendar and his
    /// tracker, so his name there is what the code is about and not a note about
    /// who wanted it; and its clock turns over when the clocks do, so the word
    /// November is the calendar. What is still provenance is a date, a version
    /// number, a named review, and his name beside a date or beside an ask.</para>
    ///
    /// <para>The sample under `samples/` is not here, because this suite does not
    /// walk it: a sample is its own solution with its own gate and its own copies
    /// of these rules (`Repo.NotOurs`). It is not unguarded for that, because the
    /// ratchet's own count reads every production file including The Shed's, so a
    /// comment of this shape added there turns that number red instead.</para>
    /// </summary>
    [Fact]
    public void No_comment_in_the_job_hunt_leans_on_provenance()
    {
        string[] production = ["functions"];

        var found = HistoryUnder(production, siteStrength: false);

        Assert.True(
            found.Count == 0,
            $"{found.Count} lines explain by a date, a version or who asked:{Environment.NewLine}"
            + string.Join(Environment.NewLine, found.Take(30)));
    }

    /// <summary>
    /// Every line under these folders whose comment leans on provenance, named
    /// with its file and line so a failure reads like a to-do list. Tests, the
    /// generated migrations and the experiment are not production.
    /// </summary>
    private static List<string> HistoryUnder(string[] production, bool siteStrength)
    {
        string root = Repo.Root();
        var found = new List<string>();

        foreach (string path in Repo.FilesWith(".cs", ".ts", ".tsx", ".css"))
        {
            string relative = Path.GetRelativePath(root, path);
            bool inProduction = production.Any(folder => relative.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal));
            // A test project by its folder and not by the word anywhere in the path: `TestsCard.tsx` is
            // production, and a rule that read the word alone would stop looking at it.
            bool generatedOrTest = relative.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || relative.Contains(".test.", StringComparison.Ordinal)
                || relative.Contains($"Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || relative.Contains($"Experiment{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
            if (!inProduction || generatedOrTest)
            {
                continue;
            }
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            foreach ((int line, string text) in TheYard.Rules.HistoryRule.HistoryIn(path, lines, siteStrength))
            {
                found.Add($"{relative}:{line}: {text}");
            }
        }

        return found;
    }

    [Theory]
    [InlineData("    // the addendum of 19 October")]
    [InlineData("    /// read live on 18 October 2026")]
    [InlineData("    // shipped on 2026-10-21")]
    [InlineData("    // Steve asked for it")]
    [InlineData("    * the review of 1.0.4.11 found it")]
    [InlineData("    // in December, the cap")]
    public void The_widened_rule_catches_history_in_a_comment(string line)
    {
        Assert.True(TheYard.Rules.HistoryRule.LeansOnHistory("Anything.cs", line, siteStrength: true));
    }

    [Theory]
    [InlineData("    /** May be empty for vehicles with no reported damage. */")]
    [InlineData("    /** A day as a person reads it: \"30 September\". */")]
    [InlineData("    // a UTC day, `2026-09-13`, read as that day")]
    [InlineData("    const label = 'October';")]
    public void The_widened_rule_leaves_a_format_example_and_code_alone(string line)
    {
        Assert.False(TheYard.Rules.HistoryRule.LeansOnHistory("Anything.cs", line, siteStrength: true));
    }

    /// <summary>
    /// The shapes that are provenance wherever they are written, so the lower
    /// strength is still a rule and not a shrug. Each of these is one of the
    /// ways the job hunt's comments had come to explain themselves.
    /// </summary>
    [Theory]
    [InlineData("    /// the goal he named on 2026-10-23: no alert reaches him")]
    [InlineData("    /// reported, as Rule 46 has it (Steve 2026-10-02 12:33)")]
    [InlineData("    /// the laptop's was unregistered on 23 Oct.")]
    [InlineData("    /// code answering (ADR-098, the addendum of 1.0.4.107).")]
    [InlineData("    /// change only with Steve's yes (10/24 12:22): v2 is")]
    [InlineData("    /// the 9/19 rule that every asset comes from our domain")]
    [InlineData("    /// which the campaign never writes to (the review of the")]
    public void The_lower_strength_still_catches_provenance(string line)
    {
        Assert.True(TheYard.Rules.HistoryRule.LeansOnHistory("Anything.cs", line, siteStrength: false));
    }

    /// <summary>
    /// And what the lower strength deliberately leaves alone: the person the job
    /// hunt acts for, the month the clocks change in, a date that spells its year
    /// out because it is naming a fixed point, and a format quoted as a sample.
    /// </summary>
    [Theory]
    [InlineData("    /// <summary>Sends one email from Steve's Gmail.</summary>")]
    [InlineData("    /// both sides of the November clock change.")]
    [InlineData("    // 1 January 1970 UTC, because that is what a browser uses")]
    [InlineData("    /// a day as `2026-10-12` or an ISO instant")]
    public void The_lower_strength_leaves_the_subject_alone(string line)
    {
        Assert.False(TheYard.Rules.HistoryRule.LeansOnHistory("Anything.cs", line, siteStrength: false));
    }
}
