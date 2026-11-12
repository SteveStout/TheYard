using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Server.Tests;

/// <summary>
/// The program for one caught role at any hour: a slot names its folders and its card, the command line takes a slot
/// and a hand-off together, no slot runs from 05:30 to 07:00 Central, the feed works that one role and writes its own
/// queue, the invites lane works that queue only, a budget-flagged role may ask about the timeline and no other may,
/// and a slot that already ran live does not run again.
/// </summary>
public sealed class SlotTests
{
    private const string Handoff = "nightly/pounce/20261103-153501Z-12786.json";
    private const string Slot = "pounce-12786";
    private const string Timeline = "Kim,\n\nYour platform work caught my eye. I cut release cycle time 50% on one .NET platform.\n\ntheyard.stevenstout.biz/?r=[code]\n\nWhat does your timeline look like for getting someone started?\n\nThanks,\nSteve";
    private const string NoOriented = "Kim,\n\nYour platform work caught my eye. I cut release cycle time 50% on one .NET platform.\n\ntheyard.stevenstout.biz/?r=[code]\n\nWould it be a bad idea to connect?\n\nThanks,\nSteve";
    private static readonly DateOnly Today = new(2026, 11, 3);
    private static readonly DateTimeOffset TenCentral = new(2026, 11, 3, 16, 0, 0, TimeSpan.Zero);

    // The shape the Function's PounceHandoff.Json writes, key for key, as its PounceTickTests pin it.
    private static string Json(bool budget = false) =>
        $$"""{"key":12786,"company":"Zephyr Labs","title":"Staff Software Engineer - Web","url":"https://job-boards.greenhouse.io/zephyr/jobs/1","band":"PASS","live_at":"2026-11-03T15:25:00+00:00","live_known":true,"budget":{{(budget ? "true" : "false")}},"passed_at":"2026-11-03T15:35:01+00:00"}""";

    private static string Candidates(string text) =>
        "{\"candidates\":["
        + $"{{\"person\":\"Kim Hot\",\"company\":\"Zephyr Labs\",\"seat\":\"VP Engineering\",\"profile_url\":\"https://www.linkedin.com/in/kimhot\",\"hot\":\"A\",\"why\":\"caught 10:25\",\"common_point\":\"Mizzou\",\"common_source\":\"LinkedIn\",\"band\":\"$210K posted\",\"text\":{System.Text.Json.JsonSerializer.Serialize(text)},\"action\":\"invite\"}},"
        + $"{{\"person\":\"Lee Off\",\"company\":\"Other Co\",\"seat\":\"CTO\",\"profile_url\":\"https://www.linkedin.com/in/leeoff\",\"hot\":\"A\",\"why\":\"x\",\"common_point\":\"\",\"common_source\":\"\",\"band\":\"\",\"text\":{System.Text.Json.JsonSerializer.Serialize(NoOriented.Replace("Kim", "Lee", StringComparison.Ordinal))},\"action\":\"invite\"}}"
        + "]}";

