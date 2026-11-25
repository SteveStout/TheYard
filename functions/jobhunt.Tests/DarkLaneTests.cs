using JobHunt.Application.Features.Watch;
using JobHunt.Domain.Features.Card;
using JobHunt.Domain.Features.Watch;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The dark-lane alarm (audit1124b F2): a sending lane with no verified send for three days, and a hold on it or work
/// due, is one DARK line, first on NOT-WORKING.md and first on the 07:30 card, never "Needs you: nothing". The judge is
/// pure; the writer runs on a share born and thrown away with the test, and the 11/12 to 11/24 rows are a copy in
/// Fixtures/dark (RULE 79).
/// </summary>
public sealed class DarkLaneTests
{
    private static readonly DateOnly Tuesday = new(2026, 11, 24);
    private static readonly HashSet<string> NoItems = [];

    private const string MessagesHold = "Lane: server6am messages\nHeld: 2026-11-19 14:02 CST\nSource: check_empty_runs.py\n"
        + "Reason: 8 empty runs in a row since 11/12, top reasons no accepted invitee at the company (105), BLACKLIST HIT (34), no tracker row names them (21)\n"
        + "Heal: 30-heal-server6am-messages-20261119.md\n\nA hold only ever stops sending.\n";

    /// <summary>One 6 AM close row and its stamp, as the program writes them to state.md.</summary>
    private static string Row(DateOnly day, string lanes) =>
        $"## LANE: server6am{day:MMdd}-0600 CLOSED 06:10 Central. server6am: {lanes}  | CLOSED |\n<!-- AID:{day:yyyyMMdd}-121000Z-server6am-0600-close -->\n\n";

    private static DarkReads Reads(string state, string[]? holds = null, string emptyRuns = "", HashSet<string>? queued = null, HashSet<string>? proposed = null, string tracker = "", string passLog = "") =>
        new(state, new Dictionary<string, string>(), holds ?? [], emptyRuns, tracker, passLog, queued ?? NoItems, proposed ?? NoItems);

    private static string Fixture() => File.ReadAllText(Path.Combine(Repo(), "functions", "jobhunt.Tests", "Fixtures", "dark", "state-1112-1124.md"));

    [Fact]
    public void A_lane_held_for_three_days_is_the_first_line()
    {
        string state = Row(Tuesday.AddDays(-3), "invites: 2 sent verified of 9, 30 invites in 7 days, 4 left out messages: 0 sent verified of 0, 12 left out")
            + Row(Tuesday.AddDays(-2), "messages: held, held by the breaker (J:\\dispatch\\held\\server6am-messages.hold)")
            + Row(Tuesday.AddDays(-1), "messages: held, held by the breaker (J:\\dispatch\\held\\server6am-messages.hold)");
        var lines = DarkLanes.Lines(Reads(state, [MessagesHold], queued: ["30-heal-server6am-messages-20261119.md"]), Tuesday);

        string line = Assert.Single(lines);
        Assert.StartsWith("DARK: LinkedIn messages (6 AM): 0 sent in 3 days. Why: held since 2026-11-19 14:02 CST, 8 empty runs in a row since 11/12", line, StringComparison.Ordinal);
        Assert.EndsWith("Fix: 30-heal-server6am-messages-20261119.md is queued.", line, StringComparison.Ordinal);
    }

    private const string Expired = "> RULES FIRST\n\n# SIGN-IN\n\nState: expired\nSince: 2026-11-24 08:58 CST\nLast read: 2026-11-25 05:15 CST by canary.py\n"
        + "Seen: Failed to authenticate: OAuth session expired and could not be refreshed\n";

