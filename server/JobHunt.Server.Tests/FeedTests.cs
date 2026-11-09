namespace JobHunt.Server.Tests;

/// <summary>The feed lane, the hot ranking and the queue's profile line: the morning's first lane writes what the next two read.</summary>
public sealed class FeedTests
{
    private const string Note = "[First name],\n\nYour platform work caught my eye. I cut release cycle time 50% on one .NET platform.\n\ntheyard.stevenstout.biz/?r=[code]\n\nWould it be a bad idea to connect?\n\nThanks,\nSteve";
    private const string FeedFolder = "nightly/2026-11-03/0600-feed-opus-5-5";
    private static readonly DateOnly Today = new(2026, 11, 3);

    private static IReadOnlyList<string[]> Rows() => [.. TrackerCsvText.Parse(Fixture.Read("tracker.csv")).Skip(1)];

    private static PersonGate Gate() =>
        new(new GateSources(Rows(), Fixture.Read("limits.md"), Fixture.Read("rules.md"), Fixture.Read("responders.md"), []), Today);

    private static FeedCandidate Candidate(string person, string company, string hot = "B", string action = "invite", string text = Note, string url = "https://www.linkedin.com/in/someone") =>
        new(person, company, "VP Engineering", url, hot, "Tier 1, posted 11/1", "Mizzou", "LinkedIn education", "$190K-$230K posted, Greenhouse 11/1", text.Replace("[First name]", person.Split(' ')[0], StringComparison.Ordinal), action);

    private static QueueBlock Block(string company, string hot, int number) => new(number, string.Empty, company, WorkAction.Invite, Note, $"QUEUE-x.md:{number}", hot);

    [Fact]
    public void The_feed_runs_first_reads_LinkedIn_and_closes_nothing()
    {
        Assert.Equal(Lane.Feed, RunSlot.Order[0]);
        Assert.Equal("feed", RunSlot.Word(Lane.Feed));
        Assert.Equal("FEED-LANE.md", RunSlot.Doc(Lane.Feed));
        Assert.False(RunSlot.OnLinkedIn(Lane.Feed));
        Assert.Equal(Lane.Feed, RunSlot.Parse("feed"));
    }

    [Fact]
    public void A_queue_block_carries_its_bucket_and_profile_and_no_line_reads_E()
    {
        string queue = "### 1. Pat Lee, Acme (VP Engineering). Code [code].\n- Seat: VP Engineering\n- Profile: https://www.linkedin.com/in/pat-lee\n- Hot: a\n\n```\nx\n```\n\n### 2. Acme (Director). Code [code].\n\n```\ny\n```\n";
        var blocks = QueueBlocks.Open("QUEUE-hotmgr-2026-11-03.md", queue, []);
        Assert.Equal(("A", "https://www.linkedin.com/in/pat-lee"), (blocks[0].Hot, blocks[0].Url));
        Assert.Equal(("E", string.Empty), (blocks[1].Hot, blocks[1].Url));
    }

    [Fact]
    public void Hot_rank_orders_by_bucket_and_keeps_the_queue_s_order_as_the_tie()
    {
        var ordered = HotRank.Order([Block("Nowhere", "E", 1), Block("Second", "B", 2), Block("First", "A", 3), Block("Third", "B", 4), Block("Fourth", "D", 5)], Rows(), Today);
        Assert.Equal(["First", "Second", "Third", "Fourth", "Nowhere"], ordered.Select(block => block.Company));
    }

    [Fact]
    public void An_application_in_the_last_21_days_lifts_a_cold_block_to_C()
    {
        Assert.Equal('C', HotRank.Bucket(Block("AHEAD", "E", 1), Rows(), Today));
        Assert.Equal('C', HotRank.Bucket(Block("ServiceTitan", "D", 1), Rows(), Today));
        Assert.Equal('A', HotRank.Bucket(Block("AHEAD", "A", 1), Rows(), Today));
        Assert.Equal('E', HotRank.Bucket(Block("Nowhere", "Z", 1), Rows(), Today));
        Assert.Equal('E', HotRank.Bucket(Block("ServiceTitan", "E", 1), Rows(), Today.AddDays(10)));
    }

