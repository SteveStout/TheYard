using JobHunt.Domain.Features.Watch;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The watch's judgments at their edges: a source that would not read, a reading exactly on its limit, a line from
/// another day, a mixed recipient list. <see cref="WatchTests"/> holds each test's failing fixture; these hold where
/// the line between pass and fail sits. Pure (RULE 79).
/// </summary>
public sealed class WatchEdgeTests
{
    private static readonly DateOnly Today = new(2026, 11, 20);
    private static readonly DateTime Nine = new(2026, 11, 20, 9, 0, 0);

    [Fact]
    public void W2_the_recorded_word_and_the_setting_are_read_without_case_or_spaces()
    {
        Assert.True(WatchJudge.Pounce(" Live ", "LIVE since 2026-11-19 14:48 CST").Pass);
        var blank = WatchJudge.Pounce(string.Empty, "live");
        Assert.False(blank.Pass);
        Assert.Equal("not set (dry)", blank.Number);
    }

    [Fact]
    public void W13_an_unread_file_is_not_read_a_not_set_switch_that_is_set_fails_and_expiry_is_14_days()
    {
        var none = Assert.Single(WatchJudge.Switches(new Dictionary<string, string?>(), string.Empty, Today));
        Assert.Null(none.Pass);
        Assert.Equal("0 lines match", none.Number);

        const string SwitchesFile = "## Switches\n\n| Name | Live |\n|---|---|\n| NETWORK_SEND | not set (the code reads shadow) | yes |\n\n"
            + "## Secrets\n\n| Name | Live | In the template | Expiry |\n|---|---|---|---|\n| GMAIL_REFRESH_TOKEN | set | no | none |\n| PROMOTE_TOKEN | set | no | 2026-12-05 |\n";
        var live = new Dictionary<string, string?> { ["NETWORK_SEND"] = "shadow", ["PROMOTE_TOKEN"] = "x" };
        var wrong = WatchJudge.Switches(live, SwitchesFile, Today);
        Assert.Equal(["NETWORK_SEND", "GMAIL_REFRESH_TOKEN"], wrong.Select(r => r.Name));
        Assert.Equal(["live shadow, file not set", "missing"], wrong.Select(r => r.Number));

        live["NETWORK_SEND"] = " ";
        live["GMAIL_REFRESH_TOKEN"] = "x";
        var fine = Assert.Single(WatchJudge.Switches(live, SwitchesFile, Today));
        Assert.True(fine.Pass);
        Assert.Equal("3 lines match", fine.Number);
        Assert.Equal("expires 2026-12-05", Assert.Single(WatchJudge.Switches(live, SwitchesFile, Today.AddDays(1))).Number);
    }

    [Fact]
    public void W16_one_other_address_among_his_own_fails_and_a_quiet_listed_day_passes()
    {
        const string Own = "steve@example.com";
        var mixed = WatchJudge.NoEmailDay(true, [$"{Own}; hiring@acme.com", Own], Own);
        Assert.False(mixed.Pass);
        Assert.Equal("1 to others", mixed.Number);
        Assert.False(WatchJudge.NoEmailDay(true, [$"{Own},hiring@acme.com"], Own).Pass);
        Assert.True(WatchJudge.NoEmailDay(true, ["STEVE@EXAMPLE.COM"], Own).Pass);

        var quiet = WatchJudge.NoEmailDay(true, [], Own);
        Assert.True(quiet.Pass);
        Assert.Equal("0 to others", quiet.Number);
        Assert.Equal("not a listed day", WatchJudge.NoEmailDay(false, [], Own).Number);
    }

    [Fact]
    public void W17_each_limit_passes_on_the_minute_an_unread_age_fails_and_a_night_pause_is_allowed()
    {
        var edge = WatchJudge.Watchers(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(60), 12);
        Assert.All(edge, r => Assert.True(r.Pass));

        var unread = WatchJudge.Watchers(null, null, null, null, 12);
        Assert.Equal(["not read", "not read", "not read", "not paused"], unread.Select(r => r.Number));
        Assert.Equal(new bool?[] { false, false, false, true }, unread.Select(r => r.Pass));

        var longPause = TimeSpan.FromMinutes(61);
        Assert.True(WatchJudge.Watchers(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, longPause, 6)[3].Pass);
        var day = WatchJudge.Watchers(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, longPause, 21)[3];
        Assert.False(day.Pass);
        Assert.Equal("61 min", day.Number);
    }

    [Fact]
    public void W18_data_heals_do_not_spend_the_code_budget_and_a_heal_for_a_heal_is_named()
    {
        string data = string.Concat(Enumerable.Repeat("- 2026-11-20 06:10 CST | leaders | x | 4 heal item (data) | ready\n", 3));
        var spent = WatchJudge.HealBudget(data, Today);
        Assert.True(spent.Pass);
        Assert.Equal("0 code heals today", spent.Number);
        Assert.Equal("1 code heals today, one for a heal lane",
            WatchJudge.HealBudget("- 2026-11-20 06:10 CST | heal-leaders-20261120 | x | 4 heal item (code) | ready\n", Today).Number);
    }

