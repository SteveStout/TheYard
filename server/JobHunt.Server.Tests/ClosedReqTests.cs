namespace JobHunt.Server.Tests;

/// <summary>The closed-req gate over rows in the shape of the real ones: Gerner's rejected req, McCabe's live one, the three on the first morning's worklist.</summary>
public sealed class ClosedReqTests
{
    private const string Aledade = "I applied for the Remote Senior Software Engineer II, Risk role at Aledade. My work: x";

    private static string[] Row(string count, string company, string channel, string date, string note) =>
        [count, "NA", company, channel, date, string.Empty, note];

    private static readonly string[][] Rows =
    [
        Row("3140", "Aledade PBC (Staff Software Engineer - AI Platform)", "Company Website", "10/26/2026", "[bookkeeping1027-1600 shape fix: Date rewritten] SUBMITTED by Steve 7:51 AM CDT. Lever aledade b0d98dcb"),
        Row("3141", "Aledade PBC (Senior Software Engineer II - Risk)", "Company Website", "10/26/2026", "[bookkeeping1027-1600 shape fix: Date rewritten] SUBMITTED 8:02 AM CDT, Lever aledade 5810c2b9"),
        Row("3212", "Aledade", "Lever", "10/28/2026", "REJECTION, generic, 10/28/2026 10:01 CDT: Senior Software Engineer II - Risk, applied 10/26/2026 (tracker row 3141)."),
        Row("2857", "ServiceTitan", "Workday", "10/19/2026", "SUBMITTED 14:3x CDT 10/19: Senior Staff Software Engineer, req JR110561, US Remote"),
        Row("2802", "ServiceTitan", "Email", "2026-10-17", "REJECTION 4:53 AM CDT 10/17: Manager, Software Engineering - Contact Center Pro, JR114680 (applied Count 1984)"),
        Row("2819", "Frontline Education", "Jobvite (Talemetry)", "2026-10-17", "APPLICATION SUBMITTED BY CLAUDE 10/17. Principal Software Engineer, Back-End .NET, req 17890165, Remote USA"),
        Row("3139", "AHEAD (Software Engineering Manager - Foundry)", "Company Website", "10/26/2026", "[bookkeeping1027-1600 shape fix: Date rewritten] SUBMITTED by Steve 7:41 AM CDT, Lever thinkahead e59a818b"),
    ];

    [Fact]
    public void The_req_a_rejection_names_is_closed()
    {
        string? why = ClosedReqs.Why("Aledade", Aledade, Rows);
        Assert.Contains("closed req", why, StringComparison.Ordinal);
        Assert.Contains("3212", why, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_req_at_the_company_stays_open_when_the_rejection_cites_the_other_one()
    {
        Assert.Null(ClosedReqs.Why("Aledade", "I applied for the Remote Staff Software Engineer, AI Platform role at Aledade.", Rows));
    }

    [Theory]
    [InlineData("ServiceTitan", "I applied for the Remote Senior Staff Software Engineer (JR110561) role at ServiceTitan.")]
    [InlineData("Frontline Education", "I applied for the Remote Principal Software Engineer, Back-End .NET (17890165) role at Frontline Education.")]
    [InlineData("AHEAD", "I applied for the Remote Software Engineering Manager, Foundry role at AHEAD.")]
    public void A_live_application_passes(string company, string text)
    {
        Assert.Null(ClosedReqs.Why(company, text, Rows));
    }

    [Fact]
    public void A_rejection_dated_before_the_application_is_about_something_else()
    {
        Assert.Null(ClosedReqs.Why("ServiceTitan", "I applied for the Remote Senior Staff Software Engineer (JR110561) role at ServiceTitan.", Rows));
    }

    [Fact]
    public void A_rejection_that_names_no_req_holds_the_block()
    {
        string[][] rows = [.. Rows, Row("3300", "AHEAD", "Email", "10/30/2026", "REJECTION, generic, 10/30/2026: the email names no role.")];
        Assert.Contains("closed req", ClosedReqs.Why("AHEAD", "I applied for the Remote Software Engineering Manager, Foundry role at AHEAD.", rows), StringComparison.Ordinal);
    }

    [Fact]
    public void A_rejection_on_a_row_of_several_companies_reaches_each_of_them()
    {
        string[][] rows = [.. Rows, Row("3301", "WellSky / Frontline Education", "Email", "10/30/2026", "REJECTIONS, generic, both logged.")];
        Assert.NotNull(ClosedReqs.Why("Frontline Education", "I applied for the Remote Principal Software Engineer, Back-End .NET (17890165) role at Frontline Education.", rows));
    }

    [Theory]
    [InlineData("POSITION FILLED per the recruiter")]
    [InlineData("[bookkeeping shape fix] REJECTED by the hiring manager")]
    [InlineData("WITHDRAWN BY Steve on 10/30")]
    public void Each_word_for_a_closing_counts_past_any_shape_fix_prefix(string note)
    {
        string[][] rows = [.. Rows, Row("3302", "AHEAD", "Email", "10/30/2026", note)];
        Assert.NotNull(ClosedReqs.Why("AHEAD", "I applied for the Remote Software Engineering Manager, Foundry role at AHEAD.", rows));
    }

    [Fact]
    public void A_text_that_says_he_applied_with_no_application_on_file_is_held()
    {
        Assert.Contains("no application on file", ClosedReqs.Why("Nowhere", "I applied for the Remote Staff Engineer role at Nowhere.", Rows), StringComparison.Ordinal);
    }

    [Fact]
    public void A_row_that_says_not_applied_is_no_application()
    {
        string[][] rows = [Row("1", "Nowhere", "Greenhouse", "10/1/2026", "NOT APPLIED, screened out: Staff Engineer")];
        Assert.Contains("no application on file", ClosedReqs.Why("Nowhere", "I applied for the Remote Staff Engineer role at Nowhere.", rows), StringComparison.Ordinal);
    }

    [Fact]
    public void A_linkedin_note_that_mentions_applying_is_no_application()
    {
        string[][] rows = [Row("2", "Nowhere", "LinkedIn", "10/1/2026", "CONNECTION REQUEST SENT, note says I applied for the Staff Engineer role")];
        Assert.Contains("no application on file", ClosedReqs.Why("Nowhere", "I applied for the Remote Staff Engineer role at Nowhere.", rows), StringComparison.Ordinal);
    }

    [Fact]
    public void A_text_that_claims_no_application_needs_none()
    {
        Assert.Null(ClosedReqs.Why("Nowhere", "Pat,\n\nOne line about your problem. My work: x", Rows));
    }

    [Fact]
    public void An_in_process_claim_is_read_the_same_way()
    {
        Assert.NotNull(ClosedReqs.Why("Nowhere", "I am in process for the Remote Staff Engineer role and meet Sam.", Rows));
    }

    [Fact]
    public void The_person_gate_answers_for_the_tracker_it_holds()
    {
        var gate = new PersonGate(new GateSources(Rows, Fixture.Read("limits.md"), Fixture.Read("rules.md"), Fixture.Read("responders.md"), []), new DateOnly(2026, 11, 2));
        Assert.NotNull(gate.ClosedReq("Aledade", Aledade));
    }
}
