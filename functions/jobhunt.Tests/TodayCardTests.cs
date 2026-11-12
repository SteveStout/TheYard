using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The one card (PLAN-AUTOHUNT rung 4, TodayCard and its parts in JobHunt.Domain's Card slice, TodayCardSource,
/// TodayCardTimer): TODAY.md rewritten whole at 7:30 Central from the reply queue, the dispatcher's files and the
/// calendar, in the plan's order, an answered ask or a DO-NOT-SEND draft never shown, rejection reads carried
/// forward, folded to the 15-minute cap, and mailed to his own address only. Made-up people except the queue fixture,
/// which is the reply lane's own contract file.
/// </summary>
public sealed class TodayCardTests
{
    private static readonly DateTime Tuesday = new(2026, 11, 10, 7, 30, 0);

    private const string Queue = "<!-- reply-queue: 2 -->\n# Reply queue\n\n## A. Inbound\n"
        + "- **Ann Lee** | Acme | thread: gmail 1a2b | draft: nightly/2026-11-10/0400-reply-opus-5-5/drafts/gmail-acme.txt\n"
        + "- **Bo Diaz** | Globex | thread: linkedin 2-xyz | draft: nightly/2026-11-10/0400-reply-opus-5-5/drafts/li-globex.txt\n"
        + "- **Cy Fox** | - | thread: linkedin 2-abc | held: text not read (a LinkedIn notice carries no text)\n"
        + "- **Allen Kadilis** | LINQ | thread: gmail 9z | held: an answered ask\n"
        + "## B. Accepted\n- **Di Ng** | Initech | Count 5 | draft: nightly/x/li-initech.txt\n"
        + "## D. Not owed\n- free text\n"
        + "## E. Rejections\n- **Ed Ruiz** | Umbrella | Count 9 | draft: nightly/2026-11-10/0400-reply-opus-5-5/drafts/rejection-ask-umbrella.txt\n"
        + "Note: read: Umbrella, rejection 11/9 (Count 9): 3 days from the room to the no.\n"
        + "- **Flo Kim** | Hooli | Count 10 | draft: nightly/2026-11-10/0400-reply-opus-5-5/drafts/rejection-ask-hooli.txt\n"
        + "Note: read: Hooli, rejection 11/9 (Count 10): 1 day from the room to the no.\n";

    private const string Status = "# Dispatch status\n\nWritten Tue 11/10 07:29:00 CST by dispatch.py.\n\n## Running\n- lane-a: opus, started 07:00 CST\n\n## Paused\n- no\n\n## Ready (1)\n- 90-x.md (waits)\n\n## Proposed (queue it?)\n- lane-b-f1.md\n\n## Held\n- lane-c: stalled twice\n";

    private const string Left = "## lane-old, closed Sun 11/08 09:00 CST\n\n## LEFT FOR STEVE\n- an old ask\n\n"
        + "## lane-x, closed Mon 11/09 12:19 CST\n\n## LEFT FOR STEVE\n\n- CRITICAL, an email under your name to Allen Kadilis at LINQ: the ask.\n- **A. A template.** Your yes:\n\n  Subject: Hi\n\n  Body line\n- nothing\n\nFOLLOW-ON: J:\\dispatch\\proposed\\x.md\n\n"
        + "## lane-y, closed Mon 11/09 15:00 CST\n\n## LEFT FOR STEVE\n- none\n";

    private const string Previous = "> RULES FIRST\n\n# TODAY\n\n## The lanes\n\n- Mon 11/09 08:39 CST, dispatch: lane-z is held\n\n## Rejection reads\n\n"
        + "- LINQ, rejection 11/5 (Count 3492): a read. Draft: J:\\nightly\\2026-11-09\\ah2\\rejection-ask-linq.txt\n  ANSWERED, do not send. Allen Kadilis.\n"
        + "- Hooli, rejection 11/9 (Count 10): 1 day from the room to the no. Draft: nightly/2026-11-10/0400-reply-opus-5-5/drafts/rejection-ask-hooli.txt\n  CRITICAL, an email under your name to Flo Kim at Hooli. Feedback ask.\n"
        + "- Initrode, rejection 11/2: carried. Draft: J:\\nightly\\2026-11-03\\r\\rejection-ask-initrode.txt\n"
        + "- Tue 11/10 02:00 CST, dispatch: lane-q is held\n";

    [Fact]
    public void The_reply_lane_s_own_fixture_reads_item_for_item_with_each_read_under_its_rejection()
    {
        var items = TodayParts.Items(File.ReadAllText(Path.Combine(Repo(), "server", "JobHunt.Server.Tests", "Fixtures", "reply-queue.md")));
        Assert.Equal(8, items.Count);
        Assert.Equal(["A", "A", "B", "B", "B", "C", "C", "E"], items.Select(i => i.Section));
        Assert.True(items[1].IsEmail);
        Assert.Equal("nightly/2026-11-09/0400-reply-opus-5-5/drafts/gmail-dan-chinnici-techpartners-checkin.txt", items[1].Draft);
        Assert.Equal("", items[5].Person);
        Assert.StartsWith("read: LINQ, rejection 11/5 (Count 3492)", Assert.Single(items[7].Notes), StringComparison.Ordinal);
    }

    [Fact]
    public void The_card_runs_in_the_plan_s_order_and_shows_every_draft_in_full()
    {
        string card = TodayCard.Render(Inputs());
        Assert.StartsWith(TodayCard.RulesFirst + "\n\n# TODAY, Tuesday 11/10\n\nWritten 07:30 Central", card, StringComparison.Ordinal);
        string[] order = ["## CRITICAL sends", "## Essays the 6 AM run submitted", "## Replies", "## Calls in the next 72 hours", "## The lanes", "## Left for you", "## One decision", "## Rejection reads"];
        Assert.Equal(order.OrderBy(h => card.IndexOf(h, StringComparison.Ordinal)), order);
        Assert.Contains("1. FIT: NOT CHECKED. CRITICAL, an email under your name to Ann Lee (Acme), a reply on thread: gmail 1a2b. Full text:\n   > Ann,\n   > Tuesday works.\n   Gate: UNANSWERED IN WRITING: range. Draft:", card, StringComparison.Ordinal);
        Assert.Contains("- **Ann Lee (Acme)**, email (thread: gmail 1a2b): FIT: NOT CHECKED\n  Gate: UNANSWERED IN WRITING: range.\n  The full text is CRITICAL send 1 above.", card, StringComparison.Ordinal);
        Assert.Contains("- **Bo Diaz (Globex)**, LinkedIn (thread: linkedin 2-xyz): FIT: NOT CHECKED\n  The draft, typed live in the thread for your send once you say \"send\" or edit it:\n   > Bo, thanks.", card, StringComparison.Ordinal);
        Assert.Contains("- **Cy Fox**, LinkedIn (thread: linkedin 2-abc): FIT: NOT CHECKED\n  Held: text not read", card, StringComparison.Ordinal);
        Assert.Contains("Question one, in full.", card, StringComparison.Ordinal);
        Assert.DoesNotContain("RULES FIRST: essays", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Di Ng", card, StringComparison.Ordinal);
        Assert.Contains("- Tue 11/10 2:00 PM Central, Interview with Acme: Google Meet x (Google Calendar). Must-answers: range NOT IN WRITING", card, StringComparison.Ordinal);
        Assert.Contains("- Running: lane-a: opus, started 07:00 CST\n- Proposed: 1 (the list is in dispatch/STATUS.md); none needs your yes today.\n- Held: lane-c: stalled twice\n", card, StringComparison.Ordinal);
        Assert.DoesNotContain("90-x.md", card, StringComparison.Ordinal);
        Assert.Contains("- Pick the Tuesday slot?\n", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Second decision", card, StringComparison.Ordinal);
    }

    [Fact]
    public void An_answered_ask_and_a_do_not_send_draft_never_reach_the_card()
    {
        string card = TodayCard.Render(Inputs());
        Assert.DoesNotContain("Allen Kadilis", card, StringComparison.Ordinal);
        Assert.DoesNotContain("LINQ", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Umbrella", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Initrode", card, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejection_reads_are_carried_once_without_the_dispatcher_s_lines_and_a_new_one_comes_with_its_ask()
    {
        var inputs = Inputs() with { DoNotSend = new HashSet<string>(StringComparer.Ordinal), Previous = Previous.Replace("Hooli", "Vandelay", StringComparison.Ordinal) };
        string card = TodayCard.Render(inputs);
        string reads = card[card.IndexOf("## Rejection reads", StringComparison.Ordinal)..];
        Assert.Contains("- Vandelay, rejection 11/9 (Count 10)", reads, StringComparison.Ordinal);
        Assert.Contains("- Initrode, rejection 11/2: carried.", reads, StringComparison.Ordinal);
        Assert.Contains("- Umbrella, rejection 11/9 (Count 9): 3 days from the room to the no. Draft: nightly/2026-11-10/0400-reply-opus-5-5/drafts/rejection-ask-umbrella.txt\n  CRITICAL, an email under your name to Ed Ruiz at Umbrella. Feedback ask, full text:\n   > Ed, Thanks for letting me know. What would have made the difference? Thanks, Steve\n  Say \"send\" to send it, or let it go.", reads, StringComparison.Ordinal);
        Assert.DoesNotContain("dispatch:", card, StringComparison.Ordinal);
        Assert.Contains("feedback asks, each in full under Rejection reads at the end.", card, StringComparison.Ordinal);
        // The carried Hooli read (renamed Vandelay above) is not added again from section E: its read line is the same.
        Assert.Equal(1, Count(TodayCard.Render(Inputs() with { DoNotSend = new HashSet<string>(StringComparer.Ordinal) }), "Hooli, rejection 11/9"));
    }

    [Fact]
    public void Only_lanes_that_closed_in_the_last_day_leave_lines_and_their_critical_ask_moves_to_the_top()
    {
        string card = TodayCard.Render(Inputs() with { Answered = "" });
        Assert.DoesNotContain("an old ask", card, StringComparison.Ordinal);
        Assert.DoesNotContain("lane-y", card, StringComparison.Ordinal);
        Assert.DoesNotContain("FOLLOW-ON", card, StringComparison.Ordinal);
        Assert.Contains("### lane-x, closed Mon 11/9 12:19\n\n- **A. A template.** Your yes:\n\n  Subject: Hi\n\n  Body line\n", card, StringComparison.Ordinal);
        Assert.True(card.IndexOf("- CRITICAL, an email under your name to Allen Kadilis at LINQ: the ask. (lane-x)", StringComparison.Ordinal) < card.IndexOf("## Essays", StringComparison.Ordinal));
    }

    [Fact]
    public void Walls_show_on_a_monday_and_otherwise_only_where_a_human_has_replied()
    {
        const string walls = "- HUNTER_KEY: run the key file once\n- Acme Workday sign-in, replied: Ann wrote back\n";
        string tuesday = TodayCard.Render(Inputs() with { Walls = walls });
        Assert.DoesNotContain("HUNTER_KEY", tuesday, StringComparison.Ordinal);
        Assert.Contains("- Acme Workday sign-in, replied: Ann wrote back", tuesday, StringComparison.Ordinal);
        string monday = TodayCard.Render(Inputs() with { At = Tuesday.AddDays(-1), Walls = walls });
        Assert.Contains("Monday, the whole list", monday, StringComparison.Ordinal);
        Assert.Contains("- HUNTER_KEY: run the key file once", monday, StringComparison.Ordinal);
        Assert.DoesNotContain("## Walls", TodayCard.Render(Inputs()), StringComparison.Ordinal);
    }

    [Fact]
    public void A_card_past_fifteen_minutes_folds_its_held_replies_into_one_line()
    {
        string many = string.Concat(Enumerable.Range(1, 400).Select(i => $"- **Person {i}** | - | thread: linkedin {i} | held: text not read, a notice with no text and a reason long enough to count\n"));
        string card = TodayCard.Render(Inputs() with { Queue = "<!-- reply-queue: 2 -->\n## A. Inbound\n" + many });
        Assert.Contains("- 400 held replies with no draft, each with its reason, in nightly/2026-11-10/0400-reply-opus-5-5/linkedin-queue.md.", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Person 7**", card, StringComparison.Ordinal);
        Assert.True(TodayCard.Minutes(card) <= TodayCard.CapMinutes);
        Assert.Contains("the held replies folded", card, StringComparison.Ordinal);
    }

    [Fact]
    public void A_day_with_no_reply_queue_says_so_and_still_draws_every_section()
    {
        string card = TodayCard.Render(Inputs() with { QueuePath = null, Queue = "", Calls = [], Status = "", LeftForSteve = "", Decision = "", EssaysPath = null, Previous = "" });
        Assert.Contains("- Not read: no reply run of the day wrote linkedin-queue.md.", card, StringComparison.Ordinal);
        Assert.Contains("## CRITICAL sends\n\n- None.", card, StringComparison.Ordinal);
        Assert.Contains("No interview or call on the calendar in the next 72 hours.", card, StringComparison.Ordinal);
        Assert.DoesNotContain("## Essays", card, StringComparison.Ordinal);
        Assert.EndsWith("## Rejection reads\n\n- None.\n", card, StringComparison.Ordinal);
    }

    [Fact]
    public void Must_answers_come_from_the_company_s_notes_file()
    {
        var notes = new List<(string, string)> { ("NOTES-JACKHENRY.md", "# Jack Henry\nUNANSWERED IN WRITING: range (asked 10/20)\n"), ("NOTES-ACME.md", "# Acme\nno card\n") };
        Assert.Equal("Must-answers: UNANSWERED IN WRITING: range (asked 10/20) (NOTES-JACKHENRY.md)", TodayCardSource.MustAnswers("Wed 11/11 10:00 AM Central, Jack Henry hiring manager call: zoom", notes));
        Assert.StartsWith("Must-answers: NOTES-ACME.md holds no UNANSWERED", TodayCardSource.MustAnswers("Interview with Acme", notes), StringComparison.Ordinal);
        Assert.StartsWith("Must-answers: no NOTES file", TodayCardSource.MustAnswers("Interview with Globex", notes), StringComparison.Ordinal);
    }

    [Fact]
    public void The_mail_part_escapes_every_character_and_draws_lists_and_quotes()
    {
        string html = TodayMail.Html("> RULES FIRST: x\n\n# TODAY, Tuesday 11/10\n\n## Replies\n\n- **Bo** <b>said</b> & more\n   > a quoted line\n1. CRITICAL, an email\n");
        Assert.DoesNotContain("RULES FIRST", html, StringComparison.Ordinal);
        Assert.Contains("<h1 style=\"font-size:20px\">TODAY, Tuesday 11/10</h1>", html, StringComparison.Ordinal);
        Assert.Contains("<li><b>Bo</b> &lt;b&gt;said&lt;/b&gt; &amp; more</li>", html, StringComparison.Ordinal);
        Assert.Contains(">a quoted line</blockquote>", html, StringComparison.Ordinal);
        Assert.Contains("<li>1. CRITICAL, an email</li>", html, StringComparison.Ordinal);
        Assert.Equal("TODAY, Tue 11/10", TodayCard.Subject(Tuesday));
    }

    [Fact]
    public void The_card_s_hour_is_seven_o_clock_Central_on_both_sides_of_the_clock_change_and_unset_is_on()
    {
        Assert.True(CentralClock.IsTodayHour(new DateTimeOffset(2026, 11, 10, 13, 30, 0, TimeSpan.Zero)));
        Assert.True(CentralClock.IsTodayHour(new DateTimeOffset(2026, 10, 30, 12, 30, 0, TimeSpan.Zero)));
        Assert.False(CentralClock.IsTodayHour(new DateTimeOffset(2026, 11, 10, 12, 30, 0, TimeSpan.Zero)));
        Assert.Equal("on", TodayCardTimer.Mode(null));
        Assert.Equal("write", TodayCardTimer.Mode(" Write "));
        Assert.Equal("off", TodayCardTimer.Mode("OFF"));
    }

    [Fact]
    public async Task The_timer_replaces_the_file_whole_mails_his_own_address_once_and_logs_the_gmail_id()
    {
        using var scratch = new Scratch();
        scratch.Write("TODAY.md", Previous);
        scratch.Write("nightly/2026-11-10/0400-reply-opus-5-5/linkedin-queue.md", Queue);
        scratch.Write("nightly/2026-11-10/0400-reply-opus-5-5-dry/linkedin-queue.md", "<!-- reply-queue: 2 -->\n## A. x\n- **Dry Run** | - | - | held: dry\n");
        scratch.Write("nightly/2026-11-10/0400-reply-opus-5-5/drafts/gmail-acme.txt", "Ann,\nTuesday works.");
        scratch.Write("nightly/2026-11-10/0400-reply-opus-5-5/drafts/gmail-acme.gate.md", "> RULES FIRST\n\nUNANSWERED IN WRITING: range\n");
        scratch.Write("nightly/2026-11-10/0400-reply-opus-5-5/drafts/rejection-ask-umbrella.txt", "Ed, thanks.");
        scratch.Write("nightly/2026-11-10/0400-reply-opus-5-5/drafts/rejection-ask-umbrella.DO-NOT-SEND.txt", "Steve: do not send");
        scratch.Write("dispatch/STATUS.md", Status);
        var sender = new Sender();
        var time = new FixedTime(new DateTimeOffset(2026, 11, 10, 13, 30, 4, TimeSpan.Zero));
        var source = new TodayCardSource(scratch.Store, new NoHttp(), time);

        string line = await TodayCardTimer.WriteAsync(scratch.Store, source, sender, time, "07:30", CancellationToken.None, (id, _) => Task.FromResult(id == "m1" ? "INBOX,SENT,UNREAD" : "wrong id"));

        string card = File.ReadAllText(Path.Combine(scratch.Root, "TODAY.md"));
        Assert.StartsWith(TodayCard.RulesFirst, card, StringComparison.Ordinal);
        Assert.Contains("Gate: UNANSWERED IN WRITING: range", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Dry Run", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Umbrella", card, StringComparison.Ordinal);
        Assert.Contains("Interviews: not read, the Function holds no Google settings", card, StringComparison.Ordinal);
        var mail = Assert.Single(sender.Sent);
        Assert.Equal("stevenstout11@gmail.com", mail.To);
        Assert.Equal("TODAY, Tue 11/10", mail.Subject);
        Assert.Equal(card, mail.Text);
        Assert.Equal("2026-11-10 07:30:04 07:30: written " + new FileInfo(Path.Combine(scratch.Root, "TODAY.md")).Length + " B, " + TodayCard.Minutes(card) + " min, mailed to stevenstout11@gmail.com, gmail m1 thread t1, labels INBOX,SENT,UNREAD", line);
        Assert.True(await TodayCardTimer.MailedTodayAsync(scratch.Store, new DateTime(2026, 11, 10, 7, 45, 0), CancellationToken.None));
        Assert.False(await TodayCardTimer.MailedTodayAsync(scratch.Store, new DateTime(2026, 11, 11, 7, 30, 0), CancellationToken.None));
    }

    private static TodayInputs Inputs() => new(
        Tuesday,
        "nightly/2026-11-10/0400-reply-opus-5-5/linkedin-queue.md",
        Queue,
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["nightly/2026-11-10/0400-reply-opus-5-5/drafts/gmail-acme.txt"] = "Ann,\nTuesday works.\n",
            ["nightly/2026-11-10/0400-reply-opus-5-5/drafts/li-globex.txt"] = "Bo, thanks.",
            ["nightly/x/li-initech.txt"] = "Di, hello.",
            ["nightly/2026-11-10/0400-reply-opus-5-5/drafts/rejection-ask-umbrella.txt"] = "Ed, Thanks for letting me know. What would have made the difference? Thanks, Steve",
            ["nightly/2026-11-10/0400-reply-opus-5-5/drafts/rejection-ask-hooli.txt"] = "Flo, thanks.",
        },
        new HashSet<string>(StringComparer.Ordinal) { "nightly/2026-11-10/0400-reply-opus-5-5/drafts/rejection-ask-umbrella.txt", "nightly/2026-11-03/r/rejection-ask-initrode.txt" },
        new Dictionary<string, string>(StringComparer.Ordinal) { ["nightly/2026-11-10/0400-reply-opus-5-5/drafts/gmail-acme.txt"] = "UNANSWERED IN WRITING: range." },
        Status,
        Left,
        "- Allen Kadilis | Steve, Mon 11/9: \"Do not send that\"\n- LINQ | the same\n",
        "- HUNTER_KEY: run the key file once\n",
        "- Pick the Tuesday slot?\n- Second decision\n",
        "nightly/2026-11-10/APPLY-ESSAYS-2026-11-10.md",
        "> RULES FIRST: essays\n\n# Essays\n\nQuestion one, in full.\n",
        Previous,
        ["Tue 11/10 2:00 PM Central, Interview with Acme: Google Meet x (Google Calendar). Must-answers: range NOT IN WRITING (NOTES-ACME.md)"]);

    private static int Count(string text, string part) => (text.Length - text.Replace(part, string.Empty, StringComparison.Ordinal).Length) / part.Length;

    private static string Repo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "server", "JobHunt.Server.Tests")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("server/JobHunt.Server.Tests not found above " + AppContext.BaseDirectory);
    }

    private sealed class Sender : IMailSender
    {
        public List<OutgoingMail> Sent { get; } = [];

        public Task<SentMail> SendAsync(OutgoingMail mail, CancellationToken ct)
        {
            Sent.Add(mail);
            return Task.FromResult(new SentMail("m1", "t1", null));
        }
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("no network in this test");
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