    [Fact]
    public void The_feed_keeps_what_passes_hottest_first_and_drops_the_rest_with_reasons()
    {
        FeedCandidate[] candidates =
        [
            Candidate("Lou Fine", "Gamma", "C"),
            Candidate("Kim Hot", "Delta", "A"),
            Candidate("Rae Role", "Epsilon", "A", text: Note.Replace("Your platform work", "Your platform role", StringComparison.Ordinal)),
            Candidate("Kim Hot", "Delta Two", "B"),
            Candidate("Max Code", "Zeta", text: Note.Replace("[code]", "abc123", StringComparison.Ordinal)),
            Candidate("Mo Msg", "Eta", action: "message", url: string.Empty),
            Candidate("Held Lead", "Theta"),
            Candidate("Ola Queued", "Iota"),
        ];
        FeedCut cut = FeedQueue.Cut(candidates, Gate(), [new QueueBlock(4, "Ola Queued", "Iota", WorkAction.Invite, Note, "QUEUE-old.md:9")], FeedHold.Read("- Held Lead, Theta\n"));
        Assert.Equal(["Kim Hot", "Lou Fine"], cut.Kept.Select(kept => kept.Person));
        string Why(string who) => Assert.Single(cut.Dropped, dropped => dropped.What.StartsWith(who, StringComparison.Ordinal)).Reason;
        Assert.Contains("application", Why("Rae Role"), StringComparison.Ordinal);
        Assert.Equal("already on this feed", Why("Kim Hot"));
        Assert.Contains("?r=[code]", Why("Max Code"), StringComparison.Ordinal);
        Assert.Contains("profile address", Why("Mo Msg"), StringComparison.Ordinal);
        Assert.Contains("held for Steve", Why("Held Lead"), StringComparison.Ordinal);
        Assert.Contains("QUEUE-old.md:9", Why("Ola Queued"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Your platform roles caught my eye.")]
    [InlineData("Your open positions caught my eye.")]
    [InlineData("Happy to work around your scheduling.")]
    public void A_plural_or_another_form_of_a_held_word_is_held_too(string line)
    {
        FeedCut cut = FeedQueue.Cut([Candidate("Lou Fine", "Gamma", text: Note.Replace("Your platform work caught my eye.", line, StringComparison.Ordinal))], Gate(), [], FeedHolds.None);
        Assert.Empty(cut.Kept);
    }

    [Fact]
    public void A_person_on_any_tracker_row_at_the_company_is_never_queued_again()
    {
        FeedCut cut = FeedQueue.Cut([Candidate("Ann Example", "AccuLynx", action: "message")], Gate(), [], FeedHolds.None);
        Assert.Contains("Rule 25", Assert.Single(cut.Dropped).Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unnamed_row_is_one_a_company()
    {
        FeedCut cut = FeedQueue.Cut([Candidate(string.Empty, "Gamma"), Candidate(string.Empty, "Gamma Inc")], Gate(), [], FeedHolds.None);
        Assert.Single(cut.Kept);
        Assert.Equal("already on this feed", Assert.Single(cut.Dropped).Reason);
    }

    [Fact]
    public void The_rendered_queue_reads_back_whole()
    {
        var kept = new[] { Candidate("Kim Hot", "Delta, Inc.", "A"), Candidate("Mo Msg", "Eta", "B", "message"), Candidate(string.Empty, "Gamma", "C") };
        FeedCut cut = FeedQueue.Cut(kept, Gate(), [], FeedHolds.None);
        string queue = FeedQueue.Render(Today, new DateTime(2026, 11, 3, 6, 4, 0), FeedFolder, cut.Kept);
        var blocks = QueueBlocks.Open("QUEUE-hotmgr-2026-11-03.md", queue, []);
        Assert.Equal(["Kim Hot", "Mo Msg", string.Empty], blocks.Select(block => block.Person));
        Assert.Equal(["Delta Inc.", "Eta", "Gamma"], blocks.Select(block => block.Company));
        Assert.Equal([WorkAction.Invite, WorkAction.Message, WorkAction.Invite], blocks.Select(block => block.Action));
        Assert.Equal(["A", "B", "C"], blocks.Select(block => block.Hot));
        Assert.Equal(cut.Kept.Select(candidate => candidate.Text), blocks.Select(block => block.Text));
        Assert.StartsWith("> RULES FIRST", queue, StringComparison.Ordinal);
        Assert.Contains("# QUEUE-hotmgr-2026-11-03", queue, StringComparison.Ordinal);
    }

    [Fact]
    public void The_hold_file_names_its_people()
    {
        FeedHolds holds = FeedHold.Read("# held\n- Jeff Loveless, Phreesia, decision 3\n- Eric Sugden, Elite Technology\n- Company: SHI International, his skip\n");
        Assert.Equal(["Jeff Loveless", "Eric Sugden"], holds.People);
        Assert.Equal(["SHI International"], holds.Companies);
    }

    [Fact]
    public void A_held_company_holds_every_row_there_and_a_suffix_does_not_slip_a_name_past_the_hold()
    {
        FeedHolds holds = FeedHold.Read("- Nish Modi, EarnIn, decision 3\n- Company: SHI International, his 9/28 skip\n");
        FeedCut cut = FeedQueue.Cut(
            [Candidate(string.Empty, "SHI International"), Candidate("Ann Other", "SHI"), Candidate("Nish Modi (He/Him)", "EarnIn"), Candidate("Lou Fine, PhD", "Gamma")],
            Gate(),
            [],
            holds);
        Assert.Equal(["Lou Fine"], cut.Kept.Select(kept => kept.Person));
        Assert.All(cut.Dropped, dropped => Assert.Contains("held for Steve", dropped.Reason, StringComparison.Ordinal));
    }

    [Fact]
    public void A_profile_address_with_a_query_keeps_its_profile()
    {
        FeedCut cut = FeedQueue.Cut([Candidate("Mo Msg", "Eta", action: "message", url: "https://www.linkedin.com/in/mo-msg/?miniProfileUrn=urn%3Ali%3Afs#x")], Gate(), [], FeedHolds.None);
        Assert.Equal("https://www.linkedin.com/in/mo-msg/", Assert.Single(cut.Kept).ProfileUrl);
    }

    [Fact]
    public async Task A_checkpoint_the_feed_meets_closes_LinkedIn_for_the_day()
    {
        var morning = new Morning(request => request.Arguments[1].Contains("FEED-LANE.md", StringComparison.Ordinal)
            ? new ClaudeRun(0, false, ResultLine.With("""{"rows":[],"live_invite_count":null,"stop_condition":"checkpoint","stop_detail":"Let's do a quick security check","stop_screenshot":null}"""))
            : new ClaudeRun(0, false, ResultLine.Empty));
        morning.Config.Lanes["feed"].Enabled = true;
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, null), default);
        Assert.True(morning.Share.Files.ContainsKey(RunSlot.LinkedInClosed(Today)));
        Assert.Contains("invites: held", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("linkedin-proof/QUEUE-hotmgr-2026-11-10.md", true)]
    [InlineData("linkedin-proof/QUEUE-linuxreach-2026-11-10.md", false)]
    [InlineData("linkedin-proof/QUEUE-hotmgr-2026-11-10.md.bak", false)]
    [InlineData("linkedin-proof/FEED-HOLD.md", false)]
    public void The_feed_may_write_its_one_queue_in_linkedin_proof_and_nothing_else_there(string path, bool allowed)
    {
        Assert.Equal(allowed, WritePolicy.Allowed(path));
    }

    [Fact]
    public void The_feed_gets_no_form_tool_and_scrolls_only_when_live()
    {
        var live = ClaudeArguments.For(new LaneConfig(), Lane.Feed, "/mnt/j", "/mnt/j/run", dry: false);
        var dry = ClaudeArguments.For(new LaneConfig(), Lane.Feed, "/mnt/j", "/mnt/j/run", dry: true);
        Assert.DoesNotContain(live, tool => tool.EndsWith("__form_input", StringComparison.Ordinal) || tool.EndsWith("__file_upload", StringComparison.Ordinal));
        Assert.Contains(live, tool => tool.EndsWith("__computer", StringComparison.Ordinal));
        Assert.DoesNotContain(dry, tool => tool.EndsWith("__computer", StringComparison.Ordinal));
        Assert.Contains("Read(//mnt/j/PRIORITY-BOARD.md)", live);
        Assert.Contains("candidates.json", live[1], StringComparison.Ordinal);
        Assert.Contains("Glob", live);
    }

    [Fact]
    public void The_recount_may_read_the_rules_and_scroll_eighty_turns()
    {
        var recount = ClaudeArguments.Recount(new LaneConfig(), "/mnt/j", "/mnt/j/run", dry: false).ToList();
        Assert.Contains("Read(//mnt/j/systemRules.md)", recount);
        Assert.Equal("80", recount[recount.IndexOf("--max-turns") + 1]);
    }

    [Fact]
    public async Task A_live_feed_writes_the_day_s_queue_and_the_invites_lane_reads_it_the_same_run()
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            if (request.Arguments[1].Contains("FEED-LANE.md", StringComparison.Ordinal))
            {
                morning.Share.Files[$"{FeedFolder}/candidates.json"] = """{"candidates":[{"person":"Kim Hot","company":"Delta","seat":"VP Engineering","profile_url":"https://www.linkedin.com/in/kimhot","hot":"A","why":"Tier 1","common_point":"Mizzou","common_source":"LinkedIn","band":"$200K","text":"Kim,\n\nYour platform work caught my eye.\n\ntheyard.stevenstout.biz/?r=[code]\n\nWould it be a bad idea to connect?\n\nThanks,\nSteve","action":"invite"}]}""";
            }
            return new ClaudeRun(0, false, ResultLine.Empty);
        });
        morning.Config.Lanes["feed"].Enabled = true;
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, null), default);

