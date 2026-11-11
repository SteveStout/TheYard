namespace JobHunt.Server.Tests;

/// <summary>The whole morning over the fixtures, with a scripted CLI: what runs, what stops, and what is recorded.</summary>
public sealed class RunTests
{
    private const string Evidence = """{"pending":true,"toast":"Invitation sent to Sam.","read_back":"READBACK","confirmation":null,"screenshot":"/tmp/claude-chrome-screenshots-abc/s.jpg"}""";

    private static ClaudeRun Answer(string structured) => new(0, false, ResultLine.With(structured));

    private static ClaudeRun Quiet(ClaudeRequest request) => new(0, false, ResultLine.Empty);

    private static bool IsRecount(ClaudeRequest request) => request.Arguments[1].Contains("section Recount", StringComparison.Ordinal);

    private static string Row(int row, string outcome, string person, string evidence) =>
        $$"""{"row":{{row}},"outcome":"{{outcome}}","reason":null,"person":"{{person}}","title":"VP","profile_url":"https://www.linkedin.com/in/x","evidence":{{evidence}}}""";

    private static string Result(string rows, string stop = "none") =>
        $$"""{"rows":[{{rows}}],"live_invite_count":17,"stop_condition":"{{stop}}","stop_detail":null,"stop_screenshot":null}""";

    private static bool IsLane(ClaudeRequest request, Lane lane) => request.Arguments[1].Contains(RunSlot.Doc(lane), StringComparison.Ordinal);

    [Fact]
    public async Task A_dry_run_mints_nothing_records_nothing_and_writes_three_notes_and_the_card()
    {
        var morning = new Morning(Quiet);
        (int exit, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(true, null), default);

        Assert.Equal(0, exit);
        Assert.Contains("DRY RUN", output, StringComparison.Ordinal);
        Assert.Empty(morning.Minter.Asked);
        Assert.Empty(morning.TrackerRows());
        Assert.False(morning.Share.Files.ContainsKey(WritePolicy.RunsTable));
        Assert.Contains("nightly/2026-11-03/0600-invites-sonnet-5-5-dry/DONE.md", morning.Share.Files.Keys);
        Assert.Contains("nightly/2026-11-03/0600-messages-sonnet-5-5-dry/DONE.md", morning.Share.Files.Keys);
        Assert.Contains("nightly/2026-11-03/0600-apply-sonnet-5-5-dry/DONE.md", morning.Share.Files.Keys);
        Assert.Contains("nightly/2026-11-03/0600-card.md", morning.Share.Files.Keys);
        Assert.Contains("nightly/2026-11-03/0600-invites-sonnet-5-5-dry/usage.json", morning.Share.Files.Keys);
    }

