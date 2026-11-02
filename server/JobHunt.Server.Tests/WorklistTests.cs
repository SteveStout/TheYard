namespace JobHunt.Server.Tests;

/// <summary>The queue, reply and apply parsers and the short-text checks, over fixtures cut from the real files.</summary>
public sealed class WorklistTests
{
    private const string Good = "Pat,\n\nI applied for the role at Acme. My work: theyard.stevenstout.biz/?r=abc123\n\nWould it be a bad idea to connect?\n\nThanks,\nSteve";

    private static IReadOnlyList<QueueBlock> Open() =>
        QueueBlocks.Open("QUEUE-linuxreach-2026-10-27-0858.md", Fixture.Read("queue.md"), [Fixture.Read("queue-status.md")]);

    [Fact]
    public void A_good_text_has_no_problems()
    {
        Assert.Empty(TextCheck.Problems(Good, invite: true));
    }

    [Fact]
    public void A_placeholder_text_counts_as_it_stands()
    {
        Assert.Empty(TextCheck.Problems("[First name],\n\nMy work: theyard.stevenstout.biz/?r=[code]\n\nWould it be a bad idea to connect?\n\nThanks,\nSteve", invite: true));
    }

    [Theory]
    [InlineData("\u2014 ", "em dash")]
    [InlineData("See https://example.com ", "other links")]
    public void A_text_with_a_tell_fails_by_name(string prefix, string reason)
    {
        Assert.Contains(TextCheck.Problems(prefix + Good, invite: true), problem => problem.Contains(reason, StringComparison.Ordinal));
    }

    [Fact]
    public void Too_many_words_and_too_many_characters_fail()
    {
        string long_ = Good + string.Concat(Enumerable.Repeat(" word", 60));
        Assert.Contains(TextCheck.Problems(long_, invite: false), problem => problem.EndsWith("words", StringComparison.Ordinal));
        Assert.Contains(TextCheck.Problems(Good + new string('x', 300), invite: true), problem => problem.EndsWith("characters", StringComparison.Ordinal));
    }

    [Fact]
    public void A_message_may_run_past_the_invite_cap()
    {
        Assert.DoesNotContain(TextCheck.Problems(Good + new string('x', 300), invite: false), problem => problem.EndsWith("characters", StringComparison.Ordinal));
    }

    [Fact]
    public void A_text_needs_its_one_link_and_its_one_no_oriented_question()
    {
        Assert.NotEmpty(TextCheck.Problems("Pat,\n\nWould it be a bad idea to connect?", invite: true));
        Assert.NotEmpty(TextCheck.Problems("Pat, my work: theyard.stevenstout.biz/?r=abc123 and thanks.", invite: true));
        Assert.NotEmpty(TextCheck.Problems("Pat, theyard.stevenstout.biz/?r=abc123 what do you think about the role?", invite: true));
    }

    [Fact]
    public void The_real_long_follow_up_draft_fails_and_is_never_fixed()
    {
        string body = ReplyQueue.Body(Fixture.Read("li-followup.txt"));
        Assert.StartsWith("Pat,", body, StringComparison.Ordinal);
        Assert.NotEmpty(TextCheck.Problems(body, invite: false));
    }

    [Fact]
    public void A_code_is_read_from_its_link_and_a_placeholder_has_none()
    {
        Assert.Equal("abc123", TextCheck.Code(Good));
        Assert.Equal(string.Empty, TextCheck.Code("theyard.stevenstout.biz/?r=[code]"));
    }

    [Fact]
    public void Only_what_nothing_closed_stays_open()
    {
        Assert.Equal([9, 10], Open().Select(block => block.Number));
    }

    [Fact]
    public void A_sent_held_dropped_replaced_or_pending_block_stays_out()
    {
        var numbers = Open().Select(block => block.Number).ToList();
        Assert.DoesNotContain(1, numbers);
        Assert.DoesNotContain(2, numbers);
        Assert.DoesNotContain(5, numbers);
        Assert.DoesNotContain(6, numbers);
        Assert.DoesNotContain(7, numbers);
        Assert.DoesNotContain(8, numbers);
        Assert.DoesNotContain(11, numbers);
        Assert.DoesNotContain(13, numbers);
    }

    [Fact]
    public void With_no_status_file_the_queue_s_own_lines_still_close_its_blocks()
    {
        var numbers = QueueBlocks.Open("q.md", Fixture.Read("queue.md"), []).Select(block => block.Number).ToList();
        Assert.Equal([5, 6, 8, 9, 10, 11, 13], numbers);
    }

    [Fact]
    public void A_line_about_someone_not_in_the_queue_closes_no_block()
    {
        Assert.DoesNotContain("14", string.Join(",", QueueBlocks.Open("q.md", Fixture.Read("queue.md"), []).Select(b => b.Number)), StringComparison.Ordinal);
        Assert.Contains(Open(), block => block.Number == 10);
    }

