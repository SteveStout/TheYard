using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The reply pass spends the user's Gmail quota once per message: it asks only for the window, with GitHub's mail left
/// out, and a message an earlier pass marked or skipped is settled by its file and never read again, while it still
/// holds its thread so an older message there is not queued beside it.
/// </summary>
public sealed partial class ReplyDraftTests
{
    [Fact]
    public async Task The_search_asks_for_the_window_only_and_leaves_GitHub_s_mail_out()
    {
        using var scratch = new Scratch();
        scratch.Master(Tracker);
        var inbox = Mailbox();

        await new ReplyDrafts(scratch.Store, inbox, null, new Fixed(Now)).RunAsync(CancellationToken.None);

        var search = Assert.Single(inbox.Searches);
        Assert.Equal(Now - ReplyDraft.Window, search.After);
        Assert.Equal("in:inbox after:1794405600 -from:me -from:(notifications@github.com OR github.com)", GmailQuery.Compose(search));
    }

    [Fact]
    public async Task A_message_an_earlier_pass_marked_or_skipped_is_never_read_again()
    {
        using var scratch = new Scratch();
        scratch.Master(Tracker);
        await new ReplyDrafts(scratch.Store, Mailbox(), null, new Fixed(Now)).RunAsync(CancellationToken.None);
        Assert.True(scratch.Has(ReplyDraft.Marker("m1")));
        Assert.True(scratch.Has(ReplyDraft.SkippedPath("m2")));
        Assert.True(scratch.Has(ReplyDraft.SkippedPath("m6")));

        var inbox = Mailbox();
        var again = await new ReplyDrafts(scratch.Store, inbox, null, new Fixed(Now.AddMinutes(5))).RunAsync(CancellationToken.None);

        Assert.DoesNotContain("m1", inbox.Reads);
        Assert.DoesNotContain("m2", inbox.Reads);
        Assert.DoesNotContain("m6", inbox.Reads);
        Assert.Equal((0, 1, 0, 3), (again.Queued, again.Already, again.Skipped, again.Read));
    }

    [Fact]
    public async Task An_older_message_in_a_marked_thread_is_not_queued_beside_it()
    {
        using var scratch = new Scratch();
        scratch.Master(Tracker);
        Mail1[] mails = [.. Mails(), new Mail1("m9", "t1", "Kim Hot <kim.hot@zephyrlabs.com>", "Re: Staff Software Engineer - Web", Now.AddMinutes(-30), "Looping in Dana")];
        await new ReplyDrafts(scratch.Store, new Inbox(mails), null, new Fixed(Now)).RunAsync(CancellationToken.None);

        var again = await new ReplyDrafts(scratch.Store, new Inbox(mails), null, new Fixed(Now.AddMinutes(5))).RunAsync(CancellationToken.None);

        Assert.Equal(0, again.Queued);
        Assert.False(scratch.Has(ReplyDraft.Marker("m9")));
        Assert.False(scratch.Has("dispatch/ready/12-replydraft-m9.md"));
    }
}
