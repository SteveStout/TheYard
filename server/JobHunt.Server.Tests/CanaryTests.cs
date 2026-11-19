namespace JobHunt.Server.Tests;

/// <summary>
/// The breaker's hold before each lane and the canary (selfheal1120 version 3): a held lane is skipped with its reason
/// and the rest of the morning runs; a canary is a dry run of one lane on one row, four minutes at most, that answers
/// whether the lane reached its target and never starts from 05:28.
/// </summary>
public sealed class CanaryTests
{
    private const string Hold = "Lane: server6am messages\nHeld: 2026-11-19 14:02 CST\nSource: check_empty_runs.py\nReason: 8 empty runs in a row since 11/12\n";

    private static ClaudeRun Quiet(ClaudeRequest request) => new(0, false, ResultLine.Empty);

    private static ClaudeRun Answered(ClaudeRequest request) =>
        new(0, false, ResultLine.With("""{"rows":[],"live_invite_count":17,"stop_condition":"none","stop_detail":null,"stop_screenshot":null}"""));

    private static bool IsLane(ClaudeRequest request, Lane lane) => request.Arguments[1].Contains(RunSlot.Doc(lane), StringComparison.Ordinal);

    [Fact]
    public async Task A_lane_the_breaker_holds_is_skipped_with_its_reason_and_the_rest_of_the_morning_runs()
    {
        var morning = new Morning(Quiet);
        morning.Share.Files[RunSlot.BreakerHold(Lane.Messages)] = Hold;

        (_, string output) = await morning.Lanes().ExecuteAsync(new RunOptions(false, null), default);

        Assert.DoesNotContain(morning.Claude.Requests, request => IsLane(request, Lane.Messages));
        Assert.Contains(morning.Claude.Requests, request => IsLane(request, Lane.Invites));
        Assert.Contains(morning.Claude.Requests, request => IsLane(request, Lane.Apply));
        Assert.Contains("messages: held, held by the breaker (J:\\dispatch\\held\\server6am-messages.hold): 8 empty runs in a row since 11/12", output, StringComparison.Ordinal);
    }

    [Fact]
    public void Canary_takes_one_lane_and_is_dry_by_construction()
    {
        Assert.Equal(new RunOptions(true, Lane.Invites, RunSlot.Canary, null, true), RunOptions.Parse(["run", "--canary", "--lane", "invites"]));
        Assert.Null(RunOptions.Parse(["run", "--canary"]));
        Assert.Null(RunOptions.Parse(["run", "--canary", "--lane", "apply", "--slot", "pounce-123456", "--role", "nightly/pounce/x.json"]));

        var config = new ServerConfig { DryRun = false, DryRows = 2 };
        config.Lanes["apply"] = new LaneConfig { TimeoutMinutes = 40 };
        CanaryRun.Fit(config, Lane.Apply);
        CanaryRun.Fit(config, Lane.Invites);
        Assert.Equal((true, 1, 4, 4), (config.DryRun, config.DryRows, config.For(Lane.Apply).TimeoutMinutes, config.For(Lane.Invites).TimeoutMinutes));
    }

    [Fact]
    public async Task A_canary_runs_its_one_lane_dry_on_one_row_and_passes_when_the_lane_answers()
    {
        var morning = new Morning(Answered);
        morning.Time.Now = Morning.At.AddHours(4);
        morning.Share.Files[RunSlot.BreakerHold(Lane.Invites)] = Hold;
        CanaryRun.Fit(morning.Config, Lane.Invites);

        (int exit, string output) = await morning.Lanes().ExecuteAsync(RunOptions.Parse(["run", "--canary", "--lane", "invites"])!, default);

        Assert.Equal(CanaryRun.Passed, exit);
        Assert.Contains("DRY RUN", output, StringComparison.Ordinal);
        Assert.All(morning.Claude.Requests.Where(request => !request.Arguments[1].Contains("section Recount", StringComparison.Ordinal)), request => Assert.True(IsLane(request, Lane.Invites)));
        Assert.Empty(morning.TrackerRows());
        string list = morning.Share.Files["nightly/2026-11-03/canary-invites-sonnet-5-5-dry/worklist.tsv"];
        Assert.Equal(2, list.TrimEnd().Split('\n').Length);
    }

    [Fact]
    public async Task A_canary_whose_lane_gives_no_answer_fails()
    {
        var morning = new Morning(_ => new ClaudeRun(1, false, null));
        morning.Time.Now = Morning.At.AddHours(4);
        CanaryRun.Fit(morning.Config, Lane.Invites);

        (int exit, _) = await morning.Lanes().ExecuteAsync(RunOptions.Parse(["run", "--canary", "--lane", "invites"])!, default);

        Assert.Equal(CanaryRun.Failed, exit);
    }

    [Fact]
    public async Task A_canary_whose_lane_has_nothing_on_its_worklist_is_not_run()
    {
        var morning = new Morning(Answered);
        morning.Time.Now = Morning.At.AddHours(4);
        CanaryRun.Fit(morning.Config, Lane.Messages);
        foreach (string path in morning.Share.Files.Keys.Where(path => path.Contains("queue", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            morning.Share.Files.Remove(path);
        }

        (int exit, _) = await morning.Lanes().ExecuteAsync(RunOptions.Parse(["run", "--canary", "--lane", "messages"])!, default);

        Assert.Equal(CanaryRun.NotRun, exit);
    }

    [Fact]
    public void A_canary_from_05_28_to_07_00_is_not_run_and_says_so()
    {
        Assert.Contains("the apply canary was not run", CanaryRun.NotStarted(Morning.At.AddMinutes(-32), Lane.Apply), StringComparison.Ordinal);
        Assert.NotNull(CanaryRun.NotStarted(Morning.At.AddMinutes(59), Lane.Apply));
        Assert.Null(CanaryRun.NotStarted(Morning.At.AddMinutes(-33), Lane.Apply));
        Assert.Null(CanaryRun.NotStarted(Morning.At.AddHours(4), Lane.Apply));
        Assert.Equal(4, CanaryRun.NotRun);
    }
}
