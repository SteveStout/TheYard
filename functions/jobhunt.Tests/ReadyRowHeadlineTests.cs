using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The resume headline band: build_tailored_resume.py's own cleaning of a posting title and its 48-character limit, so a
/// READY row whose headline will not build goes to NEEDS STEVE at pick time with "headline: N characters". The titles
/// and their cleaned headlines are the builder's own answers to --clean.
/// </summary>
public sealed class ReadyRowHeadlineTests
{
    [Theory]
    [InlineData("Senior Engineering Manager (Patient New Verticals)", "SENIOR ENGINEERING MANAGER (PATIENT NEW VERTICALS)")]
    [InlineData("Staff / Principal Software Engineer – Cloud Platform & SaaS", "STAFF / PRINCIPAL SOFTWARE ENGINEER")]
    [InlineData("Senior Software Engineer, Java | SaaS Backup", "SENIOR SOFTWARE ENGINEER")]
    [InlineData("Staff Engineer (Remote)", "STAFF ENGINEER")]
    [InlineData("Technical Lead Manager", "TECHNICAL LEAD MANAGER")]
    [InlineData("Principal Software Engineer (Remote - US)", "PRINCIPAL SOFTWARE ENGINEER")]
    [InlineData("Staff Software Engineer, Platform, St. Louis, MO", "STAFF SOFTWARE ENGINEER")]
    [InlineData("Lead .NET Engineer - R12345", "LEAD .NET ENGINEER")]
    [InlineData("REQ-2231 - Principal Engineer", "PRINCIPAL ENGINEER")]
    [InlineData("Engineering Manager [Hybrid]", "ENGINEERING MANAGER")]
    [InlineData("Software Architect (C#/.NET) #4471", "SOFTWARE ARCHITECT (C#/.NET)")]
    [InlineData("Director of Engineering; Payments", "DIRECTOR OF ENGINEERING")]
    [InlineData("Staff Engineer · Remote", "STAFF ENGINEER")]
    [InlineData("Principal Architect (Azure Integration Services and Developer Experience)", "PRINCIPAL ARCHITECT (AZURE INTEGRATION SERVICES AND DEVELOPER EXPERIENCE)")]
    [InlineData("Sr. Manager, Software Engineering", "SR. MANAGER")]
    [InlineData("Lead Engineer (L5)", "LEAD ENGINEER")]
    [InlineData("Principal Engineer, Data (Hybrid, NYC)", "PRINCIPAL ENGINEER")]
    [InlineData("Staff Product Engineer (Full Stack)", "STAFF PRODUCT ENGINEER (FULL STACK)")]
    [InlineData("Senior Software Engineer II - Backend, Payments (US Remote)", "SENIOR SOFTWARE ENGINEER II")]
    public void A_title_cleans_to_the_headline_the_builder_prints(string title, string headline) =>
        Assert.Equal(headline, ResumeHeadline.Clean(title));

    [Fact]
    public void A_headline_past_the_builder_s_48_characters_is_held_with_its_length_and_one_that_fits_is_not()
    {
        Assert.Equal(48, ResumeHeadline.MaxChars);
        Assert.Equal("headline: 50 characters", ResumeHeadline.Hold("Senior Engineering Manager (Patient New Verticals)"));
        Assert.Equal("headline: 73 characters", ResumeHeadline.Hold("Principal Architect (Azure Integration Services and Developer Experience)"));
        Assert.Null(ResumeHeadline.Hold("Staff / Principal Software Engineer – Cloud Platform & SaaS"));
        Assert.Null(ResumeHeadline.Hold(new string('A', 48)));
        Assert.Equal("headline: 49 characters", ResumeHeadline.Hold(new string('A', 49)));
        Assert.Equal("headline: a character outside ASCII", ResumeHeadline.Hold("Ingénieur logiciel principal"));
        Assert.Equal("headline: nothing left once the codes and locations are out", ResumeHeadline.Hold("(Remote)"));
    }

    [Fact]
    public void Headway_s_saved_posting_is_held_off_READY_with_its_headline_length_in_the_candidates_file()
    {
        var board = Assert.Single(PublicBoardReader.Read("ashby", File.ReadAllText(ReadyRowFixtures.Path("ashby_headway_9ef73ae3.json"))));
        var headway = new Candidate("feed:ashby", "Headway", board.Title, board.Url, "ashby", board.Posted, board.Location,
            PostingRules.Stance(board.Workplace, board.Location, board.IsRemote), board.CompMax, "feed", board.Text + " C# on Azure");
        var passed = JobsGates.Judge(headway, [], [], new DateOnly(2026, 11, 9));
        var fits = JobsGates.Judge(headway with { Title = "Engineering Manager" }, [], [], new DateOnly(2026, 11, 9));

        Assert.Null(passed.Failed);
        Assert.Equal(50, board.Title.Length);
        Assert.Equal("headline: 50 characters", passed.Hold);
        Assert.Equal("", fits.Hold);
        string[] lines = JobsFiles.Write("s", [passed], []).Split('\n');
        int hold = JobsFiles.Columns.ToList().IndexOf("Hold");
        Assert.Equal("headline: 50 characters", lines[2].Split('\t')[hold]);
    }
}
