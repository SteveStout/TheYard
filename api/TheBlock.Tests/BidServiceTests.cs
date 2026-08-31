using TheBlock.Application;
using TheBlock.Domain;

namespace TheBlock.Tests;

public class BidServiceTests
{
    private static readonly AuctionClock Now =
        TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4)));

    private static string LiveId()
    {
        for (int i = 0; i < 1000; i++)
        {
            if (AuctionSchedule.StatusFor($"probe-{i}", Now) == AuctionStatus.Live)
            {
                return $"probe-{i}";
            }
        }
        throw new InvalidOperationException("no live probe id");
    }

    [Fact]
    public void An_accepted_bid_updates_the_overlay_and_the_next_minimum()
    {
        var service = new BidService();
        var vehicle = TestData.Vehicle(id: LiveId(), currentBid: 22_800); // bid_count 16

        var outcome = service.PlaceBid(vehicle, 23_300, Now);

        Assert.Equal(BidOutcomeKind.Accepted, outcome.Kind);
        var merged = service.Apply(vehicle);
        Assert.Equal(23_300, merged.CurrentBid);
        Assert.Equal(17, merged.BidCount);
        // The next bid must clear the new high bid, not the old one.
        Assert.Equal(BidOutcomeKind.Rejected, service.PlaceBid(vehicle, 23_400, Now).Kind);
        Assert.Equal(BidOutcomeKind.Accepted, service.PlaceBid(vehicle, 23_800, Now).Kind);
    }

    [Fact]
    public void Buy_now_marks_the_vehicle_won_without_adding_a_bid()
    {
        var service = new BidService();
        var vehicle = TestData.Vehicle(id: LiveId()) with { BuyNowPrice = 28_000 };

        var outcome = service.BuyNow(vehicle, Now);

        Assert.Equal(BidOutcomeKind.Won, outcome.Kind);
        var state = service.Snapshot()[vehicle.Id];
        Assert.True(state.WonBuyNow);
        Assert.Equal(vehicle.BidCount, state.BidCount);
        Assert.Equal(28_000, state.Amount);
    }

    [Fact]
    public void Reset_clears_every_bid()
    {
        var service = new BidService();
        var vehicle = TestData.Vehicle(id: LiveId(), currentBid: 22_800);
        service.PlaceBid(vehicle, 23_300, Now);

        service.Reset();

        Assert.Empty(service.Snapshot());
        Assert.Equal(22_800, service.Apply(vehicle).CurrentBid);
    }

    [Fact]
    public void Rejected_bids_leave_no_state_behind()
    {
        var service = new BidService();
        var vehicle = TestData.Vehicle(id: LiveId(), currentBid: 22_800);

        Assert.Equal(BidOutcomeKind.Rejected, service.PlaceBid(vehicle, 100, Now).Kind);
        Assert.Empty(service.Snapshot());
    }
}
