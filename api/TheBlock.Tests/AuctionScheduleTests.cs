using TheBlock.Domain;

namespace TheBlock.Tests;

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

    [Fact]
    public void Server_local_clock_anchors_to_that_zones_midnight()
    {
        var utcNow = new DateTimeOffset(2026, 8, 15, 16, 0, 0, TimeSpan.Zero);
        var toronto = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        var clock = AuctionClock.ServerLocal(utcNow, toronto);

        // 2026-08-15 16:00Z is 12:00 in Toronto (UTC-4, DST); midnight local is 04:00Z.
        Assert.Equal(utcNow.ToUnixTimeMilliseconds(), clock.NowMs);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.FromHours(-4)).ToUnixTimeMilliseconds(),
            clock.AnchorMs);
    }
}
