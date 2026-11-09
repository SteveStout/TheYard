using TheYard.Api.Features.Admin.Cloud;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The job hunt's funnel on the Admin tab (ADR-109, the addendum on the funnel): the Function's daily line read into
/// its day and six counts, the days a window holds added up with a day written twice counted once, and the card's
/// tone from the newest seven days' calls booked against two to three a week. The line's shape is the Function's own
/// RunLine text, so the parser is held to a line written the way RunLine.Text writes one.
/// </summary>
public sealed class FunnelTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);

    private static FunnelDay Day(string day, long booked, long applied = 5) => new(day, 200, 12, applied, 15, 2, booked);

    // #region the-line
    [Fact]
    public void A_line_in_the_Functions_run_line_shape_reads_into_its_day_and_six_counts()
    {
        var day = InsightsFunnel.Parse("process:Funnel outcome=success took_ms=812 started=2026-11-09T06:00:00Z detail=2026-11-08 scanned=212 ready=14 applied=9 reached=18 replied=2 booked=1");

        Assert.Equal(new FunnelDay("2026-11-08", 212, 14, 9, 18, 2, 1), day);
    }

    [Fact]
    public void A_line_with_no_day_is_dropped_and_a_count_it_leaves_out_is_zero()
    {
        Assert.Null(InsightsFunnel.Parse("process:Funnel outcome=failed took_ms=40 started=2026-11-09T06:00:00Z scanned=3"));
        Assert.Null(InsightsFunnel.Parse(null));
        Assert.Equal(new FunnelDay("2026-11-08", 0, 0, 4, 0, 0, 0), InsightsFunnel.Parse("process:Funnel outcome=success detail=2026-11-08 applied=4"));
        // A count is matched whole: "unbooked" is not "booked".
        Assert.Equal(0, InsightsFunnel.Parse("process:Funnel detail=2026-11-08 unbooked=7")!.Booked);
    }
    // #endregion the-line

    // #region the-grade
    [Theory]
    [InlineData(0, CardTone.Red)]
    [InlineData(1, CardTone.Amber)]
    [InlineData(2, CardTone.Fine)]
    [InlineData(3, CardTone.Fine)]
    public void The_tone_is_the_weeks_calls_booked_against_two_to_three(long booked, CardTone tone)
    {
        Assert.Equal(tone, FunnelRules.Grade([Day("2026-11-15", booked)], newestAgeSeconds: 3_600));
    }

    [Fact]
    public void No_line_in_the_week_is_unread_and_a_line_three_days_old_with_its_calls_met_is_unread_too()
    {
        Assert.Equal(CardTone.Unread, FunnelRules.Grade([], null));
        Assert.Equal(CardTone.Unread, FunnelRules.Grade([Day("2026-11-12", 2)], newestAgeSeconds: 3 * 86_400 + 1));
        // A week short of its calls says so however old its newest line is.
        Assert.Equal(CardTone.Red, FunnelRules.Grade([Day("2026-11-12", 0)], newestAgeSeconds: 3 * 86_400 + 1));
    }

    [Fact]
    public void A_day_written_twice_counts_once_its_later_line()
    {
        var total = FunnelRules.Sum([Day("2026-11-14", 1, applied: 3), Day("2026-11-15", 0), Day("2026-11-14", 2, applied: 4)]);

        Assert.Equal((2L, 9L, "2026-11-14 to 2026-11-15"), (total.Booked, total.Applied, total.Day));
    }
    // #endregion the-grade

    // #region the-handler
    [Fact]
    public async Task The_window_adds_its_days_and_the_tone_reads_the_newest_seven()
    {
        var reader = new FakeReader([
            (Now.AddDays(-20), Day("2026-10-27", 3)),
            (Now.AddDays(-2), Day("2026-11-14", 1)),
            (Now.AddHours(-9), Day("2026-11-15", 0)),
        ]);
        var handler = new ReadFunnelHandler(reader, new FixedClock(Now));

        var day = await handler.HandleAsync(new ReadFunnelQuery("24h"), CancellationToken.None);
        var month = await handler.HandleAsync(new ReadFunnelQuery("30d"), CancellationToken.None);

        Assert.Equal((1, 0L, 1L, "amber"), (day.Days, day.Counts[5], day.WeekBooked, day.Tone));
        Assert.Equal((3, 4L, 1L, "amber"), (month.Days, month.Counts[5], month.WeekBooked, month.Tone));
        Assert.Equal(Now.AddHours(-9).ToUnixTimeMilliseconds(), month.NewestMs);
    }

    [Fact]
    public async Task A_reader_that_fails_is_the_cards_unread_with_its_reason_never_the_routes_500()
    {
        var handler = new ReadFunnelHandler(new FakeReader([], fails: true), new FixedClock(Now));

        var report = await handler.HandleAsync(new ReadFunnelQuery("7d"), CancellationToken.None);

        Assert.Equal((false, "unread"), (report.Available, report.Tone));
        Assert.Contains("could not be read (HttpRequestException)", report.Note, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_line_in_the_week_says_the_Function_has_not_written_one()
    {
        var report = await new ReadFunnelHandler(new FakeReader([]), new FixedClock(Now)).HandleAsync(new ReadFunnelQuery("7d"), CancellationToken.None);

        Assert.Equal("the Function has not written a funnel line in the last seven days", report.Note);
    }
    // #endregion the-handler

    private sealed class FakeReader(IReadOnlyList<(DateTimeOffset At, FunnelDay Day)> lines, bool fails = false) : IFunnelReader
    {
        public string Source => "the test's lines";

        public bool Available => true;

        public Task<IReadOnlyList<(DateTimeOffset At, FunnelDay Day)>> LinesAsync(TimeSpan window, CancellationToken cancellation) =>
            fails ? throw new HttpRequestException("refused") : Task.FromResult<IReadOnlyList<(DateTimeOffset At, FunnelDay Day)>>([.. lines.Where(line => line.At > Now - window)]);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