        string queue = morning.Share.Files["linkedin-proof/QUEUE-hotmgr-2026-11-03.md"];
        Assert.Contains("### 1. Kim Hot, Delta (VP Engineering). Code [code].", queue, StringComparison.Ordinal);
        string worklist = morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/worklist.tsv"];
        Assert.Contains("Kim Hot\tDelta", worklist, StringComparison.Ordinal);
        Assert.Contains("https://www.linkedin.com/in/kimhot", worklist.Split('\n')[1], StringComparison.Ordinal);
        Assert.Contains("feed: 1 queued, 0 left out by the gates", output, StringComparison.Ordinal);
        Assert.Contains("Queue written and read back", morning.Share.Files[$"{FeedFolder}/DONE.md"], StringComparison.Ordinal);
        Assert.Equal(1, morning.Claude.Requests.Count(request => request.Arguments[1].Contains("FEED-LANE.md", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task A_dry_feed_writes_its_queue_into_its_run_folder_only()
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            morning.Share.Files[$"{FeedFolder}-dry/candidates.json"] = """[{"person":"Kim Hot","company":"Delta","seat":"VP Engineering","profile_url":"","hot":"B","why":"Tier 1","common_point":"","common_source":"","band":"","text":"Kim,\n\nYour platform work caught my eye.\n\ntheyard.stevenstout.biz/?r=[code]\n\nWould it be a bad idea to connect?\n\nThanks,\nSteve","action":"invite"}]""";
            return new ClaudeRun(0, false, ResultLine.Empty);
        });
        morning.Config.Lanes["feed"].Enabled = true;
        var before = morning.Share.Files.Keys.Where(path => path.StartsWith("linkedin-proof/", StringComparison.Ordinal)).ToList();
        await morning.Lanes().ExecuteAsync(new RunOptions(true, Lane.Feed), default);

        Assert.Equal(before, morning.Share.Files.Keys.Where(path => path.StartsWith("linkedin-proof/", StringComparison.Ordinal)).ToList());
        Assert.Contains("- Common point: none found", morning.Share.Files[$"{FeedFolder}-dry/QUEUE-hotmgr-2026-11-03.md"], StringComparison.Ordinal);
        Assert.Empty(morning.TrackerRows());
        Assert.Empty(morning.Minter.Asked);
    }

