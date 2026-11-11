using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// A reply reaches Steve with a draft: every five minutes the inbox's last two hours are read for a message a person
/// wrote on a job thread, one whose writer's address, or work domain, a tracker row already holds, with no draft in the
/// thread and no answer from Steve after it; any other thread, a personal one Steve wrote in among them, gets nothing
/// and one log line saying so. Each job thread's message gets one dispatcher item,
/// marker first, that writes the draft to the share and one line on TODAY.md. A live pass puts each written draft into
/// its thread once with a writer that has no send; a dry pass leaves it on the share. Addresses and names are invented.
/// </summary>
public sealed class ReplyDraftTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 16, 0, 0, TimeSpan.Zero);

    private static Inbox Mailbox() => new(
        new Mail1("m1", "t1", "Dana Whitfield <dana@zephyrlabs.com>", "Re: Staff Software Engineer - Web", Now.AddMinutes(-4), "Thanks Steve, do you have time Thursday?"),
        new Mail1("m0", "t1", "Steve Stout <stevenstout11@gmail.com>", "Staff Software Engineer - Web", Now.AddDays(-1), "Hi Dana"),
        new Mail1("m2", "t2", "Lee Park <lee@arborhealth.com>", "lunch on Friday", Now.AddMinutes(-9), "Want to grab lunch?"),
        new Mail1("m3", "t3", "Omar Haddad <omar@quill.io>", "Interview availability", Now.AddMinutes(-20), "Could we set up a call?") { Draft = true },
        new Mail1("m4", "t4", "LinkedIn <inmail-hit-reply@linkedin.com>", "New role for you", Now.AddMinutes(-3), "x"),
        new Mail1("m5", "t5", "Rita Gomez <rita@oldco.com>", "Your application", Now.AddHours(-3), "We would like to talk"),
        new Mail1("m6", "t6", "Pat Friend <pat.friend@gmail.com>", "Re: Interview tips for your role search", Now.AddMinutes(-6), "Happy to help, call me"),
        new Mail1("m7", "t6", "Steve Stout <stevenstout11@gmail.com>", "Interview tips for your role search", Now.AddHours(-1), "Any advice?"));

    // The tracker's rows: a leader at Zephyr Labs by address (not Dana's), a recruiter on a personal address, Lee by name only.
    private const string Tracker = "3401,Kim Hot,Zephyr Labs,LinkedIn,11/10/2026,kim.hot@zephyrlabs.com,INVITE SENT\r\n3402,Jane Recruit,Talent Co,Email,11/10/2026,jane.recruits@gmail.com,INBOUND\r\n3403,Lee Park,Arbor Health,LinkedIn,11/9/2026,https://www.linkedin.com/in/leepark,INVITE SENT\r\n";

    [Theory]
    [InlineData("dana@zephyrlabs.com", true)]
    [InlineData("kim.hot@zephyrlabs.com", true)]
    [InlineData("jane.recruits@gmail.com", true)]
    [InlineData("pat.friend@gmail.com", false)]
    [InlineData("lee@arborhealth.com", false)]
    [InlineData("omar@quill.io", false)]
    public void A_job_thread_is_one_whose_writer_s_address_or_work_domain_a_tracker_row_holds(string address, bool job)
    {
        var known = ReplyDraft.Known(TrackerCsvText.Parse(Scratch.Header + "\r\n" + Tracker));
        Assert.Equal(job, ReplyDraft.IsJobThread(address, known));
    }

    [Fact]
    public async Task A_person_s_message_on_a_tracker_company_s_thread_gets_one_item_and_nothing_is_sent()
    {
        using var scratch = new Scratch();
        scratch.Master(Tracker);
        var pass = await new ReplyDrafts(scratch.Store, Mailbox(), null, new Fixed(Now)).RunAsync(CancellationToken.None);

        Assert.Equal(new ReplyDraftPass(6, 1, 1, 0, 0, 2), pass);
        string item = scratch.Text("dispatch/ready/12-replydraft-m1.md");
        Assert.StartsWith("Lane: replydraft-m1\nModel: claude-sonnet-5-5\n", item, StringComparison.Ordinal);
        Assert.Contains("/mnt/j/TODAY.md", item, StringComparison.Ordinal);
        Assert.Contains("dry run", item, StringComparison.Ordinal);
        Assert.Contains("send nothing", item, StringComparison.Ordinal);
        Assert.DoesNotContain("view=admin", item, StringComparison.Ordinal);
        Assert.DoesNotContain((char)0x2014, item);
        string marker = scratch.Text("inbox-pass/reply-drafts/m1.md");
        Assert.Equal(("t1", "dana@zephyrlabs.com", "Re: Staff Software Engineer - Web"), ReplyDraft.ReadMarker(marker));
        Assert.Contains("---\nThanks Steve, do you have time Thursday?", marker, StringComparison.Ordinal);
        Assert.False(scratch.Has("dispatch/ready/12-replydraft-m2.md"));
        Assert.False(scratch.Has("dispatch/ready/12-replydraft-m3.md"));

        var again = await new ReplyDrafts(scratch.Store, Mailbox(), null, new Fixed(Now.AddMinutes(5))).RunAsync(CancellationToken.None);
        Assert.Equal((0, 1, 0), (again.Queued, again.Already, again.Skipped));
    }

    [Fact]
    public async Task A_personal_thread_Steve_wrote_in_gets_no_item_and_no_draft_and_the_log_says_why_once()
    {
        using var scratch = new Scratch();
        scratch.Master(Tracker);
        scratch.Write("inbox-pass/reply-drafts/m6.draft.md", "Pat,\n\nThanks.\n\nSteve\n");
        var writer = new Drafts();

        var pass = await new ReplyDrafts(scratch.Store, Mailbox(), writer, new Fixed(Now)).RunAsync(CancellationToken.None);

        Assert.False(scratch.Has("dispatch/ready/12-replydraft-m6.md"));
        Assert.False(scratch.Has("inbox-pass/reply-drafts/m6.md"));
        Assert.Empty(writer.Made);
        Assert.Contains("skipped m6: not a job thread", scratch.Text(ReplyDraft.Log), StringComparison.Ordinal);
        Assert.Equal(2, pass.Skipped);
        await new ReplyDrafts(scratch.Store, Mailbox(), writer, new Fixed(Now.AddMinutes(5))).RunAsync(CancellationToken.None);
        Assert.Single(scratch.Text(ReplyDraft.Log).Split('\n'), line => line.Contains("skipped m6", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_live_pass_puts_a_written_draft_into_its_thread_once_and_a_dry_pass_leaves_it_on_the_share()
    {
        using var scratch = new Scratch();
        scratch.Master(Tracker);
        await new ReplyDrafts(scratch.Store, Mailbox(), null, new Fixed(Now)).RunAsync(CancellationToken.None);
        scratch.Write("inbox-pass/reply-drafts/m1.draft.md", "Dana,\n\nThursday works. 2 PM Central?\n\nSteve\n");

        var dry = await new ReplyDrafts(scratch.Store, Mailbox(), null, new Fixed(Now.AddMinutes(5))).RunAsync(CancellationToken.None);
        Assert.Equal(0, dry.Placed);
        Assert.False(scratch.Has("inbox-pass/reply-drafts/m1.placed"));

        var writer = new Drafts();
        var live = await new ReplyDrafts(scratch.Store, Mailbox(), writer, new Fixed(Now.AddMinutes(10))).RunAsync(CancellationToken.None);
        Assert.Equal(1, live.Placed);
        Assert.Equal(["t1|dana@zephyrlabs.com|Re: Staff Software Engineer - Web|Dana,\n\nThursday works. 2 PM Central?\n\nSteve"], writer.Made);
        Assert.StartsWith("r-1 ", scratch.Text("inbox-pass/reply-drafts/m1.placed"), StringComparison.Ordinal);

        await new ReplyDrafts(scratch.Store, Mailbox(), writer, new Fixed(Now.AddMinutes(15))).RunAsync(CancellationToken.None);
        Assert.Single(writer.Made);
        Assert.Contains("not sent", scratch.Text(ReplyDraft.Log), StringComparison.Ordinal);
    }

    [Fact]
    public void The_draft_port_has_one_write_and_no_send()
    {
        var members = typeof(IReplyDraftWriter).GetMethods().Select(m => m.Name).ToList();
        Assert.Equal(["CreateDraftAsync"], members);
        Assert.DoesNotContain(typeof(ReplyDrafts).GetConstructors().Single().GetParameters(), p => p.ParameterType == typeof(IMailSender) || p.ParameterType == typeof(IInbox));
    }

    private sealed class Fixed(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed record Mail1(string Id, string Thread, string From, string Subject, DateTimeOffset At, string Text)
    {
        public bool Draft { get; init; }
    }

    private sealed class Inbox(params Mail1[] mails) : IInboxReader
    {
        public Task<IReadOnlyList<string>> FindAsync(MailSearch search, int max, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([.. mails.Where(m => !m.From.Contains("stevenstout11", StringComparison.Ordinal)).Select(m => m.Id)]);

        public Task<InboxMessage> ReadAsync(string id, CancellationToken ct)
        {
            var mail = mails.Single(m => m.Id == id);
            return Task.FromResult(new InboxMessage(mail.Id, mail.Thread, mail.From, mail.Subject, mail.At, mail.Text));
        }

        public Task<IReadOnlyList<string>> ThreadSendersAsync(string threadId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<string>>([.. mails.Where(m => m.Thread == threadId).OrderBy(m => m.At).Select(m => m.From)]);

        public Task<bool> ThreadHasDraftAsync(string threadId, CancellationToken ct) => Task.FromResult(mails.Any(m => m.Thread == threadId && m.Draft));
    }

    private sealed class Drafts : IReplyDraftWriter
    {
        public List<string> Made { get; } = [];

        public Task<string> CreateDraftAsync(string threadId, string to, string subject, string text, CancellationToken ct)
        {
            Made.Add($"{threadId}|{to}|{subject}|{text}");
            return Task.FromResult($"r-{Made.Count}");
        }
    }
}