    [Fact]
    public void An_expired_claude_sign_in_is_its_own_first_line_and_names_the_double_click()
    {
        string state = Row(Tuesday.AddDays(-3), "messages: held, held by the breaker (J:\\dispatch\\held\\server6am-messages.hold)")
            + Row(Tuesday.AddDays(-1), "messages: held, held by the breaker (J:\\dispatch\\held\\server6am-messages.hold)");
        var reads = Reads(state, [MessagesHold]) with { SignIn = Expired };
        var lines = DarkLanes.Lines(reads, Tuesday);

        Assert.Equal(2, lines.Count);
        Assert.StartsWith("DARK: the claude sign-in on WickedAwsome: expired since 2026-11-24 08:58 CST (\"Failed to authenticate: OAuth session expired", lines[0], StringComparison.Ordinal);
        Assert.Contains("Why: the CLI's sign-in expired, not a lane failing.", lines[0], StringComparison.Ordinal);
        Assert.EndsWith("Fix: double-click \"Sign in to Claude\" on the WickedAwsome desktop and type /login.", lines[0], StringComparison.Ordinal);
        Assert.StartsWith("DARK: LinkedIn messages (6 AM):", lines[1], StringComparison.Ordinal);

        // Signed in, or never read, is no line at all.
        Assert.Null(DarkLanes.SignInLine(Expired.Replace("State: expired", "State: signed in", StringComparison.Ordinal)));
        Assert.Null(DarkLanes.SignInLine(string.Empty));
    }

    [Fact]
    public void A_lane_that_sent_yesterday_is_not_dark()
    {
        string state = Row(Tuesday.AddDays(-5), "invites: 0 sent verified of 12, 4 left out")
            + Row(Tuesday.AddDays(-1), "invites: 3 sent verified of 14, 41 invites in 7 days, 16 left out");
        Assert.Empty(DarkLanes.Lines(Reads(state), Tuesday));
    }

    [Fact]
    public void A_quiet_lane_with_no_hold_is_not_dark()
    {
        string state = Row(Tuesday.AddDays(-6), "messages: 1 sent verified of 1, 0 left out")
            + string.Concat(Enumerable.Range(1, 5).Select(d => Row(Tuesday.AddDays(-d), "messages: 0 sent verified of 0, 0 left out")));
        Assert.Empty(DarkLanes.Lines(Reads(state), Tuesday));

        // The same quiet days with a hold on the lane are dark: a held lane never sends, whatever was due.
        Assert.Single(DarkLanes.Lines(Reads(state, [MessagesHold]), Tuesday));
    }

    [Fact]
    public void The_line_never_reads_needs_you_nothing_with_a_hold_present()
    {
        string state = Row(Tuesday.AddDays(-4), "apply: 0 sent verified of 7, 12 left out");
        string hold = "Lane: server6am apply\nHeld: 2026-11-23 12:14 CST\nSource: check_empty_runs.py\nReason: 3 empty runs in a row since 11/20\nHeal: none (past the budget of 2 code heals today)\n";
        string line = Assert.Single(DarkLanes.Lines(Reads(state, [hold]), Tuesday));
        Assert.DoesNotContain("Needs you: nothing", line, StringComparison.Ordinal);
        Assert.EndsWith("Fix: not yet found.", line, StringComparison.Ordinal);

        string before = "> RULES FIRST\n\n# NOT WORKING\n\nNOT WORKING: server6am apply since 11/23 12:14. Tried: the day's 2 code heals were spent. Needs you: nothing.\nNOT WORKING: W12 Merger since 11/20 08:43. Tried: 2 heals (watch:w12-merger). Needs you: nothing.\n";
        string after = WatchDark.Render(before, [line]);
        Assert.Contains("NOT WORKING: server6am apply since 11/23 12:14. Tried: the day's 2 code heals were spent. Fix: not yet found.\n", after, StringComparison.Ordinal);
        Assert.Contains("NOT WORKING: W12 Merger since 11/20 08:43. Tried: 2 heals (watch:w12-merger). Needs you: nothing.\n", after, StringComparison.Ordinal);
        Assert.DoesNotContain(after.Split('\n'), l => l.StartsWith("NOT WORKING: server6am apply", StringComparison.Ordinal) && l.Contains("Needs you: nothing", StringComparison.Ordinal));
    }