    [Fact]
    public async Task A_message_takes_the_tracker_s_profile_first_and_the_block_s_when_the_tracker_has_none()
    {
        var morning = new Morning(_ => new ClaudeRun(0, false, ResultLine.Empty));
        morning.Share.Files["linkedin-proof/QUEUE-hotmgr-2026-11-03.md"] =
            "### 1. Bo Example, Aledade (VP). MESSAGE\n- Profile: https://www.linkedin.com/in/from-block\n\n```\nBo,\n\nMy work: theyard.stevenstout.biz/?r=[code]\n\nWould it be a bad idea to compare notes?\n\nThanks,\nSteve\n```\n"
            + "\n### 2. Cy New, Kappa (VP): MESSAGE\n- Profile: https://www.linkedin.com/in/cy-new\n\n```\nCy,\n\nMy work: theyard.stevenstout.biz/?r=[code]\n\nWould it be a bad idea to compare notes?\n\nThanks,\nSteve\n```\n";
        DayReads reads = await new DayReader(morning.Share).ReadAsync(Today, default);
        var builder = new WorklistBuilder(morning.Share, morning.Minter, new FakeProse(), morning.Resumes, morning.Config);
        Worklist list = await builder.MessagesAsync(reads, 10, dry: true, default);
        Assert.Equal("https://www.linkedin.com/in/cy-new", Assert.Single(list.Rows, row => row.Person == "Cy New").Url);
        Assert.Equal("https://www.linkedin.com/in/bo-example", Assert.Single(list.Rows, row => row.Person == "Bo Example").Url);
        Assert.Contains(reads.Queues, block => block.Person == "Bo Example" && block.Url == "https://www.linkedin.com/in/from-block");
    }
}
