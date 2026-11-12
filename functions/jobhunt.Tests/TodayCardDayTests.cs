using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The 07:30 card drawn from the shapes the share held on Thu 11/12 (Fixtures/today: the 4 AM reply queue's sections A
/// and E, the Northbound gate file, the dispatcher's STATUS.md, ANSWERED.md and the LEFT-FOR-STEVE sections the card
/// read, people cut to a first name and an initial): the red flag verdict and the fit line lead every reply.
/// </summary>
public sealed class TodayCardDayTests
{
    private static readonly DateTime Thursday = new(2026, 11, 12, 7, 30, 0);

    private const string Run = "nightly/2026-11-12/0400-reply-opus-5-5";

    [Fact]
    public void The_red_flag_verdict_and_the_fit_line_lead_every_reply_drafted_or_held()
    {
        string card = TodayCard.Render(Day());

        // Held, with its draft kept: the gate file beside the kept draft gives the verdict, then the fit note, then
        // what is still unanswered in writing.
        Assert.Contains("- **Adediran S. (Northbound Talent Partners (client Beemo Automation))**, email (thread: gmail 1a1c40cd7419599a): RED FLAG: RED, 5 flags: NO NAMED DECISION-MAKER (his 11/10 \"I will confirm the specific decision-maker\" is still unconfirmed in his 5:57 PM CST 11/11 message, gmail 1a1cd0bb0f45b983)\n  FIT: PAY PASS $180,000 to $220,000 (Beemo's own careers page", card, StringComparison.Ordinal);
        Assert.Contains("  Gate: UNANSWERED IN WRITING: who makes the hiring call (asked 11/10, promised, not given)", card, StringComparison.Ordinal);
        // Held with no gate read: the verdict is the held reason's own opening clause, and the reason does not say it twice.
        Assert.Contains("- **Nitish (Quantum World Technologies)**, email (thread: gmail 1a1cbf33703305d6): RED FLAG: RED, 4 flags: F3 the same onsite Lisle role Steve declined through Brajesh 9/29 (Counts 2278 and 2279)\n  FIT: PAY NONE (no band published or passed in", card, StringComparison.Ordinal);
        Assert.Contains("  Held: recommended: no reply; a remote-only decline is kept at nightly/2026-11-12/0400-reply-opus-5-5/drafts/nitish-quantumworld.txt for his word only\n", card, StringComparison.Ordinal);
    }

    [Fact]
    public void A_red_draft_is_never_a_critical_send_and_a_reply_with_no_fit_line_says_so()
    {
        const string queue = "<!-- reply-queue: 2 -->\n## A. Inbound\n"
            + "- **Ann L.** | Acme | thread: gmail 1a | draft: nightly/2026-11-12/0400-reply-opus-5-5/drafts/gmail-acme.txt\n"
            + "- **Bo D.** | Globex | thread: linkedin 2-xyz | draft: nightly/2026-11-12/0400-reply-opus-5-5/drafts/li-globex.txt\n";
        var drafts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"{Run}/drafts/gmail-acme.txt"] = "Ann, which client is this for?",
            [$"{Run}/drafts/li-globex.txt"] = "Bo, thanks.",
        };
        var gates = new Dictionary<string, string>(StringComparer.Ordinal) { [$"{Run}/drafts/gmail-acme.txt"] = "RED FLAG: RED, 5 flags: F1 a gmail.com address | PROBE: \"Who decides?\" | FIT: PAY NONE | UNANSWERED IN WRITING: client" };
        string card = TodayCard.Render(Day() with { Queue = queue, Drafts = drafts, Gates = gates });

        Assert.Contains("## CRITICAL sends\n\n- None.\n", card, StringComparison.Ordinal);
        Assert.Contains("- **Ann L. (Acme)**, email (thread: gmail 1a): RED FLAG: RED, 5 flags: F1 a gmail.com address\n  FIT: PAY NONE\n  Gate: UNANSWERED IN WRITING: client\n  RED FLAG, held for your word. The draft, sent only on your word:\n   > Ann, which client is this for?\n", card, StringComparison.Ordinal);
        Assert.Contains("- **Bo D. (Globex)**, LinkedIn (thread: linkedin 2-xyz): FIT: NOT CHECKED\n  The draft, typed live in the thread for your send once you say \"send\" or edit it:\n   > Bo, thanks.\n", card, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_source_hands_the_card_every_leading_verdict_line_of_a_gate_file_and_the_gate_beside_a_kept_draft()
    {
        using var scratch = new Scratch();
        scratch.Write($"{Run}/linkedin-queue.md", "<!-- reply-queue: 2 -->\n## A. Inbound\n"
            + $"- **Ann L.** | Acme | thread: linkedin 2-a | draft: {Run}/drafts/li-acme.txt\n"
            + $"- **Cy F.** | Initech | thread: gmail 3c | held: RED FLAG: RED, 5 flags: F3; recommended: no reply; draft kept at {Run}/drafts/gmail-initech.txt for his word\n");
        scratch.Write($"{Run}/drafts/li-acme.txt", "Ann, thanks.");
        scratch.Write($"{Run}/drafts/li-acme.gate.md", "> RULES FIRST\n\nUNANSWERED IN WRITING: range\nRED FLAG: YELLOW, 3 flags: F4 no range | PROBE: \"What range?\"\nFIT: PAY NONE\n\n# DRAFT: the scan below\nRED FLAG: not this one\n");
        scratch.Write($"{Run}/drafts/gmail-initech.txt", "Cy, no thanks.");
        scratch.Write($"{Run}/drafts/gmail-initech.gate.md", "RED FLAG: RED, 5 flags: F3 the thread he declined | PROBE: \"Who decides?\" | FIT: NO ROLE ON RECORD | UNANSWERED IN WRITING: who decides\n\n# DRAFT\n");
        var time = new FixedTime(new DateTimeOffset(2026, 11, 12, 13, 30, 0, TimeSpan.Zero));

        string card = TodayCard.Render(await new TodayCardSource(scratch.Store, new NoHttp(), time).ReadAsync(CancellationToken.None));

        Assert.Contains("- **Ann L. (Acme)**, LinkedIn (thread: linkedin 2-a): RED FLAG: YELLOW, 3 flags: F4 no range\n  FIT: PAY NONE\n  Gate: UNANSWERED IN WRITING: range\n", card, StringComparison.Ordinal);
        Assert.DoesNotContain("not this one", card, StringComparison.Ordinal);
        Assert.Contains("- **Cy F. (Initech)**, email (thread: gmail 3c): RED FLAG: RED, 5 flags: F3 the thread he declined\n  FIT: NO ROLE ON RECORD\n  Gate: UNANSWERED IN WRITING: who decides\n  Held: recommended: no reply; draft kept at", card, StringComparison.Ordinal);
    }

    [Fact]
    public void Replies_run_oldest_first_by_the_queue_s_since_line_each_with_its_history_line()
    {
        const string queue = "<!-- reply-queue: 2 -->\n## A. Inbound\n"
            + "- **Ann L.** | Acme | thread: linkedin 2-a | held: text not read (a LinkedIn notice carries no text)\n"
            + "Note: since: 2026-11-12 01:06\n"
            + "Note: history: his first InMail, 1:06 AM 11/12\n"
            + "- **Bo D.** | Globex | thread: gmail 1b | held: a second touch waits on his word\n"
            + "Note: since: 2026-11-09 08:00\n"
            + "Note: history: Steve's connect note 11/5, the accept 11/9, nothing since\n";
        string card = TodayCard.Render(Day() with { Queue = queue });

        Assert.Contains("- **Bo D. (Globex)**, email (thread: gmail 1b): FIT: NOT CHECKED\n  Held: a second touch waits on his word\n  History: Steve's connect note 11/5, the accept 11/9, nothing since. On the card since 11/9.\n- **Ann L. (Acme)**", card, StringComparison.Ordinal);
        Assert.Contains("  History: his first InMail, 1:06 AM 11/12. On the card since 11/12.\n", card, StringComparison.Ordinal);
    }

    [Fact]
    public void With_no_since_line_the_order_comes_from_the_last_card_and_the_queue_s_own_dates()
    {
        const string queue = "<!-- reply-queue: 2 -->\n## A. Inbound\n"
            + "- **Ann L.** | Acme | thread: linkedin 2-a | held: text not read (a LinkedIn notice carries no text)\n"
            + "- **Cy F.** | Initech | thread: linkedin 3c | held: waits on his word\n"
            + "- **Ed R.** | Hooli | thread: gmail 4e | held: waits on his word for the range\n"
            + "- **Di N.** | - | Count 3291 | held: the standing recommendation is a thank-you (11/8 queue A.3)\n";
        const string previous = "# TODAY, Wednesday 11/11\n\n## Replies (nightly/2026-11-11/0400-reply-opus-5-5/linkedin-queue.md, section A)\n\n"
            + "- **Cy F. (Initech)**, LinkedIn (thread: linkedin 3c): FIT: NOT CHECKED\n  Held: waits on his word\n  History: new in this run's queue. On the card since 11/10.\n"
            + "- **Ed R. (Hooli)**, held: waits on his word for the range\n\n## Calls in the next 72 hours\n\n- **Ann L.** is named here, not under Replies\n";
        string card = TodayCard.Render(Day() with { Queue = queue, Previous = previous });

        string[] order = ["- **Di N.** (Count 3291)", "- **Cy F. (Initech)**", "- **Ed R. (Hooli)**", "- **Ann L. (Acme)**"];
        Assert.Equal(order, order.OrderBy(o => card.IndexOf(o, StringComparison.Ordinal)));
        Assert.Contains("  History: carried from an earlier card; the queue gives no history line. On the card since 11/8.\n", card, StringComparison.Ordinal);
        Assert.Contains("waits on his word\n  History: carried from an earlier card; the queue gives no history line. On the card since 11/10.\n", card, StringComparison.Ordinal);
        Assert.Contains("for the range\n  History: carried from an earlier card; the queue gives no history line. On the card since 11/11.\n", card, StringComparison.Ordinal);
        Assert.Contains("carries no text)\n  History: new in this run's queue. On the card since 11/12.\n", card, StringComparison.Ordinal);
    }

    [Fact]
    public void Section_A_shows_only_what_needs_his_word_and_the_rest_is_one_handled_line()
    {
        const string previous = "# TODAY, Wednesday 11/11\n\n## Replies (nightly/2026-11-11/0400-reply-opus-5-5/linkedin-queue.md, section A)\n\n"
            + "- **Jessica N. (CloudForge Solutions)**, held: RED FLAG: RED, 4 flags: F3 a thread he declined; recommended: no reply\n"
            + "- **Nenad N.**, held: not found in LinkedIn Messaging on the 11/9 read-back; the standing recommendation is no reply (11/4 queue A.7)\n";
        string card = TodayCard.Render(Day() with { Previous = previous });
        string replies = card[card.IndexOf("## Replies", StringComparison.Ordinal)..card.IndexOf("## Calls", StringComparison.Ordinal)];

        // The blacklist hit (PetDesk), the sales InMail (Edward Jones) and the two carried "no reply" items with no new
        // message since the last card go to one line, oldest first; the two new RED items stay, each for his word.
        Assert.Contains("- **Adediran S. (Northbound Talent Partners (client Beemo Automation))**", replies, StringComparison.Ordinal);
        Assert.Contains("- **Nitish (Quantum World Technologies)**", replies, StringComparison.Ordinal);
        Assert.Contains("- Handled, no action: 4 (Nenad N., Jessica N., Matthew S., Jovan R.).\n", replies, StringComparison.Ordinal);
        foreach (string name in new[] { "Jovan R.", "Matthew S.", "Jessica N.", "Nenad N." })
        {
            Assert.DoesNotContain("- **" + name, replies, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_carried_reply_with_no_new_message_shows_on_two_cards_at_most()
    {
        const string queue = "<!-- reply-queue: 2 -->\n## A. Inbound\n"
            + "- **Mike S.** | Temporal Technologies | Count 3591 | held: a follow-up after the connect note waits on Steve's word; carried, no new message from him in this window\n";
        static string Previous(string since) => "# TODAY, Wednesday 11/11\n\n## Replies (x, section A)\n\n- **Mike S. (Temporal Technologies)** (Count 3591): FIT: NOT CHECKED\n  Held: waits\n  History: x. On the card since " + since + ".\n";

        string second = TodayCard.Render(Day() with { Queue = queue, Previous = Previous("11/11") });
        string third = TodayCard.Render(Day() with { Queue = queue, Previous = Previous("11/10") });

        Assert.Contains("- **Mike S. (Temporal Technologies)** (Count 3591): FIT: NOT CHECKED\n", second, StringComparison.Ordinal);
        Assert.DoesNotContain("Handled, no action", second, StringComparison.Ordinal);
        Assert.DoesNotContain("- **Mike S.", third, StringComparison.Ordinal);
        Assert.Contains("## Replies (nightly/2026-11-12/0400-reply-opus-5-5/linkedin-queue.md, section A)\n\n- Handled, no action: 1 (Mike S.).\n", third, StringComparison.Ordinal);
    }

    [Fact]
    public void A_decision_prints_once_and_one_asked_on_three_cards_with_no_answer_closes_as_its_default()
    {
        string card = TodayCard.Render(Day());
        string left = card[card.IndexOf("## Left for you", StringComparison.Ordinal)..card.IndexOf("## One decision", StringComparison.Ordinal)];

        // B was asked by autohunt1109-L, again by autohunt1109-7 and autohunt1109-L-f1, and closed by autohunt1109-L-f3's
        // note: four cards, no answer in ANSWERED.md, so it is closed as its default, on one line. A is answered.
        Assert.Contains("- Closed as its default after 4 cards with no answer: **B. One email after an unanswered LinkedIn invite** (default: no).\n", left, StringComparison.Ordinal);
        Assert.Equal(1, Count(left.ToUpperInvariant(), "ONE EMAIL AFTER AN UNANSWERED LINKEDIN INVITE"));
        Assert.DoesNotContain("### autohunt1109-L-f1", left, StringComparison.Ordinal);
        Assert.DoesNotContain("so that line again", left, StringComparison.Ordinal);
        Assert.Contains("### autohunt1109-L-f3, closed Wed 11/11 18:20\n\n- **Optional, \"queue it\" or leave it: a 60-word check in the send**", left, StringComparison.Ordinal);
        Assert.Contains("### newapp1111, closed Wed 11/11 17:11", left, StringComparison.Ordinal);
    }

    [Fact]
    public void A_decision_two_lanes_left_in_the_same_day_prints_once_under_the_first()
    {
        const string text = "## lane-a, closed Wed 11/11 20:00 CST\n\n## LEFT FOR STEVE\n- **C. The long title rule for resumes.** Your yes. Default: skip.\n\n"
            + "## lane-b, closed Thu 11/12 06:00 CST\n\n## LEFT FOR STEVE\n- **C. The long title rule for resumes.** Still open. Default: skip.\n";
        string card = TodayCard.Render(Day() with { LeftForSteve = text });

        Assert.Equal(1, Count(card, "The long title rule for resumes"));
        Assert.Contains("### lane-a, closed Wed 11/11 20:00\n\n- **C. The long title rule for resumes.** Your yes. Default: skip.\n", card, StringComparison.Ordinal);
        Assert.DoesNotContain("### lane-b", card, StringComparison.Ordinal);
    }

    [Fact]
    public void The_proposed_list_prints_its_count_and_only_the_items_a_lane_asks_his_yes_on()
    {
        string card = TodayCard.Render(Day());
        string lanes = card[card.IndexOf("## The lanes", StringComparison.Ordinal)..card.IndexOf("## Left for you", StringComparison.Ordinal)];

        Assert.Contains("- Running: gmail403-1112: claude-opus-5-5, started 11:07 CST, attempt 1, last event 1 min ago\n- Proposed: 21 (the list is in dispatch/STATUS.md); 1 needs your yes:\n- Proposed: autohunt1109-L-f3-f1-v3-word-cap.md. Queue it? (autohunt1109-L-f3 asks)\n", lanes, StringComparison.Ordinal);
        Assert.DoesNotContain("fitgate1110-f2-card-prints-fit-line.md", lanes, StringComparison.Ordinal);
        Assert.Contains("- Proposed: 21 (the list is in dispatch/STATUS.md); none needs your yes today.\n", TodayCard.Render(Day() with { LeftForSteve = string.Empty }), StringComparison.Ordinal);
    }

    private static TodayInputs Day() => new(
        Thursday,
        $"{Run}/linkedin-queue.md",
        Fixture("linkedin-queue-2026-11-12.md"),
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [$"{Run}/drafts/adediran-beemo.txt"] = "Sunday,\n\nWho makes the final hiring decision on this role?\n\nSteve",
        },
        new HashSet<string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal) { [$"{Run}/drafts/adediran-beemo.txt"] = Leading(Fixture("adediran-beemo.gate.md")) },
        Fixture("status-2026-11-12.md"),
        Fixture("left-for-steve-2026-11-12.md"),
        Fixture("answered-2026-11-12.md"),
        string.Empty,
        string.Empty,
        null,
        string.Empty,
        string.Empty,
        []);

    private static int Count(string text, string part) => (text.Length - text.Replace(part, string.Empty, StringComparison.Ordinal).Length) / part.Length;

    /// <summary>A gate file's lines before its first heading, as the source hands them over.</summary>
    private static string Leading(string gate) => string.Join("\n", TodayParts.Lines(gate).TakeWhile(l => !l.StartsWith('#')).Where(l => l.Trim().Length > 0 && !l.StartsWith('>')));

    private static string Fixture(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string path = Path.Combine(dir.FullName, "functions", "jobhunt.Tests", "Fixtures", "today", name);
            if (File.Exists(path))
            {
                return File.ReadAllText(path);
            }
        }
        throw new InvalidOperationException("Fixtures/today/" + name + " not found above " + AppContext.BaseDirectory);
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
