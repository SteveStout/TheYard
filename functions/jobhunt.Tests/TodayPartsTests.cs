using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The readers under the morning card (TodayParts and TodayReads in JobHunt.Domain's Card slice): the newest real reply
/// run, the dispatcher's STATUS.md and LEFT-FOR-STEVE.md read the way dispatch.py writes them, blocks that keep a
/// bullet's indented lines and drop the items that ask nothing, the answered phrases, and a draft path read from
/// whichever root a lane wrote it from.
/// </summary>
public sealed class TodayPartsTests
{
    [Fact]
    public void The_newest_reply_run_is_the_latest_slot_that_wrote_a_queue_and_never_a_dry_one()
    {
        Assert.Equal("1125-reply-opus-5-5", TodayParts.NewestRun([("0400-reply-opus-5-5", true), ("1125-reply-opus-5-5", true), ("1300-reply-opus-5-5-dry", true), ("1400-reply-opus-5-5", false), ("0600-messages-sonnet-5-5", true)]));
        Assert.Null(TodayParts.NewestRun([("0400-reply-opus-5-5", false)]));
        Assert.Null(TodayParts.NewestRun([]));
    }

    [Fact]
    public void A_queue_line_outside_the_grammar_and_section_D_are_not_items_and_a_note_before_any_item_belongs_to_none()
    {
        var items = TodayParts.Items("<!-- reply-queue: 2 -->\n## A. x\nNote: before any item\n- Lee, a sentence\n- **Lee** | - | thread: linkedin 1 | held: why\nNote: after it\n## D. Not owed\n- **Free** | - | - | held: free text\n# Another top heading\n- **Out** | - | - | held: outside\n");
        var item = Assert.Single(items);
        Assert.Equal("Lee", item.Who);
        Assert.Equal(["after it"], item.Notes);
        Assert.False(item.IsEmail);
    }

    [Fact]
    public void Who_reads_the_person_and_company_or_whichever_one_the_line_names()
    {
        Assert.Equal("Ann (Acme)", new TodayItem("A", "Ann", "Acme", "", "", "", []).Who);
        Assert.Equal("Acme", new TodayItem("C", "", "Acme", "", "", "", []).Who);
        Assert.Equal("-", new TodayItem("C", "", "", "", "", "", []).Who);
    }

    [Fact]
    public void The_lanes_read_running_a_real_pause_held_and_a_count_of_proposed_with_those_asked_and_leave_the_ready_queue_out()
    {
        const string status = "Written Tue 11/10 07:29:00 CST by dispatch.py.\n## Running\n- a: opus\n## Paused\n- until 09:05 CST (limit)\n## Ready (2)\n- r1.md\n## Proposed (queue it?)\n- p1.md\n## Held\n- h1: stalled\n## Closed in the last 24 hours\n- c1: 3 min\n";
        var none = new Dictionary<string, string>(StringComparer.Ordinal);
        Assert.Equal(["Running: a: opus", "Paused: until 09:05 CST (limit)", "Proposed: 1 (the list is in dispatch/STATUS.md); none needs your yes today.", "Held: h1: stalled"], TodayParts.Lanes(status, none));
        Assert.Equal(["Running: a: opus", "Paused: until 09:05 CST (limit)", "Proposed: 1 (the list is in dispatch/STATUS.md); 1 needs your yes:", "Proposed: p1.md. Queue it? (lane-a asks)", "Held: h1: stalled"], TodayParts.Lanes(status, new Dictionary<string, string>(StringComparer.Ordinal) { ["p1.md"] = "lane-a" }));
        Assert.Equal("Written Tue 11/10 07:29:00 CST by dispatch.py", TodayParts.Written(status));
        Assert.Null(TodayParts.Written("## Running\n"));
        Assert.Empty(TodayParts.Lanes("## Paused\n- no\n## Proposed (queue it?)\n- none\n", none));
    }

    [Fact]
    public void A_closing_in_december_read_in_january_belongs_to_the_year_before()
    {
        var sections = TodayParts.Left("## lane-a, closed Thu 12/31 23:50 CST\n\n## LEFT FOR STEVE\n- an ask\n", new DateTime(2027, 1, 1, 7, 30, 0));
        var section = Assert.Single(sections);
        Assert.Equal(new DateTime(2026, 12, 31, 23, 50, 0), section.Closed);
        Assert.Equal("lane-a", section.Lane);
        Assert.Equal(["- an ask"], section.Blocks);
    }

    [Fact]
    public void A_block_keeps_its_indented_lines_and_the_items_that_ask_nothing_are_dropped()
    {
        var blocks = TodayParts.Blocks(["- one", "  more of one", "", "  still one", "- none", "- Nothing.", "FOLLOW-ON: x.md", "- CLOSED row spooled 10:47 CST", "> a quote", "OWED:", "- two"]);
        Assert.Equal(["- one\n  more of one\n\n  still one", "OWED:", "- two"], blocks);
        Assert.Empty(TodayParts.Blocks(["", "  an orphan line"]));
    }

    [Fact]
    public void An_answered_phrase_is_the_text_before_its_bar_and_a_phrase_too_short_to_be_safe_is_ignored()
    {
        var answered = TodayParts.Answered("# ANSWERED\n\n- Allen Kadilis | his words\n- AB | too short\n- none\n");
        Assert.Equal(["Allen Kadilis"], answered);
        Assert.True(TodayParts.IsAnswered("an email to allen kadilis at LINQ", answered));
        Assert.False(TodayParts.IsAnswered("an email to Allen at LINQ", answered));
    }

    [Theory]
    [InlineData("J:\\nightly\\2026-11-09\\ah2\\ask.txt.", "nightly/2026-11-09/ah2/ask.txt")]
    [InlineData("/mnt/j/nightly/x/ask.txt", "nightly/x/ask.txt")]
    [InlineData("$JOBHUNT/nightly/x/ask.txt,", "nightly/x/ask.txt")]
    [InlineData("nightly/x/ask.txt", "nightly/x/ask.txt")]
    public void A_draft_path_is_read_from_the_share_s_root_whichever_root_the_lane_wrote(string written, string root) =>
        Assert.Equal(root, TodayReads.ShareRoot(written));

    [Fact]
    public void The_carried_reads_end_at_the_next_heading_and_a_file_without_the_heading_carries_none()
    {
        const string previous = "# TODAY\n\n## Rejection reads\n\n- one read. Draft: nightly/a.txt\n  its ask\n\n## Later heading\n\n- not a read\n";
        Assert.Equal(["- one read. Draft: nightly/a.txt\n  its ask"], TodayReads.Carried(previous));
        Assert.Equal("nightly/a.txt", TodayReads.DraftOf(TodayReads.Carried(previous)[0]));
        Assert.Equal(string.Empty, TodayReads.DraftOf("- a read with no draft"));
        Assert.Empty(TodayReads.Carried("# TODAY\n\n## The lanes\n\n- Mon 11/09 08:39 CST, dispatch: x\n"));
    }

    [Fact]
    public void A_rejection_with_no_read_line_and_no_draft_is_still_shown_held()
    {
        var item = new TodayItem("E", "Ed", "Umbrella", "Count 9", "", "no draft yet", []);
        var inputs = new TodayInputs(new DateTime(2026, 11, 10, 7, 30, 0), null, "", new Dictionary<string, string>(), new HashSet<string>(), new Dictionary<string, string>(), "", "", "", "", "", null, "", "", []);
        var reads = TodayReads.Rebuild(inputs, [item], []);
        Assert.Equal(["- Ed (Umbrella), a rejection from a live room (the queue holds no read line) Held: no draft yet"], reads);
    }

    [Fact]
    public async Task The_calendar_read_says_why_it_read_nothing_when_the_function_holds_no_google_settings()
    {
        var lines = await CalendarWeek.ReadAsync(new NoHttp(), DateTimeOffset.UnixEpoch, 3, "none", CancellationToken.None);
        Assert.Equal(["Interviews: not read, the Function holds no Google settings"], lines);
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("no network in this test");
    }
}
