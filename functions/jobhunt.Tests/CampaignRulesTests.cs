using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The campaign's company rules (ADR-118, the review of the email campaign F1, F10, F16): former employers by name
/// and by domain, the company key, the 30-day rule with the review's named cases, and the live-thread rule over
/// tracker rows. Company and person names are made up; the live-thread rows keep the tracker's real shapes
/// (Communication values, Notes lead words, both date formats) with every name removed.
/// </summary>
public sealed class CampaignRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 24);

    [Theory]
    [InlineData("Storee Construction", null, "Storee Construction")]
    [InlineData("Storee", null, "Storee Construction")]
    [InlineData("Storee Construction (network)", null, "Storee Construction")]
    [InlineData("Former Conway Investment Solutions coworker", null, null)]
    [InlineData("Conway Investment Solutions", null, "Conway Investment Research")]
    [InlineData("Cass Information Systems Inc", null, "Cass Information Systems")]
    [InlineData("Hunter Engineering", null, "Hunter Engineering")]
    [InlineData("State of Missouri", null, "State of Missouri")]
    [InlineData(null, "pat@storee.com", "Storee Construction")]
    [InlineData(null, "pat@mail.conwayis.com", "Conway Investment Research")]
    [InlineData(null, "pat@cassinfo.com", "Cass Information Systems")]
    [InlineData(null, "pat@hunter.com", "Hunter Engineering")]
    [InlineData(null, "pat@dor.mo.gov", "State of Missouri")]
    [InlineData("Acme Dental", "pat@acmedental.com", null)]
    [InlineData("Hudson Insurance Group (former Storee colleague)", null, null)]
    public void F1_a_former_employer_is_found_by_name_or_by_domain_and_never_by_a_date(string? company, string? address, string? expected)
    {
        Assert.Equal(expected, FormerEmployers.Of(company, address)?.Name);
        Assert.Equal(5, FormerEmployers.All.Count);
    }

    [Theory]
    [InlineData("pat@mail.acme.co.uk", "acme.co.uk")]
    [InlineData("careers.acme.com", "acme.com")]
    [InlineData("Pat.Lee@ACME.com", "acme.com")]
    [InlineData("www.acme.io", "acme.io")]
    [InlineData("dor.mo.gov", "mo.gov")]
    [InlineData("pat@school.k12.mo.us", "school.k12.mo.us")]
    [InlineData("acme.com.au", "acme.com.au")]
    [InlineData("no-domain", "")]
    [InlineData("pat@", "")]
    [InlineData("pat@bad host.com", "")]
    public void F10_a_company_key_is_the_registrable_domain_public_suffix_aware(string input, string key) =>
        Assert.Equal(key, CompanyKey.Of(input));

    private static CampaignCompany Co(string key, string? parent, params string[] names) => new(key, parent, names);

    private static PastSend Sent(string key, string? parent, string company, int daysAgo) => new(key, parent, company, Today.AddDays(-daysAgo));

    public static TheoryData<string, CampaignCompany, PastSend[], bool> ThirtyDayCases => new()
    {
        { "same domain, two people", Co("acme.com", null, "Acme"), [Sent("acme.com", null, "Acme", 3)], false },
        { "a subdomain", Co(CompanyKey.Of("pat@eng.acme.com"), null, "Acme Engineering"), [Sent("acme.com", null, "Acme", 3)], false },
        { "child after its parent", Co("covermymeds.com", "mckesson.com", "CoverMyMeds"), [Sent("mckesson.com", null, "McKesson", 10)], false },
        { "parent after its child", Co("mckesson.com", null, "McKesson"), [Sent("covermymeds.com", "mckesson.com", "CoverMyMeds", 10)], false },
        { "a second domain under one parent", Co("acme.com", "acmeholdings.com", "Acme"), [Sent("acme.io", "acmeholdings.com", "Acme IO", 5)], false },
        { "day 29", Co("acme.com", null, "Acme"), [Sent("acme.com", null, "Acme", 29)], false },
        { "day 30", Co("acme.com", null, "Acme"), [Sent("acme.com", null, "Acme", 30)], false },
        { "day 31", Co("acme.com", null, "Acme"), [Sent("acme.com", null, "Acme", 31)], true },
        { "a follow-up counts as the email", Co("acme.com", null, "Acme"), [Sent("acme.com", null, "Acme", 40), Sent("acme.com", null, "Acme", 2)], false },
        { "Upstart after Upstart Network by name", Co("upstart.com", null, "Upstart"), [Sent("upstartnetwork.com", null, "Upstart Network", 4)], false },
        { "another company", Co("globex.com", null, "Globex"), [Sent("acme.com", null, "Acme", 1)], true },
        { "no sends at all", Co("globex.com", null, "Globex"), [], true },
    };

    [Theory]
    [MemberData(nameof(ThirtyDayCases))]
    public void F10_one_email_a_company_in_thirty_days_by_key_parent_or_name(string because, CampaignCompany company, PastSend[] sends, bool allowed)
    {
        var verdict = ThirtyDayRule.Check(company, sends, Today);
        Assert.True(allowed == verdict.Allowed, $"{because}: {verdict.Reason}");
        if (!allowed)
        {
            Assert.StartsWith("30-day rule: emailed ", verdict.Reason, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void F10_the_thirty_day_hold_names_the_day_it_opens()
    {
        var verdict = ThirtyDayRule.Check(Co("acme.com", null, "Acme"), [Sent("acme.com", null, "Acme", 30)], Today);
        Assert.Equal("30-day rule: emailed 2026-09-24, opens 2026-10-25", verdict.Reason);
    }

    [Fact]
    public void F1_a_former_employer_is_held_first_and_named_then_the_do_not_apply_list_then_the_thirty_days()
    {
        var storee = Co("storee.com", null, "Storee Construction");
        var list = new[] { new DoNotApply(["Upstart", "Upstart Network"], new DateOnly(2026, 12, 20)) };

        Assert.Equal("former employer: Storee Construction", CompanyGate.Check(storee, [Sent("storee.com", null, "Storee", 2)], list, Today).Reason);
        Assert.Equal("former employer: Hunter Engineering", CompanyGate.Check(Co("hunter.com", null, "Hunter Eng Co"), [], list, Today).Reason);
        Assert.Equal("former employer: State of Missouri", CompanyGate.Check(Co("health.mo.gov", "mo.gov", "Department of Health"), [], list, Today).Reason);
        Assert.Equal("do-not-apply list until 2026-12-20", CompanyGate.Check(Co("upstart.com", null, "Upstart Network"), [], list, Today).Reason);
        Assert.True(CompanyGate.Check(Co("upstart.com", null, "Upstart"), [], list, new DateOnly(2026, 12, 20)).Allowed);
        Assert.StartsWith("30-day rule", CompanyGate.Check(Co("acme.com", null, "Acme"), [Sent("acme.com", null, "Acme", 1)], list, Today).Reason, StringComparison.Ordinal);
        Assert.True(CompanyGate.Check(Co("acme.com", null, "Acme"), [], list, Today).Allowed);
    }

    private static string[] Row(string count, string company, string channel, string date, string notes) =>
        [count, "", company, channel, date, "", notes];

    // The tracker's shapes, names removed: Counts 484, 3061, 3104, 2808, 2793 and 3038 as read 2026-10-24, and a sweep receipt.
    private static readonly string[][] Master =
    [
        Row("484", "Acme", "LinkedIn", "10/20/2026", "Recruiter Reachout - Senior Software Engineer (Remote)"),
        Row("3061", "Globex", "Email", "10/23/2026", "INBOUND 12:06 PM CDT 10/23 (Gmail msg 1, subject 'Remote Role'): a person wrote"),
        Row("3104", "Initech", "Greenhouse", "10/24/2026", "APPLICATION SUBMITTED BY CLAUDE (apply1024) ~09:3x CDT 10/24. Senior Software Engineer 2"),
        Row("NA", "Initech", "Email", "10/24/2026", "RECEIPT, 10/24/2026 9:40 AM Central (Gmail msg 2, from no-reply@greenhouse.io): applications on file: Counts 3104"),
        Row("2808", "Umbrella", "LinkedIn", "2026-10-17", "SCOPED CONNECTION REQUEST SENT BY CLAUDE (bg1017) ~08:03 CDT 10/17 via preload/custom-invite"),
        Row("2793", "Hooli", "LinkedIn connection invite w/ note", "2026-10-15", "SCOPED CONNECTION REQUEST SENT BY CLAUDE (li1015eve) ~3:5x PM CDT 10/15"),
        Row("3038", "Vandelay", "Email", "10/23/2026", "REJECTION, generic, 10/23/2026 11:00 AM Central: applications on file: Counts 3001"),
        Row("120", "Soylent", "LinkedIn", "8/1/2026", "INBOUND: a manager wrote back"),
    ];

    [Theory]
    [InlineData("Acme", false, "live thread, Count 484")]
    [InlineData("Globex", false, "live thread, Count 3061")]
    [InlineData("Initech", true, "allowed")]
    [InlineData("Umbrella", true, "allowed")]
    [InlineData("Hooli", true, "allowed")]
    [InlineData("Vandelay", false, "live thread, Count 3038")]
    [InlineData("Soylent", true, "allowed")]
    [InlineData("Acme Corporation", false, "live thread, Count 484")]
    public void F16_a_person_s_thread_in_thirty_days_holds_the_lead_and_an_application_receipt_or_unanswered_invite_does_not(string company, bool allowed, string reason)
    {
        var verdict = LiveThreadRule.Check([company], Master, Today);
        Assert.Equal((allowed, reason), (verdict.Allowed, verdict.Reason));
    }
}