    [Fact]
    public void With_the_rows_of_11_12_to_11_24_the_messages_lane_reads_12_days()
    {
        var reads = Reads(Fixture(), [MessagesHold], "EMPTY RUN: server6am invites, 7 runs, since 11/19. Top reasons: already invited (35), over the day's room, stays queued (19)\n");
        string messages = DarkLanes.Line(DarkLanes.Lanes[0], reads, Tuesday)!;
        Assert.StartsWith("DARK: LinkedIn messages (6 AM): 0 sent in 12 days. Why: held since 2026-11-19 14:02 CST, 8 empty runs", messages, StringComparison.Ordinal);

        // The invites lane last sent on 11/19 and has had work due since: five days, its reasons off EMPTY-RUNS.md.
        string invites = DarkLanes.Line(DarkLanes.Lanes[1], reads, Tuesday)!;
        Assert.Equal("DARK: LinkedIn invites (6 AM): 0 sent in 5 days. Why: top reasons already invited (35), over the day's room, stays queued (19). Fix: not yet found.", invites);
        Assert.Equal(messages, DarkLanes.Lines(reads, Tuesday)[0]);
    }

    [Fact]
    public void The_campaign_counts_its_tracker_sends_and_a_pounce_row_is_judged_by_its_note()
    {
        const string tracker = "Count,Person,Company,Communication,Date,Phone/Email,Notes\n3855,Ann Lee,Acme,Email,11/23/2026,ann@acme.test,\"CAMPAIGN EMAIL SENT 8:00 AM CST 11/23\"\n";
        const string pass = "2026-11-19 08:00:12 scan: scanned 01:00 | on: 122 open, 3 eligible, 0 sent of 10 left today | held: 104\n";
        Assert.Null(DarkLanes.Line(DarkLanes.Lanes[4], Reads(string.Empty, tracker: tracker, passLog: pass), Tuesday));
        Assert.StartsWith("DARK: Campaign emails (8 AM): 0 sent in 5 days.", DarkLanes.Line(DarkLanes.Lanes[4], Reads(string.Empty, passLog: pass), Tuesday), StringComparison.Ordinal);

        string pounce = "## LANE: pounce-14718 CLOSED 11:42 CST Holds: none. DONE note J:\\nightly\\pounce\\20261120-173501Z-14718-DONE.md | CLOSED |\n<!-- AID:20261120-174200Z-pounce-14718-close -->\n";
        Assert.Equal(["nightly/pounce/20261120-173501Z-14718-DONE.md"], DarkLanes.NotesWanted(pounce, Tuesday));
        var notes = new Dictionary<string, string> { ["nightly/pounce/20261120-173501Z-14718-DONE.md"] = "invites: held\napply: 0 sent verified of 1, 0 left out\n" };
        var reads = Reads(pounce) with { Notes = notes };
        Assert.Equal("empty", Assert.Single(DarkLanes.Runs(pounce, notes, Tuesday)).Verdict);
        Assert.StartsWith("DARK: Pounce applications: 0 sent in 4 days.", DarkLanes.Line(DarkLanes.Lanes[5], reads, Tuesday), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_writer_puts_the_dark_lines_on_top_of_NOT_WORKING_md_and_the_card_leads_with_them()
    {
        using var s = new Scratch();
        s.Write("state.md", Fixture());
        s.Write("dispatch/held/server6am-messages.hold", MessagesHold);
        s.Write("dispatch/NOT-WORKING.md", "> RULES FIRST: read systemRules.md before acting on this file (MASTER RULE: if stuck or a tech glitch happens, retry, then reread the .md rules).\n\n# NOT WORKING: one line a failure the heal ladder could not fix (the source of the card's first lines)\n\nNOT WORKING: leaders since 11/23 07:08. Tried: 2 heals (empty:leaders). Needs you: nothing.\n");
        var now = new DateTimeOffset(2026, 11, 24, 13, 30, 0, TimeSpan.Zero);

        var dark = await new WatchDark(s.Store).RunAsync(now, default);
        string file = s.Text(WatchDark.Path);
        string[] lines = file.Split('\n');
        Assert.StartsWith("> RULES FIRST", lines[0], StringComparison.Ordinal);
        Assert.Equal(dark[0], lines[4]);
        Assert.StartsWith("DARK: LinkedIn messages (6 AM): 0 sent in 12 days.", lines[4], StringComparison.Ordinal);
        Assert.Contains("NOT WORKING: leaders since 11/23 07:08.", file, StringComparison.Ordinal);

        await new WatchDark(s.Store).RunAsync(now.AddMinutes(15), default);
        Assert.Equal(file, s.Text(WatchDark.Path));

        Assert.Equal(DarkLanes.Prefix, TodayCard.DarkPrefix);
        Assert.Equal(WatchDark.Path, TodayCard.NotWorkingPath);
        var inputs = new TodayInputs(new DateTime(2026, 11, 25, 7, 30, 0), null, string.Empty, new Dictionary<string, string>(), new HashSet<string>(),
            new Dictionary<string, string>(), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, null, string.Empty, string.Empty, [])
        { Dark = TodayCard.DarkLines(file) };
        string[] card = TodayCard.Render(inputs).Split('\n');
        Assert.StartsWith("# TODAY, ", card[2], StringComparison.Ordinal);
        Assert.Equal(dark[0], card[4]);
    }

    [Theory]
    [InlineData("3 sent verified of 14, 41 invites in 7 days, 16 left out", "done")]
    [InlineData("2 sent verified of 14, 1 UNVERIFIED, read the page, 24 invites in 7 days, 23 left out", "done")]
    [InlineData("1 leaders written (1 with a public address), 0 feed blocks, 4 skipped", "done")]
    [InlineData("3 queued, 0 left out by the gates", "done")]
    [InlineData("0 sent verified of 12, 38 invites in 7 days, 4 left out", "empty")]
    [InlineData("0 sent verified of 0, 43 left out", "empty")]
    [InlineData("0 leaders written (0 with a public address), 0 feed blocks, 3 skipped", "empty")]
    [InlineData("held, no recount, no invite", "empty")]
    [InlineData("held, stop condition browser_down on the recount", "empty")]
    [InlineData("0 sent verified of 0, 0 left out", "quiet")]
    [InlineData("0 two-way threads spooled for the network log, 0 left out", "quiet")]
    [InlineData("dry, 0 of 0 rows read, 66 left out by the gates", null)]
    [InlineData("held, held by the breaker (J:\\dispatch\\held\\server6am-invites.hold): 3 empty runs in a row", null)]
    [InlineData("held, reply1110-0400 LIVE on LinkedIn", null)]
    [InlineData("not run, sign-in expired: the claude CLI on WickedAwsome answered \"Failed to authenticate: OAuth session expired\"", null)]
    [InlineData("held, the recount gave no result: Failed to authenticate: OAuth session expired and could not be refreshed", null)]
    [InlineData("Holds: none. DONE note J:\\nightly\\pounce\\x-DONE.md", null)]
    public void A_run_is_judged_as_check_empty_runs_judges_it(string words, string? verdict) =>
        Assert.Equal(verdict, DarkLanes.Verdict(words, outbound: true));

    [Fact]
    public void A_bookkeeping_hold_is_not_work_due_and_a_6_AM_row_splits_into_its_lanes()
    {
        Assert.Null(DarkLanes.Verdict("3 drafts held for his word", outbound: false));
        string state = Row(Tuesday.AddDays(-1), "feed: 3 queued, 0 left out by the gates commentline: 0 of 0 edits verified, 3 left out invites: held, no recount, no invite messages: 0 sent verified of 0, 66 left out apply: 1 sent verified of 1, 0 left out");
        var runs = DarkLanes.Runs(state, new Dictionary<string, string>(), Tuesday);
        Assert.Equal(["server6am feed", "server6am commentline", "server6am invites", "server6am messages", "server6am apply"], runs.Select(r => r.Lane));
        Assert.Equal(["done", "empty", "empty", "empty", "done"], runs.Select(r => r.Verdict));
    }

    [Fact]
    public void A_leaders_row_counts_and_a_row_outside_the_window_is_not_read()
    {
        string leaders = "## LANE: leaders-20261119 CLOSED 06:53 CST (rung L nightly): 0 leaders written (0 with a public address), 0 feed blocks, 9 skipped. | CLOSED |\n<!-- AID:20261119-125303Z-leaders-close -->\n\n"
            + "## LANE: leaders-20261121 CLOSED 06:53 CST (rung L nightly): 0 leaders written (0 with a public address), 0 feed blocks, 3 skipped. | CLOSED |\n<!-- AID:20261121-125303Z-leaders-close -->\n\n";
        string line = DarkLanes.Line(DarkLanes.Lanes[3], Reads(leaders), Tuesday)!;
        Assert.Equal("DARK: Leader emails (6:50 AM): 0 sent in 5 days. Why: the last run read \"06:53 CST (rung L nightly): 0 leaders written (0 with a public address), 0 feed blocks, 3 skipped\". Fix: not yet found.", line);

        string old = Row(Tuesday.AddDays(-DarkLanes.Window - 1), "messages: 0 sent verified of 0, 12 left out");
        Assert.Empty(DarkLanes.Runs(old, new Dictionary<string, string>(), Tuesday));
        Assert.Empty(DarkLanes.Lines(Reads(old), Tuesday));
    }

    [Fact]
    public void The_fix_names_a_proposed_heal_or_the_empty_run_proposal_and_nothing_else()
    {
        string state = Row(Tuesday.AddDays(-4), "messages: 0 sent verified of 0, 12 left out");
        string held = DarkLanes.Line(DarkLanes.Lanes[0], Reads(state, [MessagesHold], proposed: ["30-heal-server6am-messages-20261119.md"]), Tuesday)!;
        Assert.EndsWith("Fix: 30-heal-server6am-messages-20261119.md is proposed, waiting on the triage pass.", held, StringComparison.Ordinal);

        string open = DarkLanes.Line(DarkLanes.Lanes[0], Reads(state, proposed: ["emptyrun-server6am-messages.md"]), Tuesday)!;
        Assert.EndsWith("Fix: emptyrun-server6am-messages.md is proposed, waiting on the triage pass.", open, StringComparison.Ordinal);
        Assert.EndsWith("Fix: not yet found.", DarkLanes.Line(DarkLanes.Lanes[0], Reads(state, proposed: ["emptyrun-server6am-invites.md"]), Tuesday), StringComparison.Ordinal);
    }

    [Fact]
    public void The_writer_drops_yesterdays_dark_lines_and_starts_a_missing_file_with_its_title()
    {
        string fresh = WatchDark.Render(string.Empty, []);
        Assert.Equal("> RULES FIRST: read systemRules.md before acting on this file (MASTER RULE: if stuck or a tech glitch happens, retry, then reread the .md rules).\n\n# NOT WORKING: one line a failure the heal ladder could not fix (the source of the card's first lines)\n\n", fresh);

        string yesterday = WatchDark.Render(fresh, ["DARK: LinkedIn messages (6 AM): 0 sent in 11 days. Why: x. Fix: not yet found."]);
        string today = WatchDark.Render(yesterday + "NOT WORKING: W2 pounce since 11/20 08:00. Tried: nothing. Needs you: nothing.\n", ["DARK: LinkedIn messages (6 AM): 0 sent in 12 days. Why: x. Fix: not yet found."]);
        Assert.DoesNotContain("11 days", today, StringComparison.Ordinal);
        Assert.Equal(1, today.Split('\n').Count(l => l.StartsWith(DarkLanes.Prefix, StringComparison.Ordinal)));
        Assert.EndsWith("Fix: not yet found.\n\nNOT WORKING: W2 pounce since 11/20 08:00. Tried: nothing. Needs you: nothing.\n", today, StringComparison.Ordinal);
        Assert.Equal(fresh, WatchDark.Render(today.Replace("NOT WORKING: W2 pounce since 11/20 08:00. Tried: nothing. Needs you: nothing.\n", string.Empty, StringComparison.Ordinal), []));
    }

    [Fact]
    public void A_card_with_no_dark_lane_opens_as_it_did()
    {
        Assert.Empty(TodayCard.DarkLines("> RULES FIRST\n\n# NOT WORKING\n\nNOT WORKING: leaders since 11/23 07:08. Tried: 2 heals (empty:leaders). Needs you: nothing.\n"));
        var inputs = new TodayInputs(new DateTime(2026, 11, 25, 7, 30, 0), null, string.Empty, new Dictionary<string, string>(), new HashSet<string>(),
            new Dictionary<string, string>(), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, null, string.Empty, string.Empty, []);
        string[] card = TodayCard.Render(inputs).Split('\n');
        Assert.StartsWith("Written 07:30 Central by the job hunt's Function", card[4], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_shadow_watch_tick_writes_the_dark_line_and_the_morning_card_reads_it_off_the_share()
    {
        using var s = new Scratch();
        s.Write("state.md", Fixture());
        s.Write("dispatch/held/server6am-messages.hold", MessagesHold);
        s.Write("dispatch/held/notes.txt", "Lane: server6am invites\n");
        s.Write("dispatch/ready/30-heal-server6am-messages-20261119.md", "Lane: heal-server6am-messages-20261119\n");
        s.Write("dispatch/SWITCHES.md", "## Switches\n");
        s.Write("dispatch/HEARTBEAT", "2026-11-24T13:20:00Z | dispatch.py\n");
        var now = new DateTimeOffset(2026, 11, 24, 13, 30, 0, TimeSpan.Zero);

        var result = await new WatchPass(s.Store, _ => null, new At(now)).RunAsync(WatchMode.Shadow, default);
        Assert.True(result.Written);
        Assert.Equal(0, result.Heals);
        string file = s.Text(WatchDark.Path);
        Assert.Contains("\nDARK: LinkedIn messages (6 AM): 0 sent in 12 days. Why: held since 2026-11-19 14:02 CST, ", file, StringComparison.Ordinal);
        Assert.Contains("Fix: 30-heal-server6am-messages-20261119.md is queued.\n", file, StringComparison.Ordinal);
        Assert.DoesNotContain("DARK: Applications", file, StringComparison.Ordinal);

        // Off reads nothing and writes nothing: the file stays as the shadow tick left it.
        await new WatchPass(s.Store, _ => null, new At(now.AddMinutes(15))).RunAsync(WatchMode.Off, default);
        Assert.Equal(file, s.Text(WatchDark.Path));

        var source = new JobHunt.Infrastructure.Features.Card.TodayCardSource(s.Store, new NoHttp(), new At(new DateTimeOffset(2026, 11, 25, 13, 30, 0, TimeSpan.Zero)));
        var inputs = await source.ReadAsync(default);
        Assert.Equal(TodayCard.DarkLines(file), inputs.Dark);
        string[] card = TodayCard.Render(inputs).Split('\n');
        Assert.StartsWith("DARK: LinkedIn messages (6 AM): 0 sent in 12 days.", card[4], StringComparison.Ordinal);

        // canary.py writes an expired sign-in: the next shadow tick puts it first, and the card leads with it.
        s.Write(DarkLanes.SignInPath, Expired);
        await new WatchPass(s.Store, _ => null, new At(now.AddMinutes(30))).RunAsync(WatchMode.Shadow, default);
        file = s.Text(WatchDark.Path);
        card = TodayCard.Render(await source.ReadAsync(default)).Split('\n');
        Assert.StartsWith("DARK: the claude sign-in on WickedAwsome: expired since 2026-11-24 08:58 CST", card[4], StringComparison.Ordinal);
        Assert.StartsWith("DARK: LinkedIn messages (6 AM):", card[5], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2026-11-19 08:00:12 scan: scanned 01:00 | on: 122 open, 0 eligible, 0 sent of 10 left today | held: 104", false)]
    [InlineData("2026-11-19 08:00:12 scan: scanned 01:00 | on: 122 open, 4 eligible, 4 sent of 10 left today | held: 104", false)]
    [InlineData("2026-11-19 08:00:12 scan: scanned 01:00 | on: 122 open, 4 eligible, 0 sent of 10 left today | held: 104", true)]
    [InlineData("2026-11-19 01:01:44 scan: 145 boards, 142 answered, 4 eligible, 0 sent of 10", false)]
    [InlineData("2026-10-19 08:00:12 scan: scanned 01:00 | on: 122 open, 4 eligible, 0 sent of 10 left today | held: 104", false)]
    public void The_campaign_is_dark_only_on_an_8_oclock_pass_that_had_a_lead_and_sent_none(string pass, bool dark)
    {
        var line = DarkLanes.Line(DarkLanes.Lanes[4], Reads(string.Empty, passLog: pass + "\n"), Tuesday);
        Assert.Equal(dark, line is not null);
        if (line is not null)
        {
            Assert.Equal("DARK: Campaign emails (8 AM): 0 sent in 5 days. Why: the last run read \"4 eligible, 0 sent\". Fix: not yet found.", line);
        }
    }

    private sealed class At(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("no network in this test");
    }

    private static string Repo()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "functions", "JobHunt.Domain")))
            {
                return dir.FullName;
            }
        }
        throw new DirectoryNotFoundException("the repository root");
    }
}
