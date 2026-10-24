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
}