    private static Morning RoleMorning(string text, bool budget = false, bool dry = false, string? applyRow = null)
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            if (request.Arguments[1].Contains("FEED-LANE.md", StringComparison.Ordinal))
            {
                morning.Share.Files[$"nightly/2026-11-03/{Slot}-feed-opus-5-5{(dry ? "-dry" : string.Empty)}/candidates.json"] = Candidates(text);
            }
            if (applyRow is not null && request.Arguments[1].Contains("APPLY-LANE.md", StringComparison.Ordinal))
            {
                return new ClaudeRun(0, false, ResultLine.With($$"""{"rows":[{{applyRow}}],"live_invite_count":null,"stop_condition":"none","stop_detail":null,"stop_screenshot":null}"""));
            }
            return new ClaudeRun(0, false, ResultLine.Empty);
        });
        morning.Config.Lanes["feed"].Enabled = true;
        morning.Time.Now = TenCentral;
        morning.Share.Files[Handoff] = Json(budget);
        return morning;
    }

    [Fact]
    public void A_budget_row_may_ask_about_the_timeline_and_no_other_row_may()
    {
        Assert.Empty(TextCheck.Problems(Timeline, invite: true, budget: true));
        Assert.Empty(TextCheck.Problems(NoOriented, invite: true, budget: true));
        Assert.Contains(TextCheck.Problems(Timeline, invite: true), problem => problem.Contains("no-oriented", StringComparison.Ordinal));
        Assert.NotEmpty(TextCheck.Problems(Timeline.Replace("?\n\nThanks", "? Would it be a bad idea to connect?\n\nThanks", StringComparison.Ordinal), invite: true, budget: true));
    }

    [Fact]
    public void A_slot_names_its_folders_and_its_card_and_the_morning_keeps_0600()
    {
        Assert.Equal("nightly/2026-11-03/pounce-12786-invites-sonnet-5-5", RunSlot.RunFolder(Today, Lane.Invites, "claude-sonnet-5-5", false, Slot));
        Assert.Equal("nightly/2026-11-03/0600-invites-sonnet-5-5-dry", RunSlot.RunFolder(Today, Lane.Invites, "claude-sonnet-5-5", true));
        Assert.Equal(("nightly/2026-11-03/pounce-12786-card.md", "nightly/2026-11-03/0600-card.md"), (RunSlot.Card(Today, Slot), RunSlot.Card(Today, RunSlot.Hour)));
        Assert.True(WritePolicy.Allowed("linkedin-proof/QUEUE-pounce-2026-11-03-12786.md"));
        Assert.False(WritePolicy.Allowed("linkedin-proof/QUEUE-pounce-2026-11-03-x.md"));
    }

    [Theory]
    [InlineData(11, 29, false)]
    [InlineData(11, 30, true)]
    [InlineData(12, 59, true)]
    [InlineData(13, 0, false)]
    public void No_slot_runs_from_half_past_five_to_seven_Central(int hour, int minute, bool quiet) =>
        Assert.Equal(quiet, RunSlot.Quiet(new DateTimeOffset(2026, 11, 11, hour, minute, 0, TimeSpan.Zero)));

    [Fact]
    public void The_command_line_takes_a_slot_and_a_hand_off_together_or_neither()
    {
        Assert.Equal(new RunOptions(true, null, Slot, Handoff), RunOptions.Parse(["run", "--slot", Slot, "--role", Handoff, "--dry-run"]));
        Assert.Equal(new RunOptions(false, Lane.Invites), RunOptions.Parse(["run", "--lane", "invites"]));
        Assert.Equal(RunSlot.Hour, RunOptions.Parse(["run"])!.Slot);
        Assert.Null(RunOptions.Parse(["run", "--slot", Slot]));
        Assert.Null(RunOptions.Parse(["run", "--role", Handoff]));
        Assert.Null(RunOptions.Parse(["run", "--slot", "0600", "--role", Handoff]));
        Assert.Null(RunOptions.Parse(["run", "--slot", "../0600", "--role", Handoff]));
        Assert.Null(RunOptions.Parse(["run", "--slot", Slot, "--role", "state.md"]));
    }

    [Fact]
    public void The_hand_off_reads_as_the_function_writes_it_and_anything_else_reads_as_none()
    {
        RoleHandoff role = RoleHandoff.Parse(Handoff, Json(budget: true))!;
        Assert.Equal((12786L, "Zephyr Labs", "Staff Software Engineer - Web", true, Handoff), (role.Key, role.Company, role.Title, role.Budget, role.Path));
        Assert.Equal(new DateTimeOffset(2026, 11, 3, 15, 25, 0, TimeSpan.Zero), role.LiveAt);
        Assert.Null(RoleHandoff.Parse(Handoff, "{\"company\":\"Zephyr Labs\"}"));
        Assert.Null(RoleHandoff.Parse(Handoff, "not json"));
    }

    [Fact]
    public async Task A_slot_run_feeds_one_role_writes_its_own_queue_and_the_invites_lane_works_that_queue_only()
    {
        Morning morning = RoleMorning(NoOriented);
        (int exit, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, null, Slot, Handoff), default);

        Assert.Equal(0, exit);
        string prompt = morning.Claude.Requests.First(request => request.Arguments[1].Contains("FEED-LANE.md", StringComparison.Ordinal)).Arguments[1];
        Assert.Contains($"POUNCE_ROLE=/mnt/j/{Handoff}", prompt, StringComparison.Ordinal);
        Assert.Contains("Zephyr Labs", prompt, StringComparison.Ordinal);
        string queue = morning.Share.Files["linkedin-proof/QUEUE-pounce-2026-11-03-12786.md"];
        Assert.Contains("# QUEUE-pounce-2026-11-03-12786", queue, StringComparison.Ordinal);
        Assert.Contains("### 1. Kim Hot, Zephyr Labs (VP Engineering). Code [code].", queue, StringComparison.Ordinal);
        Assert.DoesNotContain("Other Co", queue, StringComparison.Ordinal);
        Assert.Contains("not the caught role's company", morning.Share.Files[$"nightly/2026-11-03/{Slot}-feed-opus-5-5/DONE.md"], StringComparison.Ordinal);
        string[] worklist = morning.Share.Files[$"nightly/2026-11-03/{Slot}-invites-sonnet-5-5/worklist.tsv"].TrimEnd().Split('\n');
        Assert.Equal(2, worklist.Length);
        Assert.Contains("Kim Hot\tZephyr Labs", worklist[1], StringComparison.Ordinal);
        Assert.True(morning.Share.Files.ContainsKey($"nightly/2026-11-03/{Slot}-card.md"));
        Assert.False(morning.Share.Files.ContainsKey("nightly/2026-11-03/0600-card.md"));
        Assert.DoesNotContain(morning.Share.Files.Keys, path => path.Contains("-messages-", StringComparison.Ordinal) || path.Contains("-network-", StringComparison.Ordinal));
        Assert.Contains("feed: 1 queued", output, StringComparison.Ordinal);
        Assert.All(morning.Usage.Documents, document => Assert.Contains("\"slot\":\"10:00\"", document, StringComparison.Ordinal));
        Assert.NotEmpty(morning.Usage.Documents);
    }

    [Fact]
    public async Task A_dry_slot_run_sends_its_invites_lane_the_dry_queue_and_writes_nothing_in_linkedin_proof()
    {
        Morning morning = RoleMorning(Timeline, budget: true, dry: true);
        var before = morning.Share.Files.Keys.Where(path => path.StartsWith("linkedin-proof/", StringComparison.Ordinal)).ToList();
        await morning.Lanes().ExecuteAsync(new RunOptions(true, null, Slot, Handoff), default);

        Assert.Equal(before, morning.Share.Files.Keys.Where(path => path.StartsWith("linkedin-proof/", StringComparison.Ordinal)).ToList());
        Assert.Contains("- Budget: yes", morning.Share.Files[$"nightly/2026-11-03/{Slot}-feed-opus-5-5-dry/QUEUE-pounce-2026-11-03-12786.md"], StringComparison.Ordinal);
        string worklist = morning.Share.Files[$"nightly/2026-11-03/{Slot}-invites-sonnet-5-5-dry/worklist.tsv"];
        Assert.Contains("Kim Hot\tZephyr Labs", worklist, StringComparison.Ordinal);
        Assert.Contains("getting someone started", morning.Share.Files[$"nightly/2026-11-03/{Slot}-invites-sonnet-5-5-dry/texts/01.txt"], StringComparison.Ordinal);
        Assert.Empty(morning.Minter.Asked);
    }

    [Fact]
    public async Task A_timeline_question_on_a_role_with_no_budget_flag_is_dropped()
    {
        Morning morning = RoleMorning(Timeline);
        await morning.Lanes().ExecuteAsync(new RunOptions(false, null, Slot, Handoff), default);

        Assert.False(morning.Share.Files.ContainsKey("linkedin-proof/QUEUE-pounce-2026-11-03-12786.md"));
        Assert.Contains("no-oriented", morning.Share.Files[$"nightly/2026-11-03/{Slot}-feed-opus-5-5/DONE.md"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_slot_that_ran_live_does_not_run_again()
    {
        Morning morning = RoleMorning(NoOriented);
        await morning.Lanes().ExecuteAsync(new RunOptions(false, null, Slot, Handoff), default);
        await morning.Lanes().ExecuteAsync(new RunOptions(false, null, Slot, Handoff), default);

        Assert.Equal(1, morning.Claude.Requests.Count(request => request.Arguments[1].Contains("FEED-LANE.md", StringComparison.Ordinal)));
    }

    private static string Person(string name, string company) =>
        $"{{\"person\":\"{name}\",\"company\":\"{company}\",\"seat\":\"VP Engineering\",\"profile_url\":\"https://www.linkedin.com/in/{name.Replace(" ", "", StringComparison.Ordinal).ToLowerInvariant()}\",\"hot\":\"A\",\"why\":\"x\",\"common_point\":\"\",\"common_source\":\"\",\"band\":\"\",\"text\":{System.Text.Json.JsonSerializer.Serialize(NoOriented.Replace("Kim", name.Split(' ')[0], StringComparison.Ordinal))},\"action\":\"invite\"}}";

    private static Morning PeopleMorning(params string[] people)
    {
        Morning morning = null!;
        morning = new Morning(request =>
        {
            if (request.Arguments[1].Contains("FEED-LANE.md", StringComparison.Ordinal))
            {
                morning.Share.Files[$"nightly/2026-11-03/{Slot}-feed-opus-5-5/candidates.json"] = "{\"candidates\":[" + string.Join(",", people) + "]}";
            }
            return new ClaudeRun(0, false, ResultLine.Empty);
        });
        morning.Config.Lanes["feed"].Enabled = true;
        morning.Time.Now = TenCentral;
        morning.Share.Files[Handoff] = Json();
        return morning;
    }

    [Fact]
    public async Task A_role_run_queues_one_person_and_one_at_another_company_never_crowds_out_the_role_s_own()
    {
        Morning two = PeopleMorning(Person("Kim Hot", "Zephyr Labs"), Person("Pat Two", "Zephyr Labs"));
        await two.Lanes().ExecuteAsync(new RunOptions(false, null, Slot, Handoff), default);
        string queue = two.Share.Files["linkedin-proof/QUEUE-pounce-2026-11-03-12786.md"];
        Assert.DoesNotContain("Pat Two", queue, StringComparison.Ordinal);
        Assert.Contains("one person a caught role", two.Share.Files[$"nightly/2026-11-03/{Slot}-feed-opus-5-5/DONE.md"], StringComparison.Ordinal);

        Morning crowded = PeopleMorning(Person("Kim Hot", "Other Co"), Person("Kim Hot", "Zephyr Labs"));
        await crowded.Lanes().ExecuteAsync(new RunOptions(false, null, Slot, Handoff), default);
        Assert.Contains("### 1. Kim Hot, Zephyr Labs", crowded.Share.Files["linkedin-proof/QUEUE-pounce-2026-11-03-12786.md"], StringComparison.Ordinal);
    }

    // Feed, invites and apply at 40 minutes each, the recount's 8 and 5 for the records: 133 minutes before 11:30 UTC.
    [Theory]
    [InlineData(9, 10, true)]
    [InlineData(9, 20, false)]
    [InlineData(10, 30, false)]
    public async Task A_role_run_that_could_still_hold_the_lock_at_half_past_five_does_not_start(int hour, int minute, bool runs)
    {
        Morning morning = RoleMorning(NoOriented);
        morning.Time.Now = new DateTimeOffset(2026, 11, 3, hour, minute, 0, TimeSpan.Zero);
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, null, Slot, Handoff), default);
        Assert.Equal(runs, morning.Claude.Requests.Count > 0);
        Assert.Equal(!runs, output.Contains("did nothing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_role_run_whose_wait_for_the_lock_reaches_the_cutoff_does_nothing()
    {
        Morning morning = RoleMorning(NoOriented);
        morning.Time.Now = new DateTimeOffset(2026, 11, 3, 9, 5, 0, TimeSpan.Zero);
        (_, string output) = await morning.Lanes(runLock: new SlowLock(morning.Time, TimeSpan.FromMinutes(20))).ExecuteAsync(new RunOptions(false, null, Slot, Handoff), default);
        Assert.Contains("did nothing", output, StringComparison.Ordinal);
        Assert.Empty(morning.Claude.Requests);
        Assert.DoesNotContain(morning.Share.Files.Keys, path => path.StartsWith("inbox/append-inbox/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_slot_and_a_hand_off_that_name_two_postings_do_nothing_and_a_key_past_the_range_reads_as_none()
    {
        Morning morning = RoleMorning(NoOriented);
        (int exit, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, null, "pounce-1", Handoff), default);
        Assert.Equal(1, exit);
        Assert.Contains("names posting 12786", output, StringComparison.Ordinal);
        Assert.Empty(morning.Claude.Requests);

        Assert.Null(RoleHandoff.Parse(Handoff, Json().Replace("\"key\":12786", "\"key\":9223372036854775808", StringComparison.Ordinal)));
    }

    private sealed class SlowLock(FixedTime time, TimeSpan wait) : IRunLock
    {
        public Task<IAsyncDisposable?> TakeAsync(string dayFolder, CancellationToken ct)
        {
            time.Now += wait;
            return new FakeLock().TakeAsync(dayFolder, ct);
        }
    }

    [Fact]
    public async Task A_slot_run_that_died_mid_send_holds_the_morning_s_lane_as_a_morning_run_would()
    {
        var morning = new Morning(_ => new ClaudeRun(0, false, ResultLine.Empty));
        morning.Share.Files["nightly/2026-11-02/pounce-12786-invites-sonnet-5-5/progress.tsv"] = "1\tsent\tKim Hot\n";
        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, Lane.Invites), default);

        Assert.Contains("pounce-12786-invites-sonnet-5-5/progress.tsv", morning.Share.Files["nightly/2026-11-03/0600-invites-sonnet-5-5/DONE.md"], StringComparison.Ordinal);
        Assert.Contains("invites: held", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_slot_run_inside_the_morning_s_window_or_with_no_hand_off_does_nothing()
    {
        Morning quiet = RoleMorning(NoOriented);
        quiet.Time.Now = new DateTimeOffset(2026, 11, 3, 12, 15, 0, TimeSpan.Zero);
        (int exit, string output) = await quiet.Lanes().ExecuteAsync(new RunOptions(false, null, Slot, Handoff), default);
        Assert.Equal(0, exit);
        Assert.Contains("05:30 to 07:00", output, StringComparison.Ordinal);
        Assert.Empty(quiet.Claude.Requests);
        Assert.DoesNotContain(quiet.Share.Files.Keys, path => path.StartsWith("inbox/append-inbox/", StringComparison.Ordinal));

        Morning lost = RoleMorning(NoOriented);
        lost.Share.Files.Remove(Handoff);
        (int lostExit, string lostOutput) = await lost.Lanes().ExecuteAsync(new RunOptions(false, null, Slot, Handoff), default);
        Assert.Equal(1, lostExit);
        Assert.Contains("would not read", lostOutput, StringComparison.Ordinal);
        Assert.Empty(lost.Claude.Requests);
    }

    [Fact]
    public async Task A_slot_run_mints_each_code_under_its_slot_so_the_follow_up_can_tell_a_pounce_note_s_code()
    {
        Morning morning = RoleMorning(NoOriented);
        await morning.Lanes().ExecuteAsync(new RunOptions(false, null, Slot, Handoff), default);
        Assert.Equal(["Kim Hot|Zephyr Labs"], morning.Minter.Asked);
        Assert.Equal([Slot], morning.Minter.Lanes);
    }

    [Fact]
    public async Task A_slot_run_applies_to_its_one_role_after_the_invites_and_reads_no_other_list()
    {
        Assert.Equal([Lane.Feed, Lane.Invites, Lane.Apply], RunSlot.RoleOrder);
        Morning morning = RoleMorning(NoOriented);
        await morning.Lanes().ExecuteAsync(new RunOptions(false, null, Slot, Handoff), default);

        string[] worklist = morning.Share.Files[$"nightly/2026-11-03/{Slot}-apply-sonnet-5-5/worklist.tsv"].TrimEnd().Split('\n');
        Assert.Equal(2, worklist.Length);
        Assert.Contains("Zephyr Labs", worklist[1], StringComparison.Ordinal);
        Assert.Contains("https://job-boards.greenhouse.io/zephyr/jobs/1", worklist[1], StringComparison.Ordinal);
        string prompt = morning.Claude.Requests.Last(request => request.Arguments[1].Contains("APPLY-LANE.md", StringComparison.Ordinal)).Arguments[1];
        Assert.Contains("Zephyr Labs, Staff Software Engineer - Web", prompt, StringComparison.Ordinal);
        Assert.Contains("\"needs Steve:\"", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("needs Steve", ClaudeArguments.Prompt(Lane.Apply, "/mnt/j", "/mnt/j/nightly/x", false), StringComparison.Ordinal);
        Assert.Contains(await ApplyPostings.ReadAsync(morning.Share, Today, default), posting => posting.Source.StartsWith("APPLY-SWEEP-2026-10-28", StringComparison.Ordinal));
        Assert.DoesNotContain(worklist, line => line.Contains("APPLY-SWEEP", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_form_on_the_caught_role_that_needs_Steve_goes_on_the_blockers_list_with_its_exact_control()
    {
        const string Row = """{"row":1,"outcome":"blocked","reason":"needs Steve: the required field \"Desired start date\" has no banked answer","person":null,"title":null,"profile_url":"https://job-boards.greenhouse.io/zephyr/jobs/1","evidence":null}""";
        Morning morning = RoleMorning(NoOriented, applyRow: Row);
        await morning.Lanes().ExecuteAsync(new RunOptions(false, null, Slot, Handoff), default);

        string blockers = morning.Share.Files[$"nightly/2026-11-03/{Slot}-apply-sonnet-5-5/APPLY-BLOCKERS-2026-11-03.md"];
        Assert.Contains("## 1. Zephyr Labs, Staff Software Engineer - Web", blockers, StringComparison.Ordinal);
        Assert.Contains("- The control: the required field \"Desired start date\" has no banked answer", blockers, StringComparison.Ordinal);
        Assert.DoesNotContain("The control", Blockers.Render(Today, [new Posting("Acme", "Staff Engineer", "https://acme.example/jobs/1", "Workday", "JOBS.md:3")]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_slot_run_builds_its_resume_in_its_own_run_folder_and_never_the_morning_s()
    {
        Morning morning = RoleMorning(NoOriented);
        await morning.Lanes().ExecuteAsync(new RunOptions(false, null, Slot, Handoff), default);

        string asked = Assert.Single(morning.Resumes.Asked);
        Assert.Contains($"/nightly/2026-11-03/{Slot}-apply-sonnet-5-5/resumes/01", asked, StringComparison.Ordinal);
        Assert.DoesNotContain("/0600-apply-", asked, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_caught_role_that_went_live_past_the_age_limit_is_not_applied_to()
    {
        Morning morning = RoleMorning(NoOriented);
        morning.Share.Files[Handoff] = Json().Replace("\"live_at\":\"2026-11-03T15:25:00+00:00\"", "\"live_at\":\"2026-10-01T15:25:00+00:00\"", StringComparison.Ordinal);
        await morning.Lanes().ExecuteAsync(new RunOptions(false, null, Slot, Handoff), default);

        Assert.Empty(morning.Resumes.Asked);
        Assert.Contains("skipped: age 33 days (posted 2026-10-01", morning.Share.Files[$"nightly/2026-11-03/{Slot}-apply-sonnet-5-5/DONE.md"], StringComparison.Ordinal);
    }

    [Fact]
    public void The_Function_holds_a_caught_role_s_item_from_where_the_box_starts_refusing_its_run()
    {
        // Two processes, no shared constant: the Function's window is read from its source and held to the box's cutoff,
        // 05:30 less the role run's lead with every lane on at its default limit.
        var config = new ServerConfig { Lanes = RunSlot.RoleOrder.ToDictionary(RunSlot.Word, _ => new LaneConfig()) };
        TimeSpan box = RunSlot.Morning.From - RunLanes.RoleLead(config);
        string source = File.ReadAllText(Path.Combine(Repo(), "functions", "JobHunt.Domain", "Features", "Jobs", "PounceHandoff.cs"));
        Match from = Regex.Match(source, @"Morning = \(new\((\d+), (\d+), 0\)");
        Assert.True(from.Success, "PounceHandoff.Morning was not found");
        TimeSpan function = new(int.Parse(from.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(from.Groups[2].Value, CultureInfo.InvariantCulture), 0);
        Assert.True(function <= box && box - function < TimeSpan.FromMinutes(15), $"the Function holds items from {function:hh\\:mm}, the box refuses role runs from {box:hh\\:mm}");
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
        throw new InvalidOperationException("functions/JobHunt.Domain not found above " + AppContext.BaseDirectory);
    }
}
