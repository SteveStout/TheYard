namespace JobHunt.Server.Tests;

/// <summary>
/// What kind of LinkedIn row a tracker row is (TrackerKinds), read the way outreach-limits.md counts: every way the
/// tracker has written an invite reads as an invite and never as a message, so a person invited is not served again
/// and a connect note never holds a follow-up as a second touch; an inbound, an acceptance, an application or a row
/// off LinkedIn is neither; and only the opening words decide.
/// </summary>
public sealed class TrackerKindsTests
{
    private static string[] Row(string notes, string channel = "LinkedIn") => ["3611", "Sam Roe", "Acme", channel, "11/12/2026", "", notes];

    [Theory]
    [InlineData("INVITE WITH NOTE SENT by Claude (chat1112) ~8:37 AM CST 11/12 on Steve's 'Send that'")]
    [InlineData("CONNECTION REQUEST sent with a note, seat VP Engineering")]
    [InlineData("SCOPED INVITE to the hiring manager, note read back")]
    [InlineData("INVITE SENT (no note), Pending read back")]
    [InlineData("CONNECT NOTE: Hi Sam, I lead .NET platform teams")]
    [InlineData("  invite with note sent, lower case from the 6 AM lane")]
    public void Every_way_the_tracker_writes_an_invite_reads_as_an_invite_and_not_a_message(string notes)
    {
        Assert.True(TrackerKinds.IsInvite(Row(notes)));
        Assert.False(TrackerKinds.IsMessage(Row(notes)));
    }

    [Fact]
    public void A_channel_that_says_invite_makes_the_row_an_invite_whatever_its_note_says()
    {
        Assert.True(TrackerKinds.IsInvite(Row("Hi Sam, I lead .NET platform teams", "LinkedIn invite")));
        Assert.False(TrackerKinds.IsMessage(Row("Hi Sam, I lead .NET platform teams", "LinkedIn invite")));
    }

    [Theory]
    [InlineData("Sent follow-up after the accept: thanks for connecting, Sam")]
    [InlineData("MESSAGE SENT to Sam on the Staff role")]
    [InlineData("Thanks Sam, I applied to the Staff role yesterday and wanted to say hello")]
    public void A_note_this_side_wrote_that_is_no_invite_reads_as_a_message(string notes)
    {
        Assert.True(TrackerKinds.IsMessage(Row(notes)));
        Assert.False(TrackerKinds.IsInvite(Row(notes)));
    }

    [Theory]
    [InlineData("INBOUND: CONNECTION REQUEST from Sam Roe", "LinkedIn")]
    [InlineData("CONNECTION ACCEPTED by Sam Roe", "LinkedIn")]
    [InlineData("REPLY RECEIVED from Sam", "LinkedIn")]
    [InlineData("APPLIED via the posting, Staff Engineer", "LinkedIn")]
    [InlineData("APPLICATION SUBMITTED by Claude", "LinkedIn")]
    [InlineData("MESSAGE SENT through the form", "LinkedIn Easy Apply")]
    [InlineData("INVITE SENT", "Email")]
    public void An_inbound_an_acceptance_an_application_or_a_row_off_LinkedIn_is_neither(string notes, string channel)
    {
        Assert.False(TrackerKinds.IsInvite(Row(notes, channel)));
        Assert.False(TrackerKinds.IsMessage(Row(notes, channel)));
    }

    [Fact]
    public void Only_the_opening_words_decide_so_an_invite_named_late_in_a_long_message_is_still_a_message()
    {
        string notes = "Thanks for the quick reply, Sam. Here is the role I mentioned and the two questions on it; " + "INVITE SENT";
        Assert.True(TrackerKinds.IsMessage(Row(notes)));
        Assert.False(TrackerKinds.IsInvite(Row(notes)));
    }

    [Theory]
    [InlineData("11/12/2026", 2026, 11, 12)]
    [InlineData("2026-11-12", 2026, 11, 12)]
    [InlineData("03/05/2026", 2026, 3, 5)]
    public void A_row_reads_its_date_in_each_form_the_tracker_writes(string day, int year, int month, int date) =>
        Assert.Equal(new DateOnly(year, month, date), TrackerKinds.Day(["1", "Sam Roe", "Acme", "LinkedIn", day, "", "INVITE SENT"]));

    [Fact]
    public void A_date_in_no_known_form_or_a_short_row_reads_no_date_and_a_spool_row_of_six_reads_as_seven()
    {
        Assert.Null(TrackerKinds.Day(["1", "Sam Roe", "Acme", "LinkedIn", "Nov 12", "", "INVITE SENT"]));
        Assert.Null(TrackerKinds.Day(["1", "Sam Roe", "Acme"]));
        string[] spool = ["Sam Roe", "Acme", "LinkedIn", "11/12/2026", "", "INVITE SENT"];
        Assert.Equal(["", .. spool], TrackerKinds.Seven(spool));
        Assert.Equal(new DateOnly(2026, 11, 12), TrackerKinds.Day(TrackerKinds.Seven(spool)));
    }
}