    [Fact]
    public void A_status_file_s_ready_text_replaces_the_placeholder_block()
    {
        QueueBlock block = Open().Single(b => b.Number == 9);
        Assert.Equal("Sam Roe", block.Person);
        Assert.Equal("AHEAD", block.Company);
        Assert.Equal("323a34", TextCheck.Code(block.Text));
        Assert.StartsWith("Sam,", block.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_placeholder_block_keeps_its_company_and_no_person()
    {
        QueueBlock block = Open().Single(b => b.Number == 10);
        Assert.Equal(string.Empty, block.Person);
        Assert.Equal("ServiceTitan", block.Company);
        Assert.Equal(WorkAction.Invite, block.Action);
        Assert.Contains("[First name]", block.Text, StringComparison.Ordinal);
        Assert.EndsWith(":109", block.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Headings_with_a_title_and_a_message_read_right()
    {
        QueueBlock message = QueueBlocks.Open("q.md", "### 2. Sam Roe, Sr. Director of Engineering, LINQ: MESSAGE ONLY IF ACCEPTED (invite)\n```\nSam,\n```\n", []).Single();
        Assert.Equal("Sam Roe", message.Person);
        Assert.Equal("LINQ", message.Company);
        Assert.Equal(WorkAction.Message, message.Action);
    }

    [Fact]
    public void A_heading_with_no_fence_is_no_block()
    {
        Assert.Empty(QueueBlocks.Open("q.md", "### 2. Sam Roe, LINQ: CONNECTION REQUEST\nno text\n### 3. Next\n", []));
    }

    [Theory]
    [InlineData("NOT SENT. Already shows pending on the search.", false)]
    [InlineData("DRAFTED, NOT SENT: VP of Engineering, 2nd degree, not pending.", true)]
    public void Pending_in_any_case_closes_a_block_and_not_pending_does_not(string state, bool open)
    {
        string queue = "### 4. Acme: CONNECTION REQUEST\n```\nHi\n```\n";
        string status = $"| 4 | Acme | {state} |\n";
        Assert.Equal(open, QueueBlocks.Open("q.md", queue, [status]).Count == 1);
    }

    [Fact]
    public void A_status_file_names_its_queue()
    {
        Assert.Equal("QUEUE-linuxreach-2026-10-27-0858.md", QueueBlocks.StatusOf(Fixture.Read("queue-status.md")));
        Assert.Null(QueueBlocks.StatusOf(Fixture.Read("queue.md")));
    }

    [Fact]
    public void The_agency_list_comes_from_the_skipped_section()
    {
        Assert.Equal(["Timberline", "Accelerated Search", "Talently", "Conexess"], QueueBlocks.Agencies(Fixture.Read("queue.md")));
    }

    [Fact]
    public void The_reply_queue_gives_accepts_and_windows_and_never_inbound()
    {
        var items = ReplyQueue.Items(Fixture.Read("linkedin-queue.md"));
        Assert.Contains(items, item => item.Person == "Sam Roe");
        Assert.DoesNotContain(items, item => item.Person == "Pat Lee");
        Assert.Contains(items, item => item is { Company: "AccuLynx", Source: "linkedin-queue.md C, Count 3093" });
        Assert.Contains(items, item => item.Company == "People In AI");
        Assert.Contains(items, item => item.Company == "World Wide Technology");
    }

    [Fact]
    public void The_drafts_folder_and_names_are_read()
    {
        Assert.Equal("nightly/2026-10-27/0400-reply-opus-5-5/drafts", ReplyQueue.DraftFolder(Fixture.Read("linkedin-queue.md")));
        Assert.Equal("li-followup-worldwide.txt", ReplyQueue.DraftName("WorldWide Technology"));
    }

    [Fact]
    public void A_draft_s_greeting_gives_the_first_name()
    {
        Assert.Equal("Pat", ReplyQueue.Greeted(ReplyQueue.Body(Fixture.Read("li-followup.txt"))));
    }

    [Fact]
    public void A_sweep_gives_its_confirmed_rows_and_not_its_unconfirmed_ones()
    {
        var postings = ApplyBoards.Confirmed("sweep.md", Fixture.Read("sweep.md"));
        Assert.Equal(["Acme Dental", "Acme Corp"], postings.Select(posting => posting.Company));
        Assert.EndsWith(":7", postings[0].Source, StringComparison.Ordinal);
    }

    [Fact]
    public void A_sweep_row_marked_dropped_never_reaches_the_lane()
    {
        string sweep = "### CONFIRMED LIVE\n| Company | Role | Board | Link |\n|---|---|---|---|\n| Acme | Lead (DROPPED 12:3x) | Greenhouse | https://job-boards.greenhouse.io/a/jobs/1 |\n";
        Assert.Empty(ApplyBoards.Confirmed("s.md", sweep));
    }

    [Fact]
    public void Only_a_no_account_form_is_reachable()
    {
        var postings = ApplyBoards.Confirmed("sweep.md", Fixture.Read("sweep.md"));
        Assert.True(ApplyBoards.Reachable(postings[0]));
        Assert.False(ApplyBoards.Reachable(postings[1]));
        Assert.True(ApplyBoards.Reachable(new Posting("A", "B", "https://www.linkedin.com/jobs/view/1/", string.Empty, "s")));
        Assert.False(ApplyBoards.Reachable(new Posting("A", "B", "https://www.theladders.com/jobs/x", string.Empty, "s")));
        Assert.False(ApplyBoards.Reachable(new Posting("A", "B", "https://jobs.lever.co/a/1", "Workday redirect", "s")));
    }

    [Fact]
    public void Scored_leads_need_the_score_the_name_and_the_age()
    {
        var postings = ApplyBoards.Scored(Fixture.Read("scored.tsv"), Fixture.Read("leads.md"), new DateOnly(2026, 11, 2));
        Assert.Equal(["Steneral Consulting", "Acme Hiring"], postings.Select(posting => posting.Company));
        Assert.Equal("leads/scored.tsv:1", postings[0].Source);
    }

    [Fact]
    public void A_worklist_file_is_one_line_a_row_with_no_stray_tabs()
    {
        string text = WorklistTsv.Render([new WorkRow(1, "Pat\tLee", "Acme", WorkAction.Invite, "texts/01.txt", "abc123", string.Empty, "q.md:3", "Sam Roe")]);
        string[] lines = text.TrimEnd().Split('\n');
        Assert.Equal(WorklistTsv.Header, lines[0]);
        Assert.Equal(9, lines[1].Split('\t').Length);
        Assert.Contains("\tinvite\t", lines[1], StringComparison.Ordinal);
    }
}
