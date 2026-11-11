using TheYard.Api.Features.Admin.Cloud;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// Time to first touch on the Admin tab (ADR-109, the addendum on time to first touch): the Function's PounceDay line
/// read into its day and six counts, the newest line a day winning, a day with no line read as unread, and the one
/// judgement a day gets against a median of 60 minutes. The line's shape is the Function's own RunLine text.
/// </summary>
public sealed class PounceTests
{
    // 03:00 CST Wed 11/11 is 09:00 UTC; the Central day is 2026-11-11.
    private static readonly DateTimeOffset Now = new(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    private static PounceDay Day(string day, long touched = 2, long median = 41, long waiting = 1, long caught = 3) =>
        new(day, caught, touched, median, 75, waiting, 130);

    // #region the-line
    [Fact]
    public void A_line_in_the_Functions_run_line_shape_reads_into_its_day_and_six_counts()
    {
        var day = InsightsPounce.Parse("process:PounceDay outcome=success took_ms=12 started=2026-11-11T09:05:00Z detail=2026-11-10 caught=3 touched=2 median_min=41 worst_min=75 waiting=1 oldest_wait_min=130");

        Assert.Equal(new PounceDay("2026-11-10", 3, 2, 41, 75, 1, 130), day);
    }

    [Fact]
    public void A_line_with_no_day_is_dropped_and_a_count_it_leaves_out_is_zero()
    {
        Assert.Null(InsightsPounce.Parse("process:PounceDay outcome=success caught=3"));
        Assert.Null(InsightsPounce.Parse(null));
        // A count is matched whole: "worst_min" is not "min", and "median_min" is not "worst_min".
        Assert.Equal(new PounceDay("2026-11-10", 0, 0, 7, 0, 0, 0), InsightsPounce.Parse("process:PounceDay detail=2026-11-10 median_min=7"));
    }

    [Fact]
    public void The_query_reads_only_the_roles_successful_day_lines()
    {
        var kusto = new InsightsPounce(new AzureMetrics("", configured: false), "workspace", "func-o'k").Kusto(TimeSpan.FromDays(8));

        Assert.Contains("ago(192h)", kusto, StringComparison.Ordinal);
        Assert.Contains("AppRoleName == 'func-ok'", kusto, StringComparison.Ordinal);
        Assert.Contains("startswith 'process:PounceDay outcome=success '", kusto, StringComparison.Ordinal);
    }
    // #endregion the-line

    // #region the-rules
    [Theory]
    [InlineData(2, 41, 1, 3, "on_target")]
    [InlineData(2, 60, 0, 2, "on_target")]
    [InlineData(1, 61, 0, 1, "late")]
    [InlineData(0, 0, 2, 2, "waiting")]
    [InlineData(0, 0, 0, 0, "quiet")]
    public void A_day_is_judged_on_its_median_against_60_minutes_and_on_what_still_waits(long touched, long median, long waiting, long caught, string judgement)
    {
        Assert.Equal(judgement, PounceRules.Judge(Day("2026-11-10", touched, median, waiting, caught)));
    }

    [Fact]
    public void A_day_with_no_line_is_unread()
    {
        Assert.Equal("unread", PounceRules.Judge(null));
    }

    [Fact]
    public void The_week_is_seven_Central_days_newest_first_and_the_newest_line_a_day_wins_in_any_order()
    {
        var week = PounceRules.Week(
            [
                (Now.AddMinutes(-10), Day("2026-11-10", median: 90)),
                (Now.AddHours(-5), Day("2026-11-10", median: 30)),
                (Now.AddDays(-3), Day("2026-11-08")),
                (Now.AddDays(-9), Day("2026-11-02")),
            ],
            Now);

        Assert.Equal(["2026-11-11", "2026-11-10", "2026-11-09", "2026-11-08", "2026-11-07", "2026-11-06", "2026-11-05"], week.Select(day => day.Day));
        Assert.Equal(90, week[1].Line!.MedianMin);
        Assert.Equal([false, true, false, true, false, false, false], week.Select(day => day.Line is not null));
    }

    [Fact]
    public void The_week_ends_on_the_Central_day_not_the_UTC_day()
    {
        // 23:30 CST Tue 11/10 is 05:30 UTC Wed 11/11.
        var week = PounceRules.Week([], new DateTimeOffset(2026, 11, 11, 5, 30, 0, TimeSpan.Zero));

        Assert.Equal("2026-11-10", week[0].Day);
    }
    // #endregion the-rules

    // #region the-handler
    [Fact]
    public async Task The_handler_answers_seven_days_each_from_its_newest_line_with_the_target_and_the_read_time()
    {
        var reader = new FakeReader([
            (Now.AddHours(-6), Day("2026-11-10", touched: 0, median: 0, waiting: 2, caught: 2)),
            (Now.AddMinutes(-4), Day("2026-11-10", touched: 1, median: 75, waiting: 1)),
        ]);

        var report = await new ReadPounceHandler(reader, new FixedClock(Now)).HandleAsync(new ReadPounceQuery(), CancellationToken.None);

        Assert.Equal((true, null, 7, 60), (report.Available, report.Note, report.Days.Count, report.TargetMin));
        Assert.Equal(new PounceDayRow("2026-11-10", true, 3, 1, 75, 75, 1, 130, "late"), report.Days[1]);
        Assert.Equal(new PounceDayRow("2026-11-11", false, 0, 0, 0, 0, 0, 0, "unread"), report.Days[0]);
        Assert.Equal((Now.AddMinutes(-4).ToUnixTimeMilliseconds(), Now.ToUnixTimeMilliseconds()), (report.NewestMs, report.ReadMs));
    }

    [Fact]
    public async Task An_empty_week_is_seven_unread_days_that_say_so()
    {
        var report = await new ReadPounceHandler(new FakeReader([]), new FixedClock(Now)).HandleAsync(new ReadPounceQuery(), CancellationToken.None);

        Assert.True(report.Available);
        Assert.Equal("the Function has not written a PounceDay line in the last seven days", report.Note);
        Assert.All(report.Days, day => Assert.Equal((false, "unread"), (day.Read, day.Judgement)));
        Assert.Null(report.NewestMs);
    }

    [Fact]
    public async Task A_reader_that_fails_is_the_cards_unread_with_its_reason_never_the_routes_500()
    {
        var report = await new ReadPounceHandler(new FakeReader([], fails: true), new FixedClock(Now)).HandleAsync(new ReadPounceQuery(), CancellationToken.None);

        Assert.False(report.Available);
        Assert.Equal(7, report.Days.Count);
        Assert.Contains("could not be read (HttpRequestException)", report.Note, StringComparison.Ordinal);
    }
    // #endregion the-handler

    private sealed class FakeReader(IReadOnlyList<(DateTimeOffset At, PounceDay Day)> lines, bool fails = false) : IPounceReader
    {
        public string Source => "the test's lines";

        public bool Available => true;

        public Task<IReadOnlyList<(DateTimeOffset At, PounceDay Day)>> LinesAsync(TimeSpan window, CancellationToken cancellation) =>
            fails ? throw new HttpRequestException("refused") : Task.FromResult<IReadOnlyList<(DateTimeOffset At, PounceDay Day)>>([.. lines.Where(line => line.At > Now - window)]);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
