using TheYard.Domain;

namespace TheYard.Tests;

public class AuctionScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4));
    private static readonly AuctionClock Clock = TestData.ClockAt(Now);
    private const long DayMs = 24L * 60 * 60 * 1000;

    [Fact]
    public void Windows_are_stable_for_a_fixed_anchor()
    {
        Assert.Equal(
            AuctionSchedule.Window("some-id", Clock.AnchorMs),
            AuctionSchedule.Window("some-id", Clock.AnchorMs));
    }

    [Fact]
    public void End_times_spread_two_days_back_to_five_days_ahead_with_2_to_4_day_runs()
    {
        for (int i = 0; i < 200; i++)
        {
            var window = AuctionSchedule.Window($"probe-{i}", Clock.AnchorMs);
            Assert.InRange(window.EndsAtMs, Clock.AnchorMs - 2 * DayMs, Clock.AnchorMs + 5 * DayMs);
            Assert.InRange(window.EndsAtMs - window.StartsAtMs, 2 * DayMs, 4 * DayMs);
        }
    }

    [Fact]
    public void Produces_a_mix_of_upcoming_live_and_ended()
    {
        var statuses = Enumerable.Range(0, 200)
            .Select(i => AuctionSchedule.StatusFor($"probe-{i}", Clock))
            .ToHashSet();

        Assert.Contains(AuctionStatus.Upcoming, statuses);
        Assert.Contains(AuctionStatus.Live, statuses);
        Assert.Contains(AuctionStatus.Ended, statuses);
    }

    [Fact]
    public void Status_boundaries_are_start_inclusive_and_end_exclusive()
    {
        var window = AuctionSchedule.Window("some-id", Clock.AnchorMs);

        Assert.Equal(AuctionStatus.Upcoming, AuctionSchedule.Status(window, window.StartsAtMs - 1));
        Assert.Equal(AuctionStatus.Live, AuctionSchedule.Status(window, window.StartsAtMs));
        Assert.Equal(AuctionStatus.Live, AuctionSchedule.Status(window, window.EndsAtMs - 1));
        Assert.Equal(AuctionStatus.Ended, AuctionSchedule.Status(window, window.EndsAtMs));
    }

    // #region one-clock
    /// <summary>
    /// One clock for everybody: a moment's anchor is the UTC midnight that
    /// began its day, whatever zone the moment is written in, and two moments
    /// on the same UTC day share it (ADR: Three readers with no memory of the
    /// project, the addendum on the clock). Until 1.0.0.112 the host fell
    /// back to its own zone's midnight and otherwise took the caller's.
    /// </summary>
    [Fact]
    public void The_clock_anchors_to_the_utc_midnight_of_its_day_whatever_zone_it_is_written_in()
    {
        var noonInToronto = new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4));
        var lateInLondon = new DateTimeOffset(2026, 8, 15, 23, 30, 0, TimeSpan.FromHours(1));
        var utcMidnight = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        var toronto = AuctionClock.Utc(noonInToronto);
        var london = AuctionClock.Utc(lateInLondon);

        Assert.Equal(noonInToronto.ToUnixTimeMilliseconds(), toronto.NowMs);
        Assert.Equal(utcMidnight, toronto.AnchorMs);
        Assert.Equal(utcMidnight, london.AnchorMs);
        Assert.Equal(
            AuctionSchedule.Window("some-id", toronto.AnchorMs),
            AuctionSchedule.Window("some-id", london.AnchorMs));

        // 20:00 in Toronto on the 15th is already the 16th in UTC: the next day's auction.
        var eveningInToronto = new DateTimeOffset(2026, 8, 15, 20, 0, 0, TimeSpan.FromHours(-4));
        Assert.Equal(utcMidnight + DayMs, AuctionClock.Utc(eveningInToronto).AnchorMs);
    }
    // #endregion one-clock
}
