using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// Mail on a job thread that needs no reply (AUDIT1124 follow-ups 3): the Terac paid-interview blast of 11/24 from
/// crossinghurdles.com, a domain the tracker holds only for its recruiter blasts, and the out-of-office answer to Steve's own
/// RTR reply of 11/25. Their subjects are copied as they came; the senders and the rows are trimmed made-up copies.
/// </summary>
public sealed partial class ReplyDraftTests
{
    [Theory]
    [InlineData("Reminder: Earn $85 | Software Developers – AI Assistant Integrations Interview", "", "a paid interview or survey blast")]
    [InlineData("Software Developers: Interview on AI Assistant Integrations", "Terac | AI Assistant Integration Developers$85 one-time | Software Developers", "a paid interview or survey blast")]
    [InlineData("Automatic reply: RTR- Senior/Manager .NET Cloud Lead-EYJP00011432-Remote", "", "an automatic reply")]
    [InlineData("Out of Office: Staff Engineer", "", "an automatic reply")]
    [InlineData("Re: Staff Software Engineer - Web", "Thanks Steve, do you have time Thursday?", null)]
    [InlineData("Interview availability", "The role pays $185,000 to $210,000. Could we set up a call?", null)]
    public void A_paid_blast_or_an_automatic_reply_needs_no_reply(string subject, string text, string? noise) =>
        Assert.Equal(noise, ReplyDraft.Noise(subject, text));

    [Fact]
    public async Task The_pass_queues_no_item_for_a_paid_blast_or_an_automatic_reply_and_says_why_once()
    {
        using var scratch = new Scratch();
        scratch.Master("3624,Ana Blast,Crossing Hurdles (Micro1),Email,11/12/2026,ana.blast@crossinghurdles.com,BULK RECRUITER BLAST\r\n"
            + "3880,Raj Recruit,Pyramid Consulting,Email,11/24/2026,raj.recruit@pyramidci.com,RTR SENT\r\n" + Tracker);
        var inbox = new Inbox([
            new Mail1("n1", "u1", "Dev Sender <dev.sender@crossinghurdles.com>", "Reminder: Earn $85 | Software Developers – AI Assistant Integrations Interview", Now.AddMinutes(-5), "Terac | $85 one-time"),
            new Mail1("n2", "u2", "Raj Recruit <raj.recruit@pyramidci.com>", "Automatic reply: RTR- Senior/Manager .NET Cloud Lead-EYJP00011432-Remote", Now.AddMinutes(-3), "I am out of the office."),
            .. Mails(),
        ]);

        var pass = await new ReplyDrafts(scratch.Store, inbox, null, new Fixed(Now)).RunAsync(CancellationToken.None);

        Assert.False(scratch.Has("dispatch/ready/12-replydraft-n1.md"));
        Assert.False(scratch.Has("dispatch/ready/12-replydraft-n2.md"));
        Assert.True(scratch.Has("dispatch/ready/12-replydraft-m1.md"));
        string log = scratch.Text(ReplyDraft.Log);
        Assert.Contains("skipped n1: a paid interview or survey blast, no reply is drafted", log, StringComparison.Ordinal);
        Assert.Contains("skipped n2: an automatic reply, no reply is drafted", log, StringComparison.Ordinal);
        Assert.Equal(4, pass.Skipped);

        await new ReplyDrafts(scratch.Store, inbox, null, new Fixed(Now.AddMinutes(5))).RunAsync(CancellationToken.None);
        Assert.Single(scratch.Text(ReplyDraft.Log).Split('\n'), line => line.Contains("skipped n1", StringComparison.Ordinal));
    }
}
