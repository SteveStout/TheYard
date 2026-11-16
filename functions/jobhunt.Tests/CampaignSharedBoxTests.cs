using JobHunt.Application.Features.Campaign;
using JobHunt.Domain.Features.Campaign;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// A shared box under a leader's greeting is never sent, on any track: 99 of the campaign's first 105 emails went to
/// info@, press@ and the like with a leader's first name on top, read by whoever opens that box. And a lead that already
/// had its first email never gets it again: the leads stay open after a send, so once the 30-day hold lapsed the pass
/// would have mailed the same note twice. A second, different email waits on an approved template, and never goes to a
/// shared box or a third time.
/// </summary>
public sealed class CampaignSharedBoxTests
{
    private static (CampaignVerdict Verdict, RenderedEmail? Email, bool Block) Check(LeadRecord lead) =>
        CampaignSend.Check(lead, [], [], [], new DateOnly(2026, 10, 24), new DateTimeOffset(2026, 10, 24, 13, 0, 5, TimeSpan.Zero));

    [Fact]
    public void Press_at_roblox_with_nick_in_the_greeting_is_refused()
    {
        var lead = CampaignSendTests.Lead(company: "Roblox", key: "roblox.com", edit: b =>
        {
            b.FirstName = "Nick";
            b.ContactName = "Nick Tornow";
            b.ContactTitle = "Chief Technology Officer";
            b.ContactEmail = "press@roblox.com";
        });

        var (verdict, email, _) = Check(lead);

        Assert.False(verdict.Allowed);
        Assert.Equal(CampaignSend.SharedBoxNamed, verdict.Reason);
        Assert.Null(email);
    }

    [Theory]
    [InlineData("info")]
    [InlineData("hello")]
    [InlineData("careers")]
    [InlineData("hr")]
    [InlineData("jobs")]
    [InlineData("contact")]
    [InlineData("recruiting")]
    [InlineData("media")]
    [InlineData("marketing")]
    [InlineData("support")]
    [InlineData("talentacquisition")]
    [InlineData("communications")]
    [InlineData("public.relations")]
    public void Every_shared_box_under_a_name_is_refused(string box)
    {
        var lead = CampaignSendTests.Lead(edit: b => b.ContactEmail = $"{box}@acmedental.com");

        Assert.Equal(CampaignSend.SharedBoxNamed, Check(lead).Verdict.Reason);
    }

    [Fact]
    public void A_person_s_own_address_still_goes()
    {
        Assert.True(Check(CampaignSendTests.Lead(edit: b => b.ContactEmail = "pat.lee@acmedental.com")).Verdict.Allowed);
    }

    [Fact]
    public void A_box_greeted_as_the_company_mailbox_is_not_under_a_name()
    {
        var lead = CampaignSendTests.Lead(edit: b =>
        {
            b.FirstName = "";
            b.ContactName = "";
            b.ContactTitle = LeadSpool.CompanyMailbox;
            b.ContactEmail = "info@acmedental.com";
        });

        Assert.NotEqual(CampaignSend.SharedBoxNamed, Check(lead).Verdict.Reason);
    }

    [Fact]
    public void A_lead_already_emailed_never_gets_the_first_email_again()
    {
        var lead = CampaignSendTests.Lead(edit: b => b.Touches = 1);

        var (verdict, email, block) = Check(lead);

        Assert.Equal(SecondTouch.NoTemplate, verdict.Reason);
        Assert.Null(email);
        Assert.False(block);
    }

    [Fact]
    public void A_follow_up_never_goes_to_a_shared_box_or_a_third_time()
    {
        Assert.Equal(SecondTouch.NoSharedBox, Check(CampaignSendTests.Lead(edit: b => { b.Touches = 1; b.ContactEmail = "info@acmedental.com"; })).Verdict.Reason);
        Assert.Equal(SecondTouch.NoThird, Check(CampaignSendTests.Lead(edit: b => b.Touches = 2)).Verdict.Reason);
    }

    [Fact]
    public void The_finder_s_sql_reads_the_same_boxes_the_check_does()
    {
        foreach (string box in TrackAContacts.GeneralBoxes.Split('|'))
        {
            Assert.True(TrackAContacts.IsGeneralMailbox($"{box}@acme.com"), box);
        }
        Assert.False(TrackAContacts.IsGeneralMailbox("nick.tornow@roblox.com"));
    }
}