    [Fact]
    public async Task The_config_switch_makes_a_plain_run_dry()
    {
        var morning = new Morning(Quiet);
        morning.Config.DryRun = true;
        await morning.Lanes().ExecuteAsync(new RunOptions(false, null), default);
        Assert.Contains("nightly/2026-11-03/0600-card.md", morning.Share.Files.Keys);
        Assert.Contains(morning.Share.Files.Keys, path => path.EndsWith("-dry/DONE.md", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_dry_run_holds_its_rows_to_the_dry_limit()
    {
        var morning = new Morning(Quiet);
        morning.Config.DryRows = 1;
        await morning.Lanes().ExecuteAsync(new RunOptions(true, Lane.Invites), default);
        string[] lines = morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5-dry/worklist.tsv"].TrimEnd().Split('\n');
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public async Task A_dry_run_that_claims_a_send_records_nothing_and_says_defect()
    {
        var morning = new Morning(request => IsLane(request, Lane.Invites) ? Answer(Result(Row(1, "sent", "Sam Roe", Evidence))) : Quiet(request));
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(true, null), default);
        Assert.Empty(morning.TrackerRows());
        Assert.Contains("DEFECT", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_verified_send_gets_one_row_a_screenshot_and_a_mint_for_its_placeholder()
    {
        var morning = new Morning(request =>
        {
            if (!IsLane(request, Lane.Invites))
            {
                return Quiet(request);
            }
            return Answer(Result(Row(1, "sent", "Sam Roe", Evidence.Replace("READBACK", "Sam,\\n\\nI applied for the Remote Software Engineering Manager, Foundry role at AHEAD. My work: theyard.stevenstout.biz/?r=323a34\\n\\nWould it be a bad idea to connect?\\n\\nThanks,\\nSteve", StringComparison.Ordinal))));
        });
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);

        string row = Assert.Single(morning.TrackerRows());
        Assert.Contains("-server6am-sam-roe.csv", row, StringComparison.Ordinal);
        Assert.Contains("Verified.", morning.Share.Files[row], StringComparison.Ordinal);
        Assert.Contains("nightly/2026-11-03/0600-invites-sonnet-5-5/shots/row-01.jpg", morning.Share.Files.Keys);
        Assert.Equal(["Leader at ServiceTitan, name found at send|ServiceTitan"], morning.Minter.Asked);
        Assert.Equal([RegistryRows.Lane], morning.Minter.Lanes);
        Assert.Contains("1 sent verified of 2", output, StringComparison.Ordinal);
        Assert.Contains("clean", morning.Share.Files[WritePolicy.RunsTable], StringComparison.Ordinal);
        Assert.Contains("theyard.stevenstout.biz/?r=abc123", morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/texts/02.txt"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_screenshot_the_program_could_not_copy_leaves_the_send_unverified()
    {
        string made = Evidence.Replace("READBACK", "Sam,\\n\\nI applied for the Remote Software Engineering Manager, Foundry role at AHEAD. My work: theyard.stevenstout.biz/?r=323a34\\n\\nWould it be a bad idea to connect?\\n\\nThanks,\\nSteve", StringComparison.Ordinal)
            .Replace("/tmp/claude-chrome-screenshots-abc/s.jpg", "/home/somewhere/s.jpg", StringComparison.Ordinal);
        var morning = new Morning(request => IsLane(request, Lane.Invites) ? Answer(Result(Row(1, "sent", "Sam Roe", made))) : Quiet(request));
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        string row = morning.Share.Files[Assert.Single(morning.TrackerRows())];
        Assert.Contains("UNVERIFIED", row, StringComparison.Ordinal);
        Assert.Contains("screenshot not copied into the run folder", row, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_thin_claim_is_still_recorded_and_marked()
    {
        var morning = new Morning(request => IsLane(request, Lane.Invites) ? Answer(Result(Row(1, "sent", "Sam Roe", "null"))) : Quiet(request));
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Contains("UNVERIFIED", morning.Share.Files[Assert.Single(morning.TrackerRows())], StringComparison.Ordinal);
        Assert.Contains("defect", morning.Share.Files[WritePolicy.RunsTable], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_killed_run_falls_back_on_its_progress_file()
    {
        const string Progress = "nightly/2026-11-03/0600-invites-sonnet-5-5/progress.tsv";
        Morning morning = null!;
        morning = new Morning(request =>
        {
            if (IsRecount(request))
            {
                return Quiet(request);
            }
            morning.Share.Files[Progress] = "1\tsent\tSam Roe\n99\tsent\tJo Ray\n";
            return new ClaudeRun(137, true, null);
        });
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Contains("UNVERIFIED", morning.Share.Files[Assert.Single(morning.TrackerRows())], StringComparison.Ordinal);
        string done = morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/DONE.md"];
        Assert.Contains("killed at the wall-clock limit", done, StringComparison.Ordinal);
        Assert.Contains("progress.tsv", done, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_killed_run_with_no_progress_records_nothing_and_says_so()
    {
        var morning = new Morning(request => IsRecount(request) ? Quiet(request) : new ClaudeRun(1, false, null));
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Empty(morning.TrackerRows());
        Assert.Contains("no result from the model", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_row_the_worklist_never_had_is_named_and_not_recorded()
    {
        var morning = new Morning(request => IsLane(request, Lane.Invites) ? Answer(Result(Row(44, "sent", "Sam Roe", Evidence))) : Quiet(request));
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Empty(morning.TrackerRows());
        Assert.Contains("row 44, which is not on the worklist", morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/DONE.md"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_LinkedIn_stop_ends_both_LinkedIn_lanes_and_the_apply_lane_still_runs()
    {
        var morning = new Morning(request => IsLane(request, Lane.Invites) ? Answer(Result(string.Empty, "captcha")) : Quiet(request));
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, null), default);

        Assert.Contains(morning.Claude.Requests, request => IsLane(request, Lane.Apply));
        Assert.DoesNotContain(morning.Claude.Requests, request => IsLane(request, Lane.Messages));
        Assert.Contains("messages: held, stop condition captcha", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stop_mid_lane_ends_the_messages_lane_and_the_apply_lane_s_LinkedIn_rows()
    {
        var morning = new Morning(request => IsLane(request, Lane.Invites) && !IsRecount(request) ? Answer(Result(string.Empty, "checkpoint")) : Quiet(request));
        await morning.Lanes().ExecuteAsync(new RunOptions(false, null), default);

        Assert.DoesNotContain(morning.Claude.Requests, request => IsLane(request, Lane.Messages));
        string list = morning.Share.Files["nightly/2026-11-03/0600-apply-sonnet-5-5/worklist.tsv"];
        Assert.Contains("Acme Dental", list, StringComparison.Ordinal);
        Assert.DoesNotContain("linkedin.com", list, StringComparison.Ordinal);
        Assert.Contains("LinkedIn is closed today", morning.Share.Files["nightly/2026-11-03/0600-apply-sonnet-5-5/DONE.md"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_lane_that_ends_with_no_answer_closes_LinkedIn_too()
    {
        var morning = new Morning(request => IsLane(request, Lane.Invites) && !IsRecount(request) ? new ClaudeRun(1, false, null) : Quiet(request));
        await morning.Lanes().ExecuteAsync(new RunOptions(false, null), default);
        Assert.DoesNotContain(morning.Claude.Requests, request => IsLane(request, Lane.Messages));
    }

    [Theory]
    [InlineData(95, "invite pause, 95")]
    [InlineData(-1, "no recount, no invite")]
    public async Task The_pause_is_the_program_s_arithmetic_and_a_missing_count_sends_nothing(int live, string reason)
    {
        string count = live < 0 ? "null" : live.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var morning = new Morning(_ => Answer($$"""{"rows":[],"live_invite_count":{{count}},"stop_condition":"none","stop_detail":null,"stop_screenshot":null}"""));
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Contains(reason, output, StringComparison.Ordinal);
        Assert.Single(morning.Claude.Requests);
        Assert.Empty(morning.Minter.Asked);
    }

    [Fact]
    public async Task The_worklist_is_cut_to_one_under_the_pause_from_the_live_count()
    {
        var morning = new Morning(_ => Answer("""{"rows":[],"live_invite_count":88,"stop_condition":"none","stop_detail":null,"stop_screenshot":null}"""));
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Equal(2, morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/worklist.tsv"].TrimEnd().Split('\n').Length);
    }

    [Fact]
    public async Task A_block_that_went_out_does_not_reopen_tomorrow()
    {
        string tracker = Fixture.Read("tracker.csv") + "3400,Sam Roe,ServiceTitan,LinkedIn,8/1/2026,,CONNECTION REQUEST SENT by Claude. Source QUEUE-linuxreach-2026-10-27-0858.md:109.\n";
        var morning = new Morning(Quiet, tracker);
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Contains("this block already went out", morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/DONE.md"], StringComparison.Ordinal);
        Assert.DoesNotContain("ServiceTitan", morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/worklist.tsv"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_row_with_no_name_carries_who_the_tracker_already_has_there()
    {
        string tracker = Fixture.Read("tracker.csv") + "1247,Bo Kay,ServiceTitan,LinkedIn,8/24/2026,,SECOND FOLLOW-UP MESSAGE SENT BY CLAUDE\n";
        var morning = new Morning(Quiet, tracker);
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Contains("\tBo Kay\t", morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/worklist.tsv"].Split('\n').Single(line => line.Contains("ServiceTitan", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_send_the_result_left_out_is_recorded_from_the_progress_file()
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            if (IsRecount(request))
            {
                return Quiet(request);
            }
            morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/progress.tsv"] = "1\tsent\tSam Roe\n";
            return Answer(Result(string.Empty));
        });
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Contains("UNVERIFIED", morning.Share.Files[Assert.Single(morning.TrackerRows())], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_crash_that_left_progress_holds_the_lane_for_a_person()
    {
        var morning = new Morning(Quiet);
        morning.Share.Files["nightly/2026-11-03/0600-apply-sonnet-5-5/progress.tsv"] = "1\tsent\tx\n";
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Apply), default);
        Assert.Contains("an earlier run left", output, StringComparison.Ordinal);
        Assert.Empty(morning.Claude.Requests);
    }

    [Fact]
    public async Task A_crash_yesterday_holds_the_lane_this_morning()
    {
        var morning = new Morning(Quiet);
        morning.Share.Files["nightly/2026-11-02/0600-apply-sonnet-5-5/progress.tsv"] = "1\tsent\tx\n";
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Apply), default);
        Assert.Contains("sends with no records", output, StringComparison.Ordinal);
        Assert.Empty(morning.Claude.Requests);
    }

    [Fact]
    public async Task A_crash_stays_held_through_any_number_of_later_runs()
    {
        var morning = new Morning(Quiet);
        const string Folder = "nightly/2026-11-03/0600-apply-sonnet-5-5";
        morning.Share.Files[$"{Folder}/progress.tsv"] = "1\tsent\tx\n";
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Apply), default);
        morning.Time.Now = Morning.At.AddMinutes(30);
        (_, string second) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Apply), default);
        Assert.Contains("sends with no records", second, StringComparison.Ordinal);
        Assert.False(morning.Share.Files.ContainsKey($"{Folder}/DONE.md"));
        Assert.Contains($"{Folder}/HELD.md", morning.Share.Files.Keys);
        Assert.Empty(morning.Claude.Requests);
    }

    [Fact]
    public async Task A_stop_stays_a_stop_through_a_second_run_the_same_day()
    {
        var morning = new Morning(request => IsLane(request, Lane.Invites) && !IsRecount(request) ? Answer(Result(string.Empty, "checkpoint")) : Quiet(request));
        await morning.Lanes().ExecuteAsync(new RunOptions(false, null), default);
        Assert.Contains("nightly/2026-11-03/0600-linkedin-closed.md", morning.Share.Files.Keys);
        morning.Claude.Requests.Clear();
        morning.Time.Now = Morning.At.AddMinutes(30);
        (_, string second) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Messages), default);
        Assert.Contains("did not finish clean", second, StringComparison.Ordinal);
        Assert.Empty(morning.Claude.Requests);
    }

    [Fact]
    public async Task A_stop_on_the_recount_stops_a_second_run_recounting()
    {
        var morning = new Morning(_ => Answer(Result(string.Empty, "captcha")));
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        int first = morning.Claude.Requests.Count;
        morning.Time.Now = Morning.At.AddMinutes(30);
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Equal(first, morning.Claude.Requests.Count);
    }

    [Fact]
    public async Task A_result_line_with_no_structured_answer_closes_LinkedIn()
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            if (IsRecount(request) || !IsLane(request, Lane.Invites))
            {
                return Quiet(request);
            }
            morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/progress.tsv"] = "1\tsent\tSam Roe\n2\tsent\tBo Kay\n";
            return new ClaudeRun(0, false, """{"type":"result","subtype":"error_max_turns"}""");
        });
        await morning.Lanes().ExecuteAsync(new RunOptions(false, null), default);
        Assert.DoesNotContain(morning.Claude.Requests, request => IsLane(request, Lane.Messages));
    }

    [Fact]
    public async Task A_share_that_refuses_the_runs_table_still_leaves_a_close_row()
    {
        var bad = new Morning(request => IsLane(request, Lane.Invites) && !IsRecount(request) ? Answer(Result(Row(1, "sent", "Sam Roe", "null"))) : Quiet(request));
        bad.Share.Refused = WritePolicy.RunsTable;
        (_, string output) = await bad.Lanes().ExecuteAsync(new RunOptions(false, null), default);
        Assert.Contains("the card could not be built", output, StringComparison.Ordinal);
        Assert.Contains(bad.Share.Files.Keys, path => path.EndsWith("-server6am-close.md", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_full_rerun_after_a_held_lane_leaves_the_morning_s_card_alone()
    {
        var morning = new Morning(Quiet);
        morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/DONE.md"] = "done";
        morning.Share.Files["nightly/2026-11-03/0600-card.md"] = "the morning";
        await morning.Lanes().ExecuteAsync(new RunOptions(false, null), default);
        Assert.Equal("the morning", morning.Share.Files["nightly/2026-11-03/0600-card.md"]);
    }

    [Fact]
    public async Task A_draft_greeting_someone_else_never_reaches_the_person_the_queue_named()
    {
        string tracker = "\"Count\",Person,Company,Communication,Date,Phone/Email,Notes\n1,Sam Roe,AccuLynx,LinkedIn,10/27/2026,,\"CONNECTION REQUEST SENT by Claude\"\n";
        var morning = new Morning(Quiet, tracker);
        morning.Share.Files["nightly/2026-10-27/0400-reply-opus-5-5/drafts/li-followup-acculynx.txt"] = "Ann,\n\nThanks. My work: theyard.stevenstout.biz/?r=[code]\n\nWould it be a bad idea to look?\n\nThanks,\nSteve\n";
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Messages), default);
        Assert.Contains("Sam Roe: the draft greets Ann", morning.Share.Files["nightly/2026-11-03/0600-messages-sonnet-5-5/DONE.md"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_crash_beside_an_earlier_hold_is_still_a_crash()
    {
        var morning = new Morning(Quiet);
        const string Folder = "nightly/2026-11-03/0600-apply-sonnet-5-5";
        morning.Share.Files[$"{Folder}/DONE.md"] = "# DONE\n\nHELD: reach1103 LIVE\n";
        morning.Share.Files[$"{Folder}/progress.tsv"] = "1\tsent\tx\n";
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Apply), default);
        Assert.Contains("sends with no records", output, StringComparison.Ordinal);
        Assert.Empty(morning.Claude.Requests);
    }

    [Fact]
    public async Task A_stop_the_apply_lane_meets_on_LinkedIn_closes_it_for_the_day()
    {
        var morning = new Morning(request => IsLane(request, Lane.Apply) ? Answer(Result(string.Empty, "checkpoint")) : Quiet(request));
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Apply), default);
        Assert.Contains("nightly/2026-11-03/0600-linkedin-closed.md", morning.Share.Files.Keys);
    }

    [Fact]
    public async Task A_dry_run_never_closes_the_day_s_live_work()
    {
        var morning = new Morning(_ => Answer(Result(string.Empty, "captcha")));
        await morning.Lanes().ExecuteAsync(new RunOptions(true, null), default);
        Assert.DoesNotContain("nightly/2026-11-03/0600-linkedin-closed.md", morning.Share.Files.Keys);
    }

    [Fact]
    public async Task A_dry_crash_holds_nothing()
    {
        var morning = new Morning(Quiet);
        morning.Share.Files["nightly/2026-11-02/0600-apply-sonnet-5-5-dry/progress.tsv"] = "1\tsent\tx\n";
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Apply), default);
        Assert.NotEmpty(morning.Claude.Requests);
    }

    [Fact]
    public async Task A_timeout_with_every_row_in_progress_still_closes_LinkedIn()
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            if (IsRecount(request) || !IsLane(request, Lane.Invites))
            {
                return Quiet(request);
            }
            morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/progress.tsv"] = "1\tsent\tSam Roe\n2\tsent\tBo Kay\n";
            return new ClaudeRun(137, true, null);
        });
        await morning.Lanes().ExecuteAsync(new RunOptions(false, null), default);
        Assert.DoesNotContain(morning.Claude.Requests, request => IsLane(request, Lane.Messages));
    }

    [Fact]
    public async Task A_cli_that_will_not_start_still_ends_in_a_card_and_a_close_row()
    {
        var morning = new Morning(_ => throw new System.ComponentModel.Win32Exception(2));
        (int exit, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, null), default);
        Assert.Equal(1, exit);
        Assert.Contains("DEFECT", output, StringComparison.Ordinal);
        Assert.Contains(morning.Share.Files.Keys, path => path.EndsWith("-server6am-close.md", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_broken_result_line_does_not_cost_the_records_from_progress()
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            if (IsRecount(request))
            {
                return Quiet(request);
            }
            morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/progress.tsv"] = "1\tsent\tSam Roe\n";
            return new ClaudeRun(0, false, "{not json \"type\":\"result\"");
        });
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Single(morning.TrackerRows());
    }

    [Fact]
    public async Task One_lane_run_later_leaves_the_morning_s_card_alone()
    {
        var morning = new Morning(Quiet);
        morning.Share.Files["nightly/2026-11-03/0600-card.md"] = "the morning";
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Apply), default);
        Assert.Equal("the morning", morning.Share.Files["nightly/2026-11-03/0600-card.md"]);
    }

    [Fact]
    public async Task A_lane_held_this_morning_may_run_again_once_it_is_clear()
    {
        var morning = new Morning(Quiet);
        morning.Share.Files["nightly/2026-11-03/0600-apply-sonnet-5-5/DONE.md"] = "# DONE\n\nHELD: a reason\n";
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Apply), default);
        Assert.NotEmpty(morning.Claude.Requests);
    }

    [Fact]
    public async Task An_accept_finds_its_company_on_the_tracker_and_reaches_a_short_draft()
    {
        string tracker = "\"Count\",Person,Company,Communication,Date,Phone/Email,Notes\n1,Sam Roe,AccuLynx,LinkedIn,10/27/2026,,\"CONNECTION REQUEST SENT by Claude. Profile linkedin.com/in/samroe\"\n";
        var morning = new Morning(Quiet, tracker);
        morning.Share.Files["nightly/2026-10-27/0400-reply-opus-5-5/drafts/li-followup-acculynx.txt"] = "Sam,\n\nThanks for connecting. My work: theyard.stevenstout.biz/?r=[code]\n\nWould it be a bad idea to take a look?\n\nThanks,\nSteve\n";
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Messages), default);
        Assert.Contains("Sam Roe\tAccuLynx\tmessage", morning.Share.Files["nightly/2026-11-03/0600-messages-sonnet-5-5/worklist.tsv"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stop_from_the_service_still_writes_the_records_and_the_close()
    {
        using var stop = new CancellationTokenSource();
        var morning = new Morning(request =>
        {
            if (IsRecount(request))
            {
                return Quiet(request);
            }
            stop.Cancel();
            return Answer(Result(Row(1, "sent", "Sam Roe", "null")));
        });
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, null), stop.Token);
        Assert.Single(morning.TrackerRows());
        Assert.Contains("the service was stopped", output, StringComparison.Ordinal);
        Assert.Contains(morning.Share.Files.Keys, path => path.EndsWith("-server6am-close.md", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_share_that_breaks_mid_run_still_closes_with_a_card()
    {
        var morning = new Morning(Quiet);
        morning.Share.Files.Remove("leads/scored.tsv");
        (int exit, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(true, null), default);
        Assert.Equal(1, exit);
        Assert.Contains("DEFECT: the run stopped early", output, StringComparison.Ordinal);
        Assert.Contains(morning.Share.Files.Keys, path => path.EndsWith("-server6am-close.md", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_second_run_the_same_day_leaves_the_first_run_s_note_and_card_alone()
    {
        var morning = new Morning(Quiet);
        morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/DONE.md"] = "the real note";
        morning.Share.Files["nightly/2026-11-03/0600-card.md"] = "the real card";
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Equal("the real note", morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/DONE.md"]);
        Assert.Equal("the real card", morning.Share.Files["nightly/2026-11-03/0600-card.md"]);
        Assert.Empty(morning.Claude.Requests);
    }

    [Fact]
    public async Task A_lane_row_still_in_the_spool_holds_LinkedIn()
    {
        var morning = new Morning(Quiet);
        morning.Share.Files["inbox/append-inbox/state.md/20261103-115500Z-reach1103-registry.md"] = "## LANE: reach1103 LIVE. Holds: chrome, linkedin. | IN PROGRESS |";
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Contains("a hold on LinkedIn or Chrome by reach1103", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Another_lane_live_on_LinkedIn_skips_both_and_the_apply_lane_still_runs()
    {
        var morning = new Morning(Quiet);
        morning.Time.Now = new DateTimeOffset(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
        morning.Share.Files["state.md"] = "## LANE: reach1103 (Claude in Chrome). LinkedIn invites. | IN PROGRESS |\n<!-- AID:20261103-113000Z-reach1103-registry -->\n";
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, null), default);

        Assert.Contains("invites: held, a hold on LinkedIn or Chrome by reach1103", output, StringComparison.Ordinal);
        Assert.Contains("messages: held, a hold on LinkedIn or Chrome by reach1103", output, StringComparison.Ordinal);
        Assert.Contains(morning.Claude.Requests, request => IsLane(request, Lane.Apply));
        Assert.DoesNotContain(morning.Claude.Requests, request => IsLane(request, Lane.Invites));
    }

    [Fact]
    public async Task A_stale_share_stops_the_run_before_anything_is_written()
    {
        var morning = new Morning(Quiet);
        (int exit, string output) = await morning.Lanes(new FakeMount("the share would not read")).ExecuteAsync(new RunOptions(false, null), default);
        Assert.Equal(1, exit);
        Assert.Contains("the share would not read", output, StringComparison.Ordinal);
        Assert.Empty(morning.Claude.Requests);
        Assert.DoesNotContain(morning.Share.Files.Keys, path => path.StartsWith("inbox/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_held_lock_turns_the_run_away()
    {
        var morning = new Morning(Quiet);
        (int exit, string output) = await morning.Lanes(runLock: new FakeLock(free: false)).ExecuteAsync(new RunOptions(false, null), default);
        Assert.Equal(0, exit);
        Assert.Contains("another run holds the lock", output, StringComparison.Ordinal);
        Assert.Empty(morning.Claude.Requests);
    }

    [Fact]
    public async Task A_gate_that_cannot_be_read_holds_every_lane()
    {
        var morning = new Morning(Quiet);
        morning.Share.Files["outreach-limits.md"] = "nothing here";
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, null), default);
        Assert.Contains("a gate could not be read", output, StringComparison.Ordinal);
        Assert.Empty(morning.Claude.Requests);
    }

    [Fact]
    public async Task Chrome_that_will_not_start_holds_every_lane()
    {
        var morning = new Morning(Quiet);
        (_, string output) = await morning.Lanes(desktop: new FakeDesktop("Chrome would not start")).ExecuteAsync(new RunOptions(false, null), default);
        Assert.Contains("invites: held, Chrome would not start", output, StringComparison.Ordinal);
        Assert.Empty(morning.Claude.Requests);
    }

    [Fact]
    public async Task A_lane_that_already_ran_today_does_not_run_again()
    {
        var morning = new Morning(Quiet);
        morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/DONE.md"] = "done";
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.DoesNotContain("invites", output, StringComparison.Ordinal);
        Assert.Empty(morning.Claude.Requests);
    }

    [Fact]
    public async Task A_disabled_lane_is_left_out()
    {
        var morning = new Morning(Quiet);
        morning.Config.Lanes["apply"].Enabled = false;
        await morning.Lanes().ExecuteAsync(new RunOptions(true, null), default);
        Assert.DoesNotContain(morning.Claude.Requests, request => IsLane(request, Lane.Apply));
    }

    [Fact]
    public async Task A_run_writes_a_LIVE_row_and_a_close_row_through_the_inbox()
    {
        var morning = new Morning(Quiet);
        await morning.Lanes().ExecuteAsync(new RunOptions(true, null), default);
        Assert.Contains(morning.Share.Files, file => file.Key.EndsWith("-server6am-registry.md", StringComparison.Ordinal) && file.Value.Contains("IN PROGRESS", StringComparison.Ordinal));
        Assert.Contains(morning.Share.Files, file => file.Key.EndsWith("-server6am-close.md", StringComparison.Ordinal) && file.Value.Contains("CLOSED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task At_the_pause_the_invite_list_is_empty_and_no_model_runs_for_it()
    {
        var header = "\"Count\",Person,Company,Communication,Date,Phone/Email,Notes\n";
        string rows = string.Concat(Enumerable.Range(1, 90).Select(i => $"{i},P{i},C{i},LinkedIn,11/1/2026,,CONNECTION REQUEST SENT by Claude\n"));
        const string Applied = "2900,NA,AHEAD (Software Engineering Manager - Foundry),Company Website,10/26/2026,,SUBMITTED by Steve\n2857,NA,ServiceTitan,Workday,10/19/2026,,\"SUBMITTED: Senior Staff Software Engineer, req JR110561\"\n";
        var morning = new Morning(Quiet, header + rows + Applied);
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Empty(morning.Claude.Requests);
        Assert.Contains("over the day's room", morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/DONE.md"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_mint_leaves_the_row_queued()
    {
        var morning = new Morning(Quiet);
        morning.Minter.Code = null;
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Contains("make_ref.py failed", morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/DONE.md"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Today_s_follow_up_drafts_fail_their_checks_and_no_model_runs_for_them()
    {
        var morning = new Morning(Quiet);
        morning.Share.Files["nightly/2026-10-27/0400-reply-opus-5-5/drafts/li-followup-acculynx.txt"] = Fixture.Read("li-followup.txt").Replace("Pat,", "Ann,", StringComparison.Ordinal);
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Messages), default);
        Assert.Empty(morning.Claude.Requests);
        string done = morning.Share.Files["nightly/2026-11-03/0600-messages-sonnet-5-5/DONE.md"];
        Assert.Contains("text failed:", done, StringComparison.Ordinal);
        Assert.Contains("Sam Roe: no drafted text", done, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_short_draft_reaches_the_messages_worklist_with_its_person_and_profile()
    {
        string tracker = "\"Count\",Person,Company,Communication,Date,Phone/Email,Notes\n1,Ann Example,AccuLynx,LinkedIn,10/27/2026,,\"SCOPED CONNECTION REQUEST SENT by Claude. Profile linkedin.com/in/annexample75\"\n";
        var morning = new Morning(Quiet, tracker);
        morning.Share.Files["nightly/2026-10-27/0400-reply-opus-5-5/drafts/li-followup-acculynx.txt"] = "Ann,\n\nThanks for connecting. My work: theyard.stevenstout.biz/?r=[code]\n\nWould it be a bad idea to take a look?\n\nThanks,\nSteve\n";
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Messages), default);
        string list = morning.Share.Files["nightly/2026-11-03/0600-messages-sonnet-5-5/worklist.tsv"];
        Assert.Contains("Ann Example\tAccuLynx\tmessage", list, StringComparison.Ordinal);
        Assert.Contains("https://www.linkedin.com/in/annexample75", list, StringComparison.Ordinal);
        Assert.Single(morning.Claude.Requests);
    }

    [Fact]
    public async Task The_apply_list_sorts_reachable_from_walled_and_writes_the_blockers_and_the_resume()
    {
        var morning = new Morning(Quiet);
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Apply), default);
        string list = morning.Share.Files["nightly/2026-11-03/0600-apply-sonnet-5-5/worklist.tsv"];
        Assert.Contains("Acme Dental", list, StringComparison.Ordinal);
        Assert.Contains("Acme Hiring", list, StringComparison.Ordinal);
        Assert.DoesNotContain("Steneral", list, StringComparison.Ordinal);
        string blockers = morning.Share.Files["nightly/2026-11-03/0600-apply-sonnet-5-5/APPLY-BLOCKERS-2026-11-03.md"];
        Assert.Contains("Acme Corp", blockers, StringComparison.Ordinal);
        Assert.Contains("StevenStoutResume_2026-10-29.pdf", list, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_application_gets_its_own_build_and_a_tracker_row_that_says_which()
    {
        var morning = new Morning(request => Answer(Result("""{"row":1,"outcome":"sent","reason":null,"person":null,"title":null,"profile_url":null,"evidence":{"pending":null,"toast":null,"read_back":null,"confirmation":"Thanks for applying","screenshot":"/tmp/x.jpg"}}""")));
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Apply), default);
        Assert.NotEmpty(morning.Resumes.Asked);
        Assert.Contains("/mnt/j/nightly/2026-11-03/0600-apply-sonnet-5-5/resumes/01", morning.Resumes.Asked[0], StringComparison.Ordinal);
        string row = morning.Share.Files[morning.TrackerRows().First()];
        Assert.Contains("SHA-256 abc123", row, StringComparison.Ordinal);
        Assert.Contains("headline", row, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_resume_that_will_not_build_keeps_its_row_off_the_list_and_says_why()
    {
        var morning = new Morning(Quiet);
        morning.Resumes.Failure = "STOP. The headline does not sit whole on one line.";
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Apply), default);
        string done = morning.Share.Files["nightly/2026-11-03/0600-apply-sonnet-5-5/DONE.md"];
        Assert.Contains("the resume did not build", done, StringComparison.Ordinal);
        Assert.Single(morning.Share.Files["nightly/2026-11-03/0600-apply-sonnet-5-5/worklist.tsv"].TrimEnd().Split('\n'));
    }

    [Fact]
    public async Task A_block_on_a_closed_req_never_reaches_the_worklist()
    {
        string tracker = Fixture.Read("tracker.csv") + "3401,NA,AHEAD,Lever,10/30/2026,,\"REJECTION, generic, the email names no role.\"\n";
        var morning = new Morning(Quiet, tracker);
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.DoesNotContain("AHEAD", morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/worklist.tsv"], StringComparison.Ordinal);
        Assert.Contains("closed req", morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/DONE.md"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_status_command_finds_the_newest_card()
    {
        var share = new MemoryShare();
        var time = new FixedTime(Morning.At);
        Assert.Contains("No card", await new LastCard(share, time).ExecuteAsync(default), StringComparison.Ordinal);
        share.Files["nightly/2026-11-01/0600-card.md"] = "the card";
        Assert.Contains("the card", await new LastCard(share, time).ExecuteAsync(default), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_send_in_progress_the_result_calls_failed_is_still_recorded()
    {
        const string Progress = "nightly/2026-11-03/0600-invites-sonnet-5-5/progress.tsv";
        Morning morning = null!;
        morning = new Morning(request =>
        {
            if (IsRecount(request))
            {
                return Quiet(request);
            }
            morning.Share.Files[Progress] = "1\tsent\tSam Roe\n";
            return Answer(Result("""{"row":1,"outcome":"failed","reason":"no toast","person":null,"title":null,"profile_url":null,"evidence":null}"""));
        });
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Contains("UNVERIFIED", morning.Share.Files[Assert.Single(morning.TrackerRows())], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1 sent Sam Roe\n")]
    [InlineData("1<TAB>sent<TAB>Sam Roe\n")]
    public async Task A_progress_line_a_small_model_wrote_loosely_still_counts(string line)
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            if (IsRecount(request))
            {
                return Quiet(request);
            }
            morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/progress.tsv"] = line;
            return new ClaudeRun(137, true, null);
        });
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Single(morning.TrackerRows());
    }

    [Fact]
    public async Task A_row_reported_twice_is_one_tracker_row()
    {
        string once = Row(1, "sent", "Sam Roe", Evidence);
        var morning = new Morning(request => IsRecount(request) ? Quiet(request) : Answer(Result(once + "," + once)));
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Single(morning.TrackerRows());
    }

    [Fact]
    public async Task A_send_the_worklist_never_held_reaches_the_card_and_closes_LinkedIn()
    {
        var morning = new Morning(request => IsRecount(request) ? Quiet(request) : Answer(Result(Row(77, "sent", "Jo Stranger", Evidence))));
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Contains("DEFECT", output, StringComparison.Ordinal);
        Assert.Contains("Jo Stranger", output, StringComparison.Ordinal);
        Assert.Contains("nightly/2026-11-03/0600-linkedin-closed.md", morning.Share.Files.Keys);
    }

    [Fact]
    public async Task A_run_cut_short_says_so_on_the_card()
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            if (IsRecount(request))
            {
                return Quiet(request);
            }
            morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/progress.tsv"] = "1\tsent\tSam Roe\n";
            return new ClaudeRun(137, true, null);
        });
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Contains("cut short", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_registry_row_that_will_not_write_stops_the_run_with_a_line()
    {
        var morning = new Morning(Quiet);
        morning.Share.Files[RegistryRows.Path(Morning.At, "registry")] = "already there";
        (int exit, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, null), default);
        Assert.Equal(1, exit);
        Assert.Contains("registry", output, StringComparison.Ordinal);
        Assert.Empty(morning.Claude.Requests);
    }

    [Fact]
    public async Task A_progress_line_for_a_row_the_worklist_never_held_is_a_defect_and_closes_LinkedIn()
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            if (IsRecount(request))
            {
                return Quiet(request);
            }
            morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/progress.tsv"] = "7\tsent\tJane Doe\n";
            return Answer(Result(Row(1, "skipped", "Sam Roe", "null") + "," + Row(2, "skipped", "Bo Kay", "null")));
        });
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Contains("Jane Doe", output, StringComparison.Ordinal);
        Assert.Contains("nightly/2026-11-03/0600-linkedin-closed.md", morning.Share.Files.Keys);
    }

    [Fact]
    public async Task A_progress_line_nobody_can_read_is_said_out_loud()
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            if (IsRecount(request))
            {
                return Quiet(request);
            }
            morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/progress.tsv"] = "row one was sent to Jane\n";
            return new ClaudeRun(137, true, null);
        });
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Contains("progress.tsv line not read", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1 | sent | Sam Roe\n")]
    [InlineData("1, sent, Sam Roe\n")]
    public async Task A_progress_line_with_other_separators_still_counts(string line)
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            if (IsRecount(request))
            {
                return Quiet(request);
            }
            morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/progress.tsv"] = line;
            return new ClaudeRun(137, true, null);
        });
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Contains("Sam Roe,", morning.Share.Files[Assert.Single(morning.TrackerRows())], StringComparison.Ordinal);
    }

    [Fact]
    public void Every_urgent_line_reaches_the_card_however_many_there_are()
    {
        var at = Morning.At;
        LaneReport Report(Lane lane) => new(lane, "m", false, at, at, null, [], [], null, [], [], null);
        string[] urgent = ["TRACKER ROW NOT WRITTEN for row 1", "TRACKER ROW NOT WRITTEN for row 2", "TRACKER ROW NOT WRITTEN for row 3", "TRACKER ROW NOT WRITTEN for row 4"];
        string card = RunCard.Render(new DateOnly(2026, 11, 3), false, [Report(Lane.Invites), Report(Lane.Messages), Report(Lane.Apply)], urgent);
        Assert.Contains("row 4", card, StringComparison.Ordinal);
        Assert.True(card.TrimEnd().Split('\n').Length <= RunCard.MostLines);
    }

    [Fact]
    public async Task Two_names_for_one_company_are_one_application_a_morning()
    {
        var morning = new Morning(Quiet);
        morning.Share.Files["APPLY-SWEEP-2026-10-29-two.md"] = "## CONFIRMED LIVE\n| Company | Role | Link |\n|---|---|---|\n| Northwind | Staff Engineer | https://jobs.lever.co/northwind/a |\n| Northwind Network | Principal Engineer | https://jobs.lever.co/northwind/b |\n";
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Apply), default);
        Assert.Contains("one application a company a morning", morning.Share.Files["nightly/2026-11-03/0600-apply-sonnet-5-5/DONE.md"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tracker_write_that_fails_once_is_tried_again()
    {
        var morning = new Morning(request => IsRecount(request) ? Quiet(request) : Answer(Result(Row(1, "sent", "Sam Roe", Evidence))));
        morning.Share.RefuseSpool = 1;
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Single(morning.TrackerRows());
    }

    [Fact]
    public async Task A_row_that_never_wrote_holds_the_lane_the_next_morning()
    {
        var morning = new Morning(request => IsRecount(request) ? Quiet(request) : Answer(Result(Row(1, "sent", "Sam Roe", Evidence))));
        morning.Share.RefuseSpool = 99;
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        morning.Share.RefuseSpool = 0;
        morning.Time.Now = Morning.At.AddDays(1);
        morning.Claude.Requests.Clear();
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Contains("tracker row", output, StringComparison.Ordinal);
        Assert.Empty(morning.Claude.Requests);
    }

    [Fact]
    public async Task A_defect_in_an_earlier_note_holds_the_lane_however_old_it_is()
    {
        var morning = new Morning(Quiet);
        morning.Share.Files["nightly/2026-10-20/0600-invites-sonnet-5-5/DONE.md"] = "# DONE\n- DEFECT: progress.tsv line not read, read the page: Row 3 sent Jane\n";
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Contains("nightly/2026-10-20/0600-invites-sonnet-5-5", output, StringComparison.Ordinal);
        Assert.Empty(morning.Claude.Requests);
    }

    [Fact]
    public async Task A_row_that_half_wrote_is_not_written_twice_and_holds_the_lane()
    {
        var morning = new Morning(request => IsRecount(request) ? Quiet(request) : Answer(Result(Row(1, "sent", "Sam Roe", Evidence))));
        morning.Share.PartialSpool = true;
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Single(morning.TrackerRows());
        Assert.Contains("TRACKER ROW NOT WRITTEN", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_tracker_row_carries_the_time_of_the_send()
    {
        var morning = new Morning(request => IsRecount(request) ? Quiet(request) : Answer(Result(Row(1, "sent", "Sam Roe", Evidence))));
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Contains("06:00 Central", morning.Share.Files[Assert.Single(morning.TrackerRows())], StringComparison.Ordinal);
    }

    [Fact]
    public async Task One_person_written_two_ways_is_one_message_a_morning()
    {
        const string Text = "Lou,\n\nThanks for connecting. My work: theyard.stevenstout.biz/?r=[code]\n\nWould it be a bad idea to take a look?\n\nThanks,\nSteve";
        var morning = new Morning(Quiet);
        morning.Share.Files["linkedin-proof/QUEUE-qa-2026-11-04.md"] = $"### 1. Lou Grant, Northwind Widgets: MESSAGE\n\n```\n{Text}\n```\n\n### 2. Dr. Lou Grant, Northwind Widgets: MESSAGE\n\n```\n{Text}\n```\n";
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Messages), default);
        Assert.Contains("already on this worklist", morning.Share.Files["nightly/2026-11-03/0600-messages-sonnet-5-5/DONE.md"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_leaders_at_one_company_are_never_invited_the_same_morning()
    {
        const string Text = "[First name],\n\nYour platform work at Northwind is close to what I built. My work: theyard.stevenstout.biz/?r=[code]\n\nWould it be a bad idea to connect?\n\nThanks,\nSteve";
        var morning = new Morning(Quiet);
        morning.Share.Files["linkedin-proof/QUEUE-qa-2026-11-04.md"] = $"### 1. Northwind Widgets: CONNECTION REQUEST to the engineering leader\n\n```\n{Text}\n```\n\n### 2. Northwind Widgets: CONNECTION REQUEST to the recruiter\n\n```\n{Text}\n```\n";
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Contains("already on this worklist", morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/DONE.md"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_application_recovered_from_progress_keeps_the_company_out_of_the_person_column()
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            morning.Share.Files["nightly/2026-11-03/0600-apply-sonnet-5-5/progress.tsv"] = "1\tsent\tAcme Dental\n";
            return new ClaudeRun(137, true, null);
        });
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Apply), default);
        Assert.StartsWith("NA,", morning.Share.Files[Assert.Single(morning.TrackerRows())], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_leader_the_lane_would_pick_never_rides_beside_a_named_invite_at_the_same_company()
    {
        const string Named = "Kim,\n\nYour platform work at Northwind is close to what I built. My work: theyard.stevenstout.biz/?r=[code]\n\nWould it be a bad idea to connect?\n\nThanks,\nSteve";
        string unnamed = Named.Replace("Kim,", "[First name],", StringComparison.Ordinal);
        var morning = new Morning(Quiet);
        morning.Share.Files["linkedin-proof/QUEUE-qa-2026-11-04.md"] = $"### 1. Kim Park, Northwind Widgets: CONNECTION REQUEST\n\n```\n{Named}\n```\n\n### 2. Northwind Widgets: CONNECTION REQUEST to the engineering leader\n\n```\n{unnamed}\n```\n\n### 3. Northwind Widgets: CONNECTION REQUEST to the recruiter\n\n```\n{unnamed}\n```\n";
        morning.Share.Files["linkedin-proof/QUEUE-qb-2026-11-04.md"] = $"### 1. Northwind Gadgets: CONNECTION REQUEST to the engineering leader\n\n```\n{unnamed}\n```\n\n### 2. Kim Lane, Northwind Gadgets: CONNECTION REQUEST\n\n```\n{Named}\n```\n";
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        string list = morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/worklist.tsv"];
        Assert.Single(list.Split('\n'), line => line.Contains("Northwind Widgets", StringComparison.Ordinal));
        Assert.Single(list.Split('\n'), line => line.Contains("Northwind Gadgets", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_heading_that_names_a_role_and_not_a_person_is_still_a_row_the_lane_names()
    {
        const string Text = "[First name],\n\nYour platform work at Northwind is close to what I built. My work: theyard.stevenstout.biz/?r=[code]\n\nWould it be a bad idea to connect?\n\nThanks,\nSteve";
        var morning = new Morning(Quiet);
        morning.Share.Files["linkedin-proof/QUEUE-qa-2026-11-04.md"] = $"### 1. Engineering leader, Northwind Widgets: CONNECTION REQUEST\n\n```\n{Text}\n```\n\n### 2. Recruiter, Northwind Widgets: CONNECTION REQUEST\n\n```\n{Text}\n```\n";
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        string[] rows = [.. morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/worklist.tsv"].Split('\n').Where(line => line.Contains("Northwind", StringComparison.Ordinal))];
        Assert.Equal("", Assert.Single(rows).Split('\t')[1]);
    }

    [Fact]
    public async Task A_progress_name_that_differs_from_the_result_on_the_same_row_holds_the_lane()
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            if (IsRecount(request))
            {
                return Quiet(request);
            }
            morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/progress.tsv"] = "1\tsent\tJohn Other\n";
            return Answer(Result(Row(1, "sent", "Sam Roe", Evidence)));
        });
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Contains("John Other", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_placeholder_in_any_case_leaves_the_name_to_the_lane()
    {
        const string Text = "[First Name],\n\nYour platform work at Northwind is close to what I built. My work: theyard.stevenstout.biz/?r=[code]\n\nWould it be a bad idea to connect?\n\nThanks,\nSteve";
        var morning = new Morning(Quiet);
        morning.Share.Files["linkedin-proof/QUEUE-qa-2026-11-04.md"] = $"### 1. VP Eng, Northwind Widgets: CONNECTION REQUEST\n\n```\n{Text}\n```\n";
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        string row = morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/worklist.tsv"].Split('\n').Single(line => line.Contains("Northwind", StringComparison.Ordinal));
        Assert.Equal("", row.Split('\t')[1]);
    }

    [Fact]
    public async Task A_heading_name_already_invited_holds_its_placeholder_block()
    {
        const string Text = "[First name],\n\nYour platform work at Northwind is close to what I built. My work: theyard.stevenstout.biz/?r=[code]\n\nWould it be a bad idea to connect?\n\nThanks,\nSteve";
        string tracker = Fixture.Read("tracker.csv") + "3500,Jane Quill,Beta Corp,LinkedIn,6/1/2026,,CONNECTION REQUEST SENT by Claude.\n";
        var morning = new Morning(Quiet, tracker);
        morning.Share.Files["linkedin-proof/QUEUE-qa-2026-11-04.md"] = $"### 1. Jane Quill, Northwind Widgets: CONNECTION REQUEST\n\n```\n{Text}\n```\n";
        await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.DoesNotContain("Northwind", morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/worklist.tsv"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_progress_name_and_a_result_name_that_share_only_a_company_prefix_still_differ()
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            if (IsRecount(request))
            {
                return Quiet(request);
            }
            morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/progress.tsv"] = "1\tsent\tAcme, John Doe\n";
            return Answer(Result(Row(1, "sent", "Acme, Jane Roe", Evidence)));
        });
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);
        Assert.Contains("John Doe", output, StringComparison.Ordinal);
    }
}
