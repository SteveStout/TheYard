using JobHunt.Domain.Features.Watch;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The watch's judgments (selfheal1120 version 4, the year-end plan's 7a), each with the failing fixture it exists for
/// and the reading that passes. Pure: the tick's ports are not here (RULE 79).
/// </summary>
public sealed class WatchTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);

    /// <summary>SWITCHES.md's shape as selfheal1120 and golive1119 wrote it on 11/19.</summary>
    private const string Switches = """
        ## Switches

        | Name | Live value 11/19 14:5x | In infra/jobhunt.bicep | Who set it, on whose words | Restore |
        |---|---|---|---|---|
        | MERGER | on | yes | the template | yes |
        | NETWORK_SEND | not set (the code reads shadow) | yes | the template | no |
        | POUNCE_RUN | live since 2026-11-19 14:48 CST (set 14:48:59) | no | golive1119 | yes |

        ## Secrets (name and "set" only; never a value)

        | Name | Live | In the template | Expiry the J: record gives | What it stands for |
        |---|---|---|---|---|
        | GMAIL_REFRESH_TOKEN | set | no | none | Gmail |
        | PROMOTE_TOKEN_TEST | set | no | 2026-11-30 | a test secret |
        """;

    [Fact]
    public void W2_a_dry_pounce_after_go_live_fails_and_a_live_one_passes()
    {
        Assert.False(WatchJudge.Pounce(null, "live since 2026-11-19 14:48 CST").Pass);
        Assert.False(WatchJudge.Pounce("dry", "live").Pass);
        Assert.True(WatchJudge.Pounce("live", "live since 2026-11-19 14:48 CST").Pass);
        Assert.True(WatchJudge.Pounce(null, null).Pass);
        Assert.Contains("go-live not recorded", WatchJudge.Pounce(null, null).Number, StringComparison.Ordinal);
    }

    [Fact]
    public void W13_a_wiped_switch_a_missing_secret_and_a_near_expiry_each_fail()
    {
        var live = new Dictionary<string, string?> { ["MERGER"] = "on", ["POUNCE_RUN"] = "live", ["GMAIL_REFRESH_TOKEN"] = "x", ["PROMOTE_TOKEN_TEST"] = "x" };
        Assert.Equal(["PROMOTE_TOKEN_TEST"], WatchJudge.Switches(live, Switches, Today).Select(r => r.Name));

        var wiped = new Dictionary<string, string?> { ["MERGER"] = "on", ["NETWORK_SEND"] = "on" };
        var wrong = WatchJudge.Switches(wiped, Switches, new DateOnly(2026, 10, 1));
        Assert.Equal(["NETWORK_SEND", "POUNCE_RUN", "GMAIL_REFRESH_TOKEN", "PROMOTE_TOKEN_TEST"], wrong.Select(r => r.Name));
        Assert.All(wrong, r => Assert.False(r.Pass));
        Assert.Equal("live not set, file live", wrong[1].Number);

        var fine = WatchJudge.Switches(live, Switches.Replace("2026-11-30", "2027-11-19", StringComparison.Ordinal), Today);
        Assert.True(Assert.Single(fine).Pass);
    }

    [Fact]
    public void W16_on_a_listed_day_one_mail_to_anyone_else_fails()
    {
        const string Own = "steve@example.com";
        Assert.False(WatchJudge.NoEmailDay(true, [Own, "hiring@acme.com"], Own).Pass);
        Assert.True(WatchJudge.NoEmailDay(true, [Own, $"Steve <{Own}>"], Own).Pass);
        Assert.True(WatchJudge.NoEmailDay(false, ["hiring@acme.com"], Own).Pass);
        Assert.Null(WatchJudge.NoEmailDay(null, [], Own).Pass);
    }

    [Fact]
    public void W17_an_old_heartbeat_a_missed_tick_and_a_long_day_pause_fail()
    {
        var fresh = WatchJudge.Watchers(TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(1), null, 9);
        Assert.All(fresh, r => Assert.True(r.Pass));
        var stale = WatchJudge.Watchers(TimeSpan.FromMinutes(11), null, TimeSpan.FromMinutes(6), TimeSpan.FromMinutes(61), 9);
        Assert.All(stale, r => Assert.False(r.Pass));
        Assert.True(WatchJudge.Watchers(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.FromHours(3), 23)[3].Pass);
    }

    [Fact]
    public void W18_a_third_code_heal_in_a_day_or_a_heal_for_a_heal_fails()
    {
        string two = "- 2026-11-20 06:10 CST | server6am apply | x | 4 heal item (code) | ready\n- 2026-11-20 09:10 CST | leaders | x | 4 heal item (code) | ready\n";
        Assert.True(WatchJudge.HealBudget(two, Today).Pass);
        Assert.False(WatchJudge.HealBudget(two + "- 2026-11-20 11:10 CST | replydraft | x | 4 heal item (code) | ready\n", Today).Pass);
        Assert.False(WatchJudge.HealBudget("- 2026-11-20 06:10 CST | heal-leaders-20261120 | x | 4 heal item (code) | ready\n", Today).Pass);
        Assert.True(WatchJudge.HealBudget(two.Replace("2026-11-20", "2026-11-19", StringComparison.Ordinal) + two, Today).Pass);
    }

    [Fact]
    public void W8_a_lane_with_no_canary_or_a_failed_last_try_fails_and_before_05_30_nothing_is_due()
    {
        const string Log = """
            2026-11-20T11:15:01Z | Fri 11/20 05:15:01 CST | apply | try 1 | passed | exit 0: apply: dry, 1 of 1 rows read
            2026-11-20T11:16:01Z | Fri 11/20 05:16:01 CST | invites | try 1 | failed | exit 3
            2026-11-20T11:16:11Z | Fri 11/20 05:16:11 CST | invites | try 2 | passed | exit 0
            2026-11-20T11:17:01Z | Fri 11/20 05:17:01 CST | messages | try 1 | not run | exit 3: nothing on the day's worklist
            2026-11-20T11:18:01Z | Fri 11/20 05:18:01 CST | board scan | try 1 | passed | 9 postings
            2026-11-19T20:33:42Z | Thu 11/19 14:33:42 CST | resume | try 1 | passed | yesterday's
            """;
        var six = new DateTime(2026, 11, 20, 6, 0, 0);
        var read = WatchJudge.Canaries(Log, six);
        Assert.Equal(["apply", "invites", "messages", "board scan"], read.Where(r => r.Pass == true).Select(r => r.Name[7..]));
        Assert.Equal("no canary today", read.Single(r => r.Name == "canary resume").Number);
        Assert.False(WatchJudge.Canaries(Log.Replace("try 2 | passed", "try 2 | failed", StringComparison.Ordinal), six).Single(r => r.Name == "canary invites").Pass);
        Assert.True(Assert.Single(WatchJudge.Canaries(string.Empty, six.AddMinutes(-31))).Pass);
    }

    [Fact]
    public void W14_a_queue_it_or_an_empty_run_line_on_the_card_fails()
    {
        Assert.False(WatchJudge.Reached("# TODAY\n\n**EMPTY RUN: leaders, 2 runs, since 11/18.**\n").Pass);
        Assert.False(WatchJudge.Reached("# TODAY\n\nProposed: selfheal1118. Queue it?\n").Pass);
        Assert.True(WatchJudge.Reached("# TODAY\n\nNOT WORKING: apply since 11/20 06:10. Tried: x. Needs you: nothing.\n").Pass);
    }

    [Fact]
    public void W15_a_late_candidates_file_or_two_nights_with_no_READY_row_fails()
    {
        const string Jobs = "# JOBS\n\n## READY TO APPLY\n\n| # | Company |\n|---|---|\n| 2 | 1Password | x |\n| 3 | 1Password | y |\n\n## NEEDS STEVE\n\n| 4 | Acme | z |\n";
        Assert.Equal(2, WatchJudge.ReadyRows(Jobs));
        var nine = new DateTime(2026, 11, 20, 9, 0, 0);
        Assert.True(WatchJudge.Supply(nine.Date.AddHours(2).AddMinutes(2), 2, 0, nine).Pass);
        Assert.False(WatchJudge.Supply(nine.Date.AddHours(3), 2, 0, nine).Pass);
        Assert.False(WatchJudge.Supply(null, null, null, nine).Pass);
        Assert.False(WatchJudge.Supply(nine.Date.AddHours(2), 0, 0, nine).Pass);
        Assert.True(WatchJudge.Supply(null, null, null, nine.Date.AddHours(1)).Pass);
    }

    [Fact]
    public void WATCH_md_is_the_newest_tick_whole_and_the_change_log_gets_a_line_only_when_a_word_changes()
    {
        var local = new DateTime(2026, 11, 20, 9, 13, 0);
        WatchResult[] first = [WatchJudge.Pounce("live", "live"), new("W12", "Merger", false, "no run in 31 min", "Application Insights")];
        string watch = WatchTick.Render(local, WatchMode.Shadow, first);
        Assert.Contains("mode shadow: 1 pass, 1 fail, 0 not read", watch, StringComparison.Ordinal);
        Assert.Contains("| 09:13 | W2 | pounce mode after go-live | pass | live |", watch, StringComparison.Ordinal);
        Assert.True(watch.IndexOf("| W2 |", StringComparison.Ordinal) < watch.IndexOf("| W12 |", StringComparison.Ordinal));

        string log = WatchTick.Changes(local, string.Empty, first);
        Assert.Equal(2, log.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.Equal(string.Empty, WatchTick.Changes(local.AddMinutes(15), log, first));
        WatchResult[] healed = [first[0], first[1] with { Pass = true, Number = "ran 09:27" }];
        Assert.Equal("09:28\tW12\tMerger\tpass\tran 09:27\n", WatchTick.Changes(local.AddMinutes(15), log, healed));
        Assert.Equal((WatchMode.Shadow, WatchMode.On, WatchMode.Off), (WatchTick.Mode("shadow"), WatchTick.Mode(" ON "), WatchTick.Mode(null)));
    }
}
