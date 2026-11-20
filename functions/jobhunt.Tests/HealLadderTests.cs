using JobHunt.Application.Features.Watch;
using JobHunt.Domain.Features.Watch;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The heal ladder for the watch's failures (selfheal1120 version 5, the year-end plan's 7b): its steps by the failing
/// tick, its budget, the outside list, and the item in the dispatcher's fixed text. The ladder is pure; the writer runs
/// on a share born and thrown away with the test (RULE 79).
/// </summary>
public sealed class HealLadderTests
{
    private static readonly DateTimeOffset NineThirteen = new(2026, 11, 20, 15, 13, 0, TimeSpan.Zero);
    private static readonly WatchResult Merger = new("W12", "Merger", false, "no run in 31 min", "/api/admin/cloud/nightly/runs/Merger");
    private static readonly HashSet<string> None = [];

    private const string Playbook = """
        | Reason pattern | Fix | Who | Added |
        |---|---|---|---|
        | Gmail 403 / rate limit | Back off 30 minutes, retry once, then pause the lane (layer 2) | Checker | 2026-11-18 chat1118 |
        | Gmail 403 | Back off 30 minutes | Checker | 2026-11-18 chat1118 |
        """;

    /// <summary>The day's change log with the test turned FAIL at a Central time.</summary>
    private static string FailedAt(string hhmm, WatchResult r) => $"06:13\t{r.Test}\t{r.Name}\tpass\tran\n{hhmm}\t{r.Test}\t{r.Name}\tFAIL\t{r.Number}\n";

    [Fact]
    public void The_first_failing_tick_logs_a_retry_or_the_playbook_row_and_opens_nothing()
    {
        var first = HealLadder.Plan([Merger], FailedAt("09:13", Merger), string.Empty, Playbook, None, true, NineThirteen);
        Assert.Empty(first.Items);
        Assert.Equal("- 2026-11-20 09:13 CST | W12 Merger | no run in 31 min (/api/admin/cloud/nightly/runs/Merger) | 2 retry | the next tick, 15 minutes on; sig=watch:w12-merger", Assert.Single(first.Healed));

        var gmail = Merger with { Name = "ReplyDraft", Number = "Gmail 403 on the third read" };
        Assert.Contains("| 1 playbook | PLAYBOOK.md row \"Gmail 403\": Back off 30 minutes (Checker); sig=watch:w12-replydraft", Assert.Single(HealLadder.Plan([gmail], string.Empty, string.Empty, Playbook, None, true, NineThirteen).Healed), StringComparison.Ordinal);

        var second = HealLadder.Plan([Merger], FailedAt("08:58", Merger), first.Healed[0], Playbook, None, true, NineThirteen);
        Assert.Empty(second.Healed);
        Assert.Empty(second.Items);
        Assert.Empty(HealLadder.Plan([Merger with { Pass = null }], FailedAt("07:00", Merger), string.Empty, Playbook, None, true, NineThirteen).Healed);
    }

