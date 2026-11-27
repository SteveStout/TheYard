using JobHunt.Domain.Features.Watch;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The ladder's reading of a landed heal (<see cref="HealWaits"/>, the nightly-jobs fix R1 items 5 and 6), on the lines
/// HEALED.md held on 26 and 27 November: heal-w12 closed clean at 20:10 waiting on Saturday's 08:00 run, then the watch
/// counted the 25 November failed run again at 00:13 and 06:28; and W9's red main refused past the day's two code heals.
/// Pure: strings in, lines out (RULE 79).
/// </summary>
public sealed class HealWaitsTests
{
    private static readonly WatchResult Campaign = new("W12", "CampaignSend", false, "failed at 08:00", "/api/admin/cloud/nightly/runs/CampaignSend");
    private static readonly HashSet<string> None = [];

    private const string Landed = """
        - 2026-11-26 20:10 CST | w12-campaignsend | watch:w12-campaignsend | 4 heal lane | closed clean
        - 2026-11-26 20:13 CST | W12 CampaignSend | failed at 08:00 (/api/admin/cloud/nightly/runs/CampaignSend) | 4 refused | past the budget of 2 code heals today; sig=watch:w12-campaignsend
        - 2026-11-27 09:00 CST | w12-campaignsend | watch:w12-campaignsend | 4 heal lane | landed, waiting on Sat 11/28 08:00 CST; sig=watch:w12-campaignsend
        """;

    private static string FailedAt(string hhmm, WatchResult r) => $"{hhmm}\t{r.Test}\t{r.Name}\tFAIL\t{r.Number}\n";

    private static DateTimeOffset Central(int day, int hour, int minute) => new(2026, 11, day, hour, minute, 0, TimeSpan.FromHours(-6));

    [Fact]
    public void A_landed_heal_stops_the_old_failed_run_counting_again_until_its_run()
    {
        var tick = HealLadder.Plan([Campaign], FailedAt("00:00", Campaign), Landed, string.Empty, None, true, Central(28, 0, 43));
        Assert.Empty(tick.Healed);
        Assert.Empty(tick.Items);
        Assert.Empty(tick.NotWorking);
        Assert.Equal(new DateTime(2026, 11, 28, 8, 0, 0), HealWaits.Run(Landed.Split('\n'), "watch:w12-campaignsend"));
    }

    [Fact]
    public void After_its_run_a_fail_reopens_it_and_its_attempts_count_again()
    {
        var tick = HealLadder.Plan([Campaign], FailedAt("08:13", Campaign), Landed, string.Empty, None, true, Central(28, 8, 43));
        Assert.Contains("| W12 CampaignSend | failed at 08:00 (/api/admin/cloud/nightly/runs/CampaignSend) | 4 reopened | the run of Sat 11/28 08:00 CST failed; sig=watch:w12-campaignsend", tick.Healed[0], StringComparison.Ordinal);
        Assert.Equal("30-heal-w12-campaignsend-20261128.md", Assert.Single(tick.Items).Name);
        var again = HealLadder.Plan([Campaign], FailedAt("08:13", Campaign), Landed + "\n" + string.Join("\n", tick.Healed), string.Empty, new HashSet<string> { tick.Items[0].Name }, true, Central(28, 8, 58));
        Assert.DoesNotContain(again.Healed, l => l.Contains("4 reopened", StringComparison.Ordinal));
    }

    [Fact]
    public void After_its_run_a_pass_is_fixed_once()
    {
        var pass = Campaign with { Pass = true, Number = "success at 08:00" };
        var tick = HealLadder.Plan([pass], string.Empty, Landed, string.Empty, None, true, Central(28, 8, 13));
        Assert.EndsWith("| 5 fixed | the run of Sat 11/28 08:00 CST passed; sig=watch:w12-campaignsend", Assert.Single(tick.Healed), StringComparison.Ordinal);
        Assert.Empty(HealLadder.Plan([pass], string.Empty, Landed + "\n" + tick.Healed[0], string.Empty, None, true, Central(28, 8, 28)).Healed);
        Assert.Empty(HealLadder.Plan([pass], string.Empty, Landed, string.Empty, None, true, Central(28, 7, 58)).Healed);
    }

