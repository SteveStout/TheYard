using System.Text.Json;
using TheYard.Api.Features.Admin.Cloud;
using TheYard.Application;
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Tests;

/// <summary>
/// The Claude usage card (ADR: Every Claude lane reports its usage), built the way the catalogue
/// builds it, over the ten usage documents the nightly lane wrote on 20 and 21 October: the four
/// rows, the run table with its dry runs, the long run's warning, the slot join's two "not"
/// sentences, the dry runs left out of the averages, and the day's dial at twice the average.
/// </summary>
public sealed class ClaudeCardTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset Central(int day, int hour, int minute) => new(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(-5));

    private static UsageRecord Doc(int day, int hour, int minute, string mode, string model, long wallS, long tokensIn, long cacheWrite, long cacheRead, long tokensOut, decimal cost, string? run = null) =>
        new(ClaudeCard.NightlyLane, UsageKind.Plan, model, tokensIn, cacheWrite, cacheRead, tokensOut, wallS * 1000, cost,
            run ?? $"backfill-2026-10-{day}-{hour:00}{minute:00}", $"{hour:00}:{minute:00}", mode, Central(day, hour, minute));

    /// <summary>The ten usage.json files of 20 and 21 October, as the follow-on prompt measured them.</summary>
    private static readonly UsageRecord[] Ten =
    [
        Doc(20, 16, 4, "bookkeeping-dry", "claude-opus-5-5", 527, 92, 165_165, 4_086_312, 37_876, 2.90m),
        Doc(20, 16, 13, "bookkeeping-dry", "claude-sonnet-5-5", 151, 32, 65_992, 663_544, 9_474, 0.43m),
        Doc(20, 16, 19, "bookkeeping", "claude-opus-5-5", 429, 78, 135_641, 3_546_649, 26_518, 2.33m),
        Doc(20, 16, 33, "bookkeeping", "claude-opus-5-5", 1_405, 148, 252_896, 11_624_283, 51_232, 5.37m),
        Doc(20, 20, 0, "bookkeeping", "claude-opus-5-5", 848, 90, 170_137, 4_845_371, 30_441, 2.94m),
        Doc(21, 0, 0, "bookkeeping", "claude-opus-5-5", 860, 86, 127_660, 3_555_237, 21_947, 2.17m),
        Doc(21, 4, 0, "reply", "claude-opus-5-5", 616, 136, 163_711, 7_966_901, 40_200, 4.08m, run: "18745020001"),
        Doc(21, 8, 0, "bookkeeping", "claude-opus-5-5", 536, 98, 152_435, 5_264_192, 31_920, 2.91m),
        Doc(21, 12, 0, "bookkeeping", "claude-opus-5-5", 537, 98, 133_896, 4_488_283, 28_715, 2.54m),
        Doc(21, 16, 0, "bookkeeping", "claude-opus-5-5", 322, 72, 117_309, 2_935_391, 20_626, 1.94m),
    ];

    /// <summary>The card as ResourceCatalog.Cards builds it, over a usage log holding <paramref name="records"/>.</summary>
    private static async Task<(ResourceCard Card, ClaudeSource Source, IReadOnlyList<ResourceMinute> Minutes)> Catalogued(IEnumerable<UsageRecord> records)
    {
        var log = new MemoryUsageLog("kept in the test");
        foreach (var record in records)
        {
            await log.UpsertAsync(record, CancellationToken.None);
        }
        var source = new ClaudeSource(log, new FixedClock(Now));
        var azure = new AzureMetrics("client-1", configured: false);
        var site = new AzureResources("RG-TEST", "app-test", "sqldb-test", "cosmos-test", "crtest", "log-test", "acs-test", "edge-test", new Uri("https://yard.example"), new DateOnly(2026, 10, 30));
        var cards = ResourceCatalog.Cards(azure, new ResourceDirectory(azure, "/subscriptions/sub-1/resourceGroups/RG-TEST", TimeProvider.System), new NetlifyEdge(null, "edge-test", new Uri("https://yard.example")), (3, "m1"), site, claude: source);
        var card = cards.Single(c => c.Key == "claude");
        Assert.Equal(("plan", "claude-"), (card.Container, card.KindPrefix));
        return (card, source, await card.Minutes(Now.AddMinutes(-10), Now, "1.0.4.28", CancellationToken.None));
    }

    private static ResourceCardInputs Inputs(IReadOnlyList<ResourceMinute> minutes, string window = "30d") =>
        new(window, window == "30d" ? TimeSpan.FromDays(30) : TimeSpan.FromDays(1), Now, new PlanHistoryAvailability(true, "kept in Cosmos DB, 31 days"), minutes, [], [], [], null, "the test site");

    private static NightlyRun Run(string id, long number, int day, int hour) =>
        new(id, number, Central(day, hour, 0), $"{hour:00}:00", GitHubRuns.ByFunction, false, hour == 4 ? "reply" : "bookkeeping", 300, "success");

    // #region claude-card-tests
    [Fact]
    public async Task The_catalogue_draws_four_questions_and_lists_every_run_with_its_dry_runs_said()
    {
        var (card, _, minutes) = await Catalogued(Ten);
        var view = card.View(Inputs(minutes));
        Assert.Equal(["lanes", "runs", "plan", "function"], view.Rows.Select(row => row.Key));
        var table = view.Rows.Single(row => row.Key == "runs").Table;
        Assert.Equal(ClaudeRows.Columns, table.Columns);
        Assert.Equal(10, table.Rows.Count);
        Assert.Equal(2, table.Rows.Count(row => row[2].Text == "bookkeeping (dry run)"));
        Assert.Equal("Claude usage", view.Title);
        Assert.Equal(ClaudeCard.PlanUnread, view.Rows.Single(row => row.Key == "plan").Answer);
        Assert.Equal(ClaudeCard.PlanUnread, view.Dials[1].Absolute);
        Assert.Contains("API-equivalent", view.Readout.Footnote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_run_over_twelve_hundred_seconds_is_a_warning_and_a_dry_one_is_not_counted()
    {
        var (card, _, minutes) = await Catalogued(Ten);
        Assert.Equal("warning", card.View(Inputs(minutes)).Rows.Single(row => row.Key == "runs").Tone);
        var withoutTheBacklog = Ten.Where(record => record.WallMs != 1_405_000).Append(Ten[0] with { WallMs = 2_000_000, Run = "dry-long" });
        var (calm, _, calmMinutes) = await Catalogued(withoutTheBacklog);
        Assert.Equal("good", calm.View(Inputs(calmMinutes)).Rows.Single(row => row.Key == "runs").Tone);
    }

    [Fact]
    public void The_slot_join_names_a_run_with_no_usage_and_usage_whose_run_was_never_listed()
    {
        UsageRecord[] reported = [Ten[6], Ten[7] with { Run = "18745020002" }];
        NightlyRun[] listed = [Run("18745020001", 41, 21, 4), Run("18745020003", 43, 21, 12)];
        var (lines, matched, missing, unseen) = ClaudeRows.SlotJoin(reported, listed);
        Assert.Equal((1, 1, 1), (matched, missing, unseen));
        Assert.Contains($"run #43 (12:00, bookkeeping): {ClaudeRows.NoUsage}", lines);
        Assert.Contains($"run 18745020002 (08:00): {ClaudeRows.NotSeen}", lines);
        Assert.Contains(lines, line => line.StartsWith("04:00: 1 run, 40,200 tokens out, $4.08 API-equivalent", StringComparison.Ordinal));
    }

    [Fact]
    public void Dry_runs_are_left_out_of_the_day_and_the_average_and_twice_the_average_is_a_warning()
    {
        // The seven days before 22 October hold 20 and 21 October's eight counted runs.
        double average = ClaudeCard.WeekAverage(Ten, Now)!.Value;
        Assert.Equal(Ten.Where(record => !record.Dry).Sum(record => record.TokensOut) / 7d, average, 3);
        var later = Now.AddHours(14);
        var today = Ten.Append(Ten[3] with { At = later.AddHours(-1), Run = "today-1", TokensOut = (long)(average * 2) })
            .Append(Ten[0] with { At = later.AddHours(-1), Run = "today-dry", TokensOut = 9_999_999 }).ToList();
        Assert.Equal((long)(average * 2), ClaudeCard.Today(today, later));
        Assert.Equal("warning", ClaudeCard.Tone(ClaudeCard.TodayShare(today, later), CloudThresholds.ClaudeDay));
        Assert.Equal("good", ClaudeCard.Tone(150, CloudThresholds.ClaudeDay));
        Assert.Equal("info", ClaudeCard.Tone(null, CloudThresholds.ClaudeDay));
    }

    [Fact]
    public async Task The_chart_has_a_line_a_lane_in_the_order_the_lanes_first_appear_and_the_lanes_come_from_the_data()
    {
        var personal = new UsageRecord("Personal", UsageKind.Plan, "claude-opus-5-5", 1, 2, 3, 500, 1000, null, "personal-2026-10-21-claude-opus-5-5", null, null, Central(21, 23, 0));
        var (card, source, minutes) = await Catalogued(Ten.Append(personal));
        var view = card.View(Inputs(minutes));
        Assert.Equal([ClaudeCard.NightlyLane, "Personal"], view.Chart.Series.Select(series => series.Name));
        Assert.All(view.Chart.Series.SelectMany(series => series.Points), point => Assert.InRange(point.Value ?? 0, 0, 100));
        Assert.Equal(11, source.Records.Count);
        Assert.StartsWith("2 lanes", view.Rows.Single(row => row.Key == "lanes").Answer, StringComparison.Ordinal);
        string wire = JsonSerializer.Serialize(view);
        Assert.DoesNotContain("nightly/2026", wire, StringComparison.Ordinal);
        Assert.DoesNotContain("DONE", wire, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_log_says_no_document_has_been_posted_and_reads_nothing_as_zero()
    {
        var (card, _, minutes) = await Catalogued([]);
        var view = card.View(Inputs(minutes, "24h"));
        Assert.StartsWith("No usage document has been posted yet", view.Note, StringComparison.Ordinal);
        Assert.Equal("no run reported in the window", view.Rows[0].Answer);
        Assert.Null(view.Dials[0].Percent);
        Assert.Equal(0d, minutes.Single().Values[ClaudeSource.TokensOutToday]);
    }
    // #endregion claude-card-tests

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
