using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The reply-draft item's steps (audit1124b F5): the three CONVENTIONS fragments, no contractions, the tracker rows and
/// the thread before a draft (none when Steve's own send is newer), no question the thread answered, the intro asked for
/// now, and check_draft.py before the card names it, in that order; a LinkedIn item holds chrome and linkedin and reads
/// the live thread first, an email item opens no browser. Made-up people; the item is text, nothing is read or written.
/// </summary>
public sealed partial class ReplyDraftTests
{
    private static readonly DateTimeOffset Found = new(2026, 11, 24, 10, 0, 0, TimeSpan.Zero);

    private static WaitingReply Waiting(string to) =>
        new("m9", "t9", to, "Sam Reed", "Re: the platform seat", Found.AddMinutes(-20), "Happy to introduce you to our VP, Dana.");

    [Fact]
    public void The_item_holds_each_new_line_in_order_before_the_draft_is_named_on_the_card()
    {
        string item = ReplyDraft.Item(Waiting("sam@acme.test"), live: true, Found);
        string[] order =
        [
            "Read the CONVENTIONS.md fragments 20261119-133302Z, 20261124-134700Z and 20261124-142100Z before drafting",
            "No contractions anywhere in the draft.",
            "Read the person's newest tracker rows and the thread (the marker's text) before drafting. When Steve's own send in the thread is newer than this message, write no draft and no TODAY.md line, and close the item as answered",
            "Never ask a question the thread already answered.",
            "When the other person offered an intro, a referral or a named person, draft \"Could you put me in touch with ...\" now, and never hold the reply behind an application.",
            "Write the reply's body, and nothing else, to /mnt/j/inbox-pass/reply-drafts/m9.draft.md.tmp",
            "Run `python3 /mnt/j/check_draft.py /mnt/j/inbox-pass/reply-drafts/m9.draft.md` and fix every finding before the draft is named on the card",
            "Append one line to /mnt/j/TODAY.md: `- REPLY DRAFT",
        ];
        int at = 0;
        foreach (string line in order)
        {
            int found = item.IndexOf(line, StringComparison.Ordinal);
            Assert.True(found > at, $"not in order or missing: {line}");
            at = found;
        }
        Assert.Contains("\n2. Read the CONVENTIONS.md fragments", item, StringComparison.Ordinal);
        Assert.Contains("\n9. Append one line to /mnt/j/TODAY.md", item, StringComparison.Ordinal);
        Assert.Contains("check_draft.py's last result", item, StringComparison.Ordinal);
        Assert.Contains("send nothing", item, StringComparison.Ordinal);
        Assert.DoesNotContain((char)0x2014, item);
    }

    [Fact]
    public void A_LinkedIn_item_holds_chrome_and_linkedin_and_its_first_step_is_the_read_and_an_email_item_does_not()
    {
        string linkedIn = ReplyDraft.Item(Waiting("inmail-hit-reply@linkedin.com"), live: true, Found);
        Assert.Contains("\nHolds: chrome, linkedin\n", linkedIn, StringComparison.Ordinal);
        Assert.Contains("\n1. Read the live LinkedIn thread first, read only, in the WickedAwsome Chrome", linkedIn, StringComparison.Ordinal);
        Assert.Contains("Save the thread's text to /mnt/j/networklog/linkedin-inbox/text/t9.txt", linkedIn, StringComparison.Ordinal);
        Assert.Contains("close with the DONE note saying `thread not read`", linkedIn, StringComparison.Ordinal);
        Assert.Contains("the thread (/mnt/j/networklog/linkedin-inbox/text/t9.txt) before drafting", linkedIn, StringComparison.Ordinal);
        Assert.Contains("\n2. Read /mnt/j/writing-samples.md", linkedIn, StringComparison.Ordinal);
        Assert.Equal("networklog/linkedin-inbox/text", ReplyDraft.ThreadTexts);

        string email = ReplyDraft.Item(Waiting("sam@acme.test"), live: true, Found);
        Assert.Contains("\nHolds:\nNeeds: jdrive\n", email, StringComparison.Ordinal);
        Assert.DoesNotContain("chrome", email, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\n1. Read /mnt/j/writing-samples.md", email, StringComparison.Ordinal);
        Assert.Contains("You open no browser and no mailbox", email, StringComparison.Ordinal);
        Assert.True(ReplyDraft.IsLinkedIn(" Notes@LinkedIn.com "));
        Assert.False(ReplyDraft.IsLinkedIn("sam@notlinkedin.com.test"));
    }
}