    [Fact]
    public void A_product_reading_opens_no_heal_and_spends_no_budget()
    {
        // W11 "8 of 49 reached a second action" opened two heal lanes that built nothing (HEALED.md).
        var review = new WatchResult("W11", "review route conversion", false, "8 of 49 reached a second action", "/api/admin/review");
        var tick = HealLadder.Plan([review], FailedAt("00:13", review), string.Empty, string.Empty, None, true, Central(27, 0, 43));
        Assert.Empty(tick.Items);
        Assert.Contains(tick.Healed, l => l.EndsWith("| 4 refused | a product reading, not a fault: product item, January; sig=watch:w11-review-route-conversion", StringComparison.Ordinal));
        // holdfix1127: a product reading is not a failure, so it is never listed on NOT-WORKING.md; HEALED.md keeps the refusal.
        Assert.Empty(tick.NotWorking);
        Assert.Empty(HealLadder.Plan([review], FailedAt("00:13", review), string.Join("\n", tick.Healed), string.Empty, None, true, Central(27, 0, 58)).Healed);

        var supply = new WatchResult("W15", "supply", false, "candidates not written", "J:\\nightly");
        Assert.Single(HealLadder.Plan([supply], FailedAt("00:13", supply), string.Empty, string.Empty, None, true, Central(27, 0, 43)).Items);
    }

    [Fact]
    public void A_landed_line_that_names_no_run_holds_nothing_and_starts_no_count_again()
    {
        // A "none named" landed line must not reopen two spent attempts (HEALED.md, the W11 heal it let open).
        var merger = Campaign with { Name = "Merger", Number = "no run in 31 min" };
        const string Spent = "- 2026-11-26 09:00 CST | W12 Merger | x | 4 heal item (code) | ready\\c; sig=watch:w12-merger\n- 2026-11-26 12:00 CST | W12 Merger | x | 4 heal item (code) | ready\\d; sig=watch:w12-merger\n"
            + "- 2026-11-27 08:59 CST | w12-merger | watch:w12-merger | 4 heal lane | landed, waiting on the next run, none named; sig=watch:w12-merger\n";
        var tick = HealLadder.Plan([merger], FailedAt("09:13", merger), Spent, string.Empty, None, true, Central(27, 9, 43));
        Assert.Empty(tick.Items);
        Assert.Contains("Tried: 2 heals (watch:w12-merger)", Assert.Single(tick.NotWorking), StringComparison.Ordinal);
        Assert.False(HealWaits.Waiting(Spent.Split('\n'), "watch:w12-merger", new DateTime(2026, 11, 27, 9, 43, 0)));
        Assert.Equal(-1, HealWaits.LandedAt(Spent.Split('\n'), "watch:w12-merger"));
    }

    [Fact]
    public void A_red_mains_heal_is_outside_the_days_two_code_heals()
    {
        const string Two = "- 2026-11-27 00:43 CST | W14 what reached him | x | 4 heal item (code) | ready\\a; sig=watch:w14-what-reached-him\n- 2026-11-27 06:13 CST | W8 canary invites | x | 4 heal item (code) | ready\\b; sig=watch:w8-canary-invites\n";
        var red = new WatchResult("W9", "TheYard up", false, "theyard a234c7a, theyard-cosmos a234c7a, main a234c7a, CI failure", "/api/version on both names, main and CI from the GitHub API");
        var tick = HealLadder.Plan([red], FailedAt("06:58", red), Two, string.Empty, None, true, Central(27, 7, 28));
        Assert.Equal("30-heal-w9-theyard-up-20261127.md", Assert.Single(tick.Items).Name);
        Assert.Contains("| 4 heal item (code, red main) |", tick.Healed[^1], StringComparison.Ordinal);
        Assert.Contains("Waiting on: <Ddd MM/DD HH:MM> CST", tick.Items[0].Text, StringComparison.Ordinal);
        Assert.True(WatchJudge.HealBudget(Two + string.Join('\n', tick.Healed), new DateOnly(2026, 11, 27)).Pass);

        var other = Campaign with { Name = "Merger" };
        var refused = HealLadder.Plan([other], FailedAt("06:58", other), Two + string.Join('\n', tick.Healed), string.Empty, None, true, Central(27, 7, 28));
        Assert.Empty(refused.Items);
        Assert.Contains("past the budget of 2 code heals today", refused.Healed[^1], StringComparison.Ordinal);
    }
}
