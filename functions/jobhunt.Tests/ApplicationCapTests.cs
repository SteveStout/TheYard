using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// RULE 50 as code: three applications to one company in a rolling 60 days, the do-not-apply list read
/// out of systemRules.md, and a company's variants counted as the company (the Upstart Network hole).
/// Made-up rows and companies, except the rule's own shape.
/// </summary>
public sealed class ApplicationCapTests
{
    private static readonly DateOnly Today = new(2026, 10, 22);

    private static string[] App(int count, string company, string date) =>
        [count.ToString(System.Globalization.CultureInfo.InvariantCulture), "NA", company, "Greenhouse", date, "", "SUBMITTED by Claude: a role"];

    private const string Rules = """
        **DO-NOT-APPLY LIST, kept here because this is where the check runs.**
        | Company | Do not apply before | Why |
        |---|---|---|
        | Acme (also "Acme Network") | 2026-12-20 | over the cap |
        | Globex | 2026-10-01 | an old hold, past |

        Steve's words: something else.
        """;

    [Fact]
    public void The_list_is_read_with_its_variants_and_dates()
    {
        var list = ApplicationCap.DoNotApplyList(Rules);
        Assert.Equal(2, list.Count);
        Assert.Equal(["Acme", "Acme Network"], list[0].Names);
        Assert.Equal(new DateOnly(2026, 12, 20), list[0].Until);
    }

    [Fact]
    public void Three_in_sixty_days_holds_the_fourth_until_the_oldest_ages_out()
    {
        var master = new List<string[]> { App(1, "Initech", "9/1/2026"), App(2, "Initech", "10/20/2026"), App(3, "Initech Inc", "10/21/2026"), App(4, "Initech", "6/1/2026") };
        var verdict = ApplicationCap.Check("Initech", master, [], Today);
        Assert.False(verdict.Allowed);
        Assert.Equal(new DateOnly(2026, 10, 31), verdict.OpensOn);
        Assert.Contains("Counts 1, 2, 3", verdict.Why, StringComparison.Ordinal);
        Assert.True(ApplicationCap.Check("Initech", master, [], new DateOnly(2026, 10, 31)).Allowed);
    }

    [Fact]
    public void A_variant_spelling_counts_as_the_company_and_the_list_outranks_the_count()
    {
        var list = ApplicationCap.DoNotApplyList(Rules);
        Assert.False(ApplicationCap.Check("Acme Network", [], list, Today).Allowed);
        Assert.True(ApplicationCap.Check("Globex", [], list, Today).Allowed);
        Assert.True(ApplicationCap.Same("Upstart", "Upstart Network"));
        Assert.False(ApplicationCap.Same("Upstart", "Upstarter"));
    }

    [Fact]
    public void Messages_and_invites_do_not_count_and_the_card_lists_who_is_held()
    {
        string[] invite = ["9", "Jane", "Initech", "LinkedIn", "10/21/2026", "", "Connection request sent with a note"];
        var master = new List<string[]> { App(1, "Initech", "10/1/2026"), App(2, "Initech", "10/20/2026"), invite, invite };
        Assert.True(ApplicationCap.Check("Initech", master, [], Today).Allowed);
        var held = ApplicationCap.Held([.. master, App(3, "Initech", "10/21/2026")], ApplicationCap.DoNotApplyList(Rules), Today);
        Assert.Equal(["Acme", "Initech"], held.Select(h => h.Company));
    }

    // The openings of real rows: Foundation Finance Counts 3475 and 3481 (11/5) and PointClickCare Count 3566 (11/11) were
    // never sent, and the ServiceTitan row is a real application whose note mentions an earlier draft further in.
    private const string NotAppliedBlocker = "NOT APPLIED, BLOCKER (chat1105) 11/5, on J:\\APPLY-BLOCKERS-2026-11-05.md. #5 Software Architect, remote (MO allowed). iCIMS login/account (his).";
    private const string NotAppliedYet = "NOT APPLIED YET (chat1105) 11/5. iCIMS Candidate Profile creates an account (password x2 + hCaptcha), so it is his.";
    private const string NotSubmitted = "NOT SUBMITTED (chat1111): Senior AI Architect (US), Lever, Remote USA, no band. Form requires sworn history answers.";
    private const string AppliedWithAnEarlierDraft = "APPLIED 9/28 ~14:55 CDT, applylane with Steve. Senior Software Engineer (Exteriors squad, second req), Remote US. "
        + "Workday account already open from the first req, resume uploaded, every question answered and read back on the review page before the click; "
        + "the earlier draft on this req sat NOT SUBMITTED for a day.";

    [Fact]
    public void A_row_that_says_not_applied_or_not_submitted_is_not_an_application()
    {
        string[] Row(int count, string company, string note) => [count.ToString(System.Globalization.CultureInfo.InvariantCulture), "", company, "Company Website", "11/5/2026", "", note];
        var master = new List<string[]>
        {
            Row(3475, "Foundation Finance", NotAppliedBlocker), Row(3481, "Foundation Finance", NotAppliedYet), Row(3566, "Foundation Finance", NotSubmitted),
            Row(2220, "ServiceTitan", AppliedWithAnEarlierDraft),
        };
        Assert.False(TrackerRows.IsApplication(master[0]));
        Assert.False(TrackerRows.IsApplication(master[1]));
        Assert.False(TrackerRows.IsApplication(master[2]));
        Assert.True(TrackerRows.IsApplication(master[3]));
        Assert.Empty(ApplicationCap.Applications("Foundation Finance", master, new DateOnly(2026, 11, 12)));
        Assert.Empty(ApplicationCap.Held(master, [], new DateOnly(2026, 11, 12)));
    }
}