    [Fact]
    public void The_third_failing_tick_opens_one_heal_item_in_the_dispatchers_fixed_text()
    {
        var plan = HealLadder.Plan([Merger], FailedAt("08:43", Merger), string.Empty, Playbook, None, false, NineThirteen);
        HealItem item = Assert.Single(plan.Items);
        Assert.Equal(("dispatch/heal-shadow", "30-heal-w12-merger-20261120.md", "dispatch/heal-fixtures/30-heal-w12-merger-20261120.txt"), (item.Folder, item.Name, item.Fixture));
        Assert.StartsWith("Lane: heal-w12-merger-20261120\nHeal: true\nNow: true\nKind: code\nSignature: watch:w12-merger\n", item.Text, StringComparison.Ordinal);
        Assert.Contains("\nApproved_by: Steve 12:50 CST 11/19 (standing, heals of scheduled tasks)\n", item.Text, StringComparison.Ordinal);
        Assert.Contains("\nEntered_by: watch 2026-11-20T15:13:00Z\n", item.Text, StringComparison.Ordinal);
        Assert.Contains("\nFixture: J:\\dispatch\\heal-fixtures\\30-heal-w12-merger-20261120.txt\n", item.Text, StringComparison.Ordinal);
        Assert.Contains("\n\n# HEAL (fixed text, written by code; a model never writes or edits this item)\n\n> RULES FIRST", item.Text, StringComparison.Ordinal);
        Assert.Contains("push ready/heal-w12-merger-20261120-<sha8>", item.Text, StringComparison.Ordinal);
        Assert.Contains("(g) a gate made easier", item.Text, StringComparison.Ordinal);
        Assert.Contains("failing 3 ticks in a row since 11/20 08:43:\n| W12 | Merger | FAIL | no run in 31 min |", item.FixtureText, StringComparison.Ordinal);
        Assert.EndsWith("| 4 heal item (code) | heal-shadow (heal_mode shadow)\\30-heal-w12-merger-20261120.md; sig=watch:w12-merger", plan.Healed[^1], StringComparison.Ordinal);

        Assert.Equal("dispatch/ready", Assert.Single(HealLadder.Plan([Merger], FailedAt("08:43", Merger), string.Empty, Playbook, None, true, NineThirteen).Items).Folder);
        Assert.Empty(HealLadder.Plan([Merger], FailedAt("08:43", Merger), string.Empty, Playbook, new HashSet<string> { item.Name }, true, NineThirteen).Items);
        Assert.Contains("Kind: data\n", Assert.Single(HealLadder.Plan([Merger with { Test = "W15" }], FailedAt("08:43", Merger with { Test = "W15" }), string.Empty, Playbook, None, true, NineThirteen).Items).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Past_two_code_heals_a_day_or_two_attempts_the_failure_goes_to_NOT_WORKING_and_a_data_heal_still_opens()
    {
        const string Two = "- 2026-11-20 06:10 CST | server6am apply | x | 4 heal item (code) | ready\\a; sig=empty:a\n- 2026-11-20 07:10 CST | leaders | x | 4 heal item (code) | ready\\b; sig=empty:b\n";
        var spent = HealLadder.Plan([Merger], FailedAt("08:43", Merger), Two, Playbook, None, true, NineThirteen);
        Assert.Empty(spent.Items);
        Assert.Contains("| 4 refused | past the budget of 2 code heals today; sig=watch:w12-merger", spent.Healed[^1], StringComparison.Ordinal);
        Assert.Equal("NOT WORKING: W12 Merger since 11/20 08:43. Tried: past the budget of 2 code heals today. Needs you: nothing.", Assert.Single(spent.NotWorking));
        Assert.Empty(HealLadder.Plan([Merger], FailedAt("08:43", Merger), Two + string.Join('\n', spent.Healed), Playbook, None, true, NineThirteen).Healed);

        var supply = new WatchResult("W15", "supply", false, "candidates not written", "J:\\nightly");
        Assert.Single(HealLadder.Plan([supply], FailedAt("08:43", supply), Two, Playbook, None, true, NineThirteen).Items);

        const string Tried = "- 2026-11-18 09:00 CST | W12 Merger | x | 4 heal item (code) | ready\\c; sig=watch:w12-merger\n- 2026-11-19 09:00 CST | W12 Merger | x | 4 heal item (code) | ready\\d; sig=watch:w12-merger\n";
        var twice = HealLadder.Plan([Merger], FailedAt("08:43", Merger), Tried, Playbook, None, true, NineThirteen);
        Assert.Empty(twice.Items);
        Assert.Equal("NOT WORKING: W12 Merger since 11/20 08:43. Tried: 2 heals (watch:w12-merger). Needs you: nothing.", Assert.Single(twice.NotWorking));
    }

    [Fact]
    public void A_send_switch_a_secret_and_a_no_email_breach_are_outside_self_heal_and_a_heal_lane_is_never_healed()
    {
        WatchResult[] outside =
        [
            new("W2", "pounce mode after go-live", false, "dry", "POUNCE_RUN"),
            new("W13", "GMAIL_REFRESH_TOKEN", false, "missing", "settings"),
            new("W16", "no-email day", false, "1 to others", "Gmail Sent"),
            new("W12", "heal-w12-merger-20261119", false, "failed", "card"),
        ];
        string log = string.Concat(outside.Select(r => FailedAt("08:13", r)));
        var plan = HealLadder.Plan(outside, log, string.Empty, Playbook, None, true, NineThirteen);
        Assert.Empty(plan.Items);
        Assert.Equal(["(e)", "(d)", "(a)"], plan.Healed.Take(3).Select(l => l[(l.IndexOf("self-heal ", StringComparison.Ordinal) + 10)..][..3]));
        Assert.Contains("| 4 refused | a heal lane's own failure is never healed; sig=watch:w12-heal-w12-merger-20261119", plan.Healed[^1], StringComparison.Ordinal);
        Assert.Contains("Needs you: renew or sign in again: GMAIL_REFRESH_TOKEN.", plan.NotWorking[1], StringComparison.Ordinal);
        Assert.Equal(4, plan.NotWorking.Count);
        Assert.Equal(new string?[] { "e", "d", "a", null }, outside.Select(HealLadder.Outside));
        Assert.Equal("e", HealLadder.Outside(new("W13", "NETWORK_SEND", false, "live on, file shadow", "settings")));
    }

    [Fact]
    public async Task The_writer_puts_the_item_and_fixture_whole_appends_HEALED_md_and_adds_a_NOT_WORKING_line_once()
    {
        using var s = new Scratch();
        s.Write("dispatch/policy.json", "{\"heal_mode\": \"on\"}");
        s.Write("dispatch/HEALED.md", "# HEALED\n\n");
        var heal = new WatchHeal(s.Store);
        var pounce = new WatchResult("W2", "pounce mode after go-live", false, "dry", "POUNCE_RUN");

        var plan = await heal.RunAsync([Merger, pounce], FailedAt("08:43", Merger), NineThirteen, default);
        Assert.Single(plan.Items);
        Assert.StartsWith("Lane: heal-w12-merger-20261120\n", s.Text("dispatch/ready/30-heal-w12-merger-20261120.md"), StringComparison.Ordinal);
        Assert.StartsWith("Watch reading", s.Text("dispatch/heal-fixtures/30-heal-w12-merger-20261120.txt"), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(s.Root, "dispatch", "ready", "30-heal-w12-merger-20261120.md.merger-tmp")));
        Assert.Equal(3, s.Text("dispatch/HEALED.md").Split('\n').Count(l => l.StartsWith("- 2026-11-20 ", StringComparison.Ordinal)));
        Assert.StartsWith("> RULES FIRST", s.Text("dispatch/NOT-WORKING.md"), StringComparison.Ordinal);

        var again = await heal.RunAsync([Merger, pounce], FailedAt("08:43", Merger), NineThirteen.AddMinutes(15), default);
        Assert.Empty(again.Items);
        Assert.Equal(1, s.Text("dispatch/NOT-WORKING.md").Split('\n').Count(l => l.StartsWith("NOT WORKING: W2 ", StringComparison.Ordinal)));

        s.Write("dispatch/policy.json", "not json");
        var shadow = await heal.RunAsync([Merger with { Name = "Sweep" }], FailedAt("08:43", Merger with { Name = "Sweep" }), NineThirteen, default);
        Assert.Equal("dispatch/heal-shadow", Assert.Single(shadow.Items).Folder);
    }
}