    [Fact]
    public void W8_at_05_30_the_canaries_are_due_and_a_line_from_another_day_is_not_read_as_today()
    {
        const string Log = "2026-11-19T11:15:01Z | Thu 11/19 05:15:01 CST | apply | try 1 | passed | exit 0\n"
            + "a line the log never writes\n"
            + "2026-11-20T11:15:01Z | Fri 11/20 05:15:01 CST | resume | try 1 | not run | exit 4\n";
        var due = WatchJudge.Canaries(Log, new DateTime(2026, 11, 20, 5, 30, 0));
        Assert.Equal(WatchJudge.CanaryLanes.Count, due.Count);
        Assert.Equal("no canary today", due.Single(r => r.Name == "canary apply").Number);
        Assert.Equal(["canary resume"], due.Where(r => r.Pass == true).Select(r => r.Name));
        Assert.Equal("not due before 05:30", Assert.Single(WatchJudge.Canaries(Log, new DateTime(2026, 11, 20, 5, 29, 59))).Number);
    }

    [Fact]
    public void W14_a_proposed_bullet_and_a_queue_it_in_any_case_fail_and_a_plain_card_passes()
    {
        Assert.False(WatchJudge.Reached("# TODAY\n\n- Proposed: merger-header-map\n").Pass);
        Assert.False(WatchJudge.Reached("# TODAY\n\nqueue IT: 2 code fixes ready\n").Pass);
        Assert.Equal("2 lines outside his two jobs", WatchJudge.Reached("Queue it?\n**EMPTY RUN: leaders**\n").Number);

        var clean = WatchJudge.Reached("# TODAY\n\nApplications sent: 3. Proposals wait for a planning session.\n");
        Assert.True(clean.Pass);
        Assert.Equal("0 lines outside his two jobs", clean.Number);
        Assert.True(WatchJudge.Reached(string.Empty).Pass);
    }

    [Fact]
    public void W15_ready_rows_are_numbered_rows_under_their_heading_and_the_file_is_due_by_02_30_today()
    {
        const string Jobs = "## READY TO APPLY\n\n| # | Company |\n|---|---|\n| 1 | Acme |\n| x | not a row |\n\n## NEEDS STEVE\n\n| 2 | Beta |\n";
        Assert.Equal(1, WatchJudge.ReadyRows(Jobs));
        Assert.Equal(0, WatchJudge.ReadyRows(string.Empty));

        var onTime = WatchJudge.Supply(Nine.Date.AddHours(2).AddMinutes(30), 0, 1, Nine);
        Assert.True(onTime.Pass);
        Assert.Equal("candidates 02:30, READY 0 and 1", onTime.Number);
        Assert.False(WatchJudge.Supply(Nine.Date.AddDays(-1).AddHours(1), 1, 1, Nine).Pass);
        Assert.Equal("candidates not written, READY no file and no file", WatchJudge.Supply(null, null, null, Nine).Number);
    }

    [Fact]
    public void WATCH_md_orders_tests_by_number_keeps_each_cell_on_its_row_and_counts_not_read()
    {
        WatchResult[] results =
        [
            new("W12", "b lane", false, "a|b", "card"),
            new("W2", "pounce", true, "live", "setting"),
            new("W12", "a lane", null, "line one\nline two", "card"),
            new("W9x", "odd", true, "x", "y"),
        ];
        string watch = WatchTick.Render(Nine.AddMinutes(13), WatchMode.On, results);
        Assert.Contains("Tick 2026-11-20 09:13 Central, mode on: 2 pass, 1 fail, 1 not read.", watch, StringComparison.Ordinal);
        Assert.Contains("| 09:13 | W12 | b lane | FAIL | a/b | card |", watch, StringComparison.Ordinal);
        Assert.Contains("| 09:13 | W12 | a lane | not read | line one line two | card |", watch, StringComparison.Ordinal);
        string[] order = ["| W2 |", "| W12 | a lane", "| W12 | b lane", "| W9x |"];
        Assert.Equal(order, order.OrderBy(cell => watch.IndexOf(cell, StringComparison.Ordinal)));
        Assert.StartsWith("> RULES FIRST", watch, StringComparison.Ordinal);
    }

    [Fact]
    public void The_change_log_reads_a_windows_line_keeps_tabs_out_of_a_name_and_is_named_by_the_day()
    {
        WatchResult[] one = [new("W12", "tab\there", true, "ran\t09:27", "card")];
        string first = WatchTick.Changes(Nine, string.Empty, one);
        Assert.Equal("09:00\tW12\ttab here\tpass\tran 09:27\n", first);
        Assert.Equal(string.Empty, WatchTick.Changes(Nine.AddMinutes(15), first.Replace("\n", "\r\n", StringComparison.Ordinal), one));
        Assert.Equal("dispatch/watch/2026-11-20.tsv", WatchTick.ChangeLog(Today));
        Assert.Equal(WatchMode.Off, WatchTick.Mode("shadowy"));
        Assert.Equal("not read", new WatchResult("W1", "x", null, "y", "z").Word);
    }
}
