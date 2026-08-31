using TheBlock.Domain;

namespace TheBlock.Tests;

public class VehicleOrderingTests
{
    private static readonly AuctionClock Clock =
        TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4)));

    [Fact]
    public void Price_sorts_use_the_competing_price()
    {
        var vehicles = new[]
        {
            TestData.Vehicle(id: "high", currentBid: 20_000),
            TestData.Vehicle(id: "unbid", currentBid: null), // starting bid 14,500
        };

        Assert.Equal(
            ["unbid", "high"],
            VehicleOrdering.Sort(vehicles, VehicleSort.PriceAsc, Clock).Select(v => v.Id));
        Assert.Equal(
            ["high", "unbid"],
            VehicleOrdering.Sort(vehicles, VehicleSort.PriceDesc, Clock).Select(v => v.Id));
    }

    [Fact]
    public void Ending_soonest_puts_live_before_upcoming_before_ended()
    {
        var vehicles = Enumerable.Range(0, 60).Select(i => TestData.Vehicle(id: $"probe-{i}")).ToList();

        var statuses = VehicleOrdering.Sort(vehicles, VehicleSort.EndingSoonest, Clock)
            .Select(v => AuctionSchedule.StatusFor(v.Id, Clock))
            .ToList();

        int lastLive = statuses.LastIndexOf(AuctionStatus.Live);
        int firstUpcoming = statuses.IndexOf(AuctionStatus.Upcoming);
        int lastUpcoming = statuses.LastIndexOf(AuctionStatus.Upcoming);
        int firstEnded = statuses.IndexOf(AuctionStatus.Ended);

        Assert.Equal(AuctionStatus.Live, statuses[0]);
        Assert.True(firstUpcoming > lastLive, "upcoming must follow all live");
        Assert.True(firstEnded > lastUpcoming, "ended must follow all upcoming");
    }

    [Fact]
    public void Ending_soonest_orders_live_auctions_by_closest_end()
    {
        var vehicles = Enumerable.Range(0, 60).Select(i => TestData.Vehicle(id: $"probe-{i}")).ToList();

        var liveEnds = VehicleOrdering.Sort(vehicles, VehicleSort.EndingSoonest, Clock)
            .Where(v => AuctionSchedule.StatusFor(v.Id, Clock) == AuctionStatus.Live)
            .Select(v => AuctionSchedule.Window(v.Id, Clock.AnchorMs).EndsAtMs)
            .ToList();

        Assert.Equal(liveEnds.OrderBy(e => e), liveEnds);
    }
}
