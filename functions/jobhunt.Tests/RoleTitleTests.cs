using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The one title test (pouncetitle1120): an engineering role word passes a title, another function's word fails it
/// first, and "Technology", "AI" and "Product" alone carry nothing. The failing evidence is the 11/19 pounce on Delinea
/// (tracker Count 3803, receipt 3804), and the 11/25 cases are the titles the 2 AM pick passed to the 6 AM apply lane
/// (jobs-candidates-2026-11-25.tsv). Both the pick and the pounce refuse them.
/// </summary>
public sealed class RoleTitleTests
{
    private const string Delinea = "Director, People Business Partner - Product, Technology & AI";

    private static readonly DateOnly Today = new(2026, 11, 19);

    private static Candidate Role(string company, string title, string text = "AI agents on Azure. Fully remote.") =>
        new("feed:ashby", company, title, "https://jobs.ashbyhq.com/x/1", "ashby", Today, "Remote", "Remote", 250_000, "feed", text);

    [Theory]
    [InlineData("Director of Engineering, AI")]
    [InlineData("Principal Software Engineer")]
    [InlineData("Staff Engineer")]
    [InlineData("Engineering Manager, Platform")]
    [InlineData("Software Architect")]
    [InlineData("Director, Product Engineering")]
    [InlineData("Staff Product Engineer (Full Stack)")]
    [InlineData("Engineering Manager (People Leader)")]
    [InlineData("Lead .NET Developer")]
    [InlineData("Senior Site Reliability Engineer")]
    public void An_engineering_role_word_with_no_other_function_passes(string title) => Assert.Null(RoleTitle.Failure(title));

    [Theory]
    [InlineData(Delinea, "People Business Partner")]
    [InlineData("Technical Recruiter", "Recruiter")]
    [InlineData("Product Marketing Manager, AI", "Product Marketing")]
    [InlineData("Talent Partner, Engineering", "Talent Partner")]
    [InlineData("Business Development Senior Manager", "Business Development")]
    [InlineData("Manager, Process Engineering - Productivity", "Process Engineering")]
    [InlineData("Director, Business Development", "Business Development")]
    [InlineData("Senior Manager, Business Development", "Business Development")]
    [InlineData("VP of Product (Platform)", "VP of Product")]
    [InlineData("Principal Product Manager (MajorClarity)", "Product Manager")]
    [InlineData("Regional Vice President, US Field Sales (CA)", "Sales")]
    [InlineData("Head of AI Technology", "no engineering role word")]
    public void Another_function_or_no_role_word_fails_with_the_word_that_decided_it(string title, string word) =>
        Assert.Equal(RoleTitle.NotEngineering + word, RoleTitle.Failure(title));

    [Fact]
    public void The_11_19_Delinea_people_partner_fails_the_pick_and_the_pounce()
    {
        var verdict = JobsGates.Judge(Role("Delinea", Delinea), [], [], Today);
        Assert.Equal("not an engineering role: People Business Partner", verdict.Failed);
        Assert.Equal(verdict.Failed, PounceRules.Gate(verdict, [], new HashSet<string>()));
        Assert.False(PounceRules.IsSeat(Delinea));
    }

    [Theory]
    [InlineData("Ripple", "Business Development Senior Manager")]
    [InlineData("ASM", "Manager, Process Engineering - Productivity")]
    [InlineData("ASM", "Director, Business Development")]
    [InlineData("ASM", "Senior Manager, Business Development")]
    [InlineData("Edmentum", "VP of Product (Platform)")]
    [InlineData("Edmentum", "Principal Product Manager (MajorClarity)")]
    [InlineData("Edmentum", "Regional Vice President, US Field Sales (CA)")]
    public void The_seven_11_25_titles_the_pick_passed_now_fail_it(string company, string title)
    {
        var verdict = JobsGates.Judge(Role(company, title), [], [], Today);
        Assert.StartsWith(RoleTitle.NotEngineering, verdict.Failed);
        Assert.Equal(verdict.Failed, PounceRules.Gate(verdict, [], new HashSet<string>()));
    }
}
