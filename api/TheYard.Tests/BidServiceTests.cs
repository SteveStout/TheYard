using TheYard.Application;
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Tests;

public class BidServiceTests
{
    /// <summary>
    /// A bid belongs to an account (ADR: Accounts and per-user bids). These
    /// tests are about the bidding rules rather than about who holds them, so
    /// they all bid as the same person and say so once.
    /// </summary>
    private const string Buyer = "buyer-under-test";

    // #region composed
    [Fact]
    public void A_bid_is_measured_against_the_composed_price_not_the_buyers_own()
    {
        // The bug this catches: BidService.Apply overwrote CurrentBid instead
        // of taking the max, so when the endpoint handed it a vehicle the room
        // had already raised, the buyer's own older bid was written back over
        // the room's higher one and the minimum next bid was computed against
        // the wrong number. The buyer could then sit permanently one increment
        // under the room and be accepted every time (ADR-027, self review).
        var clock = TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4)));
        var vehicle = LiveVehicleFor(clock, currentBid: 22_800);
        var bids = new BidService();
        var market = new MarketService();

        // The buyer takes the lead at the minimum, 22,800 + 500.
        Assert.Equal(BidOutcomeKind.Accepted, bids.PlaceBid(vehicle, 23_300, clock, Buyer).Kind);

        // The room answers twice, so it stands at 24,300.
        var buyer = bids.SnapshotFor(Buyer);
        var later = TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 1, 0, TimeSpan.FromHours(-4)));
        market.Tick([vehicle], buyer, later);
        var laterStill = TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 2, 0, TimeSpan.FromHours(-4)));
        market.Tick([vehicle], buyer, laterStill);
        Assert.Equal(24_300, market.For(vehicle.Id)!.Amount);

        // 23,800 is a raise on the buyer's own bid and $500 under the room.
        var rejected = bids.PlaceBid(market.Apply(vehicle), 23_800, laterStill, Buyer);

        Assert.Equal(BidOutcomeKind.Rejected, rejected.Kind);
        Assert.Contains("24,800", rejected.Reason);
        // And the price the page advertises is the price it enforces.
        Assert.Equal(24_800, BidRules.MinNextBid(market.Apply(bids.Apply(vehicle))));
    }

    [Fact]
    public void Retaking_the_lead_never_lowers_the_bid_count()
    {
        // The count used to come from the buyer's own state, which is behind
        // the room's, so a vehicle went from seven bids to six on being bid on.
        var clock = TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4)));
        var vehicle = LiveVehicleFor(clock, currentBid: 22_800);
        var bids = new BidService();
        var market = new MarketService();

        bids.PlaceBid(vehicle, 23_300, clock, Buyer);
        int afterMine = market.Apply(bids.Apply(vehicle)).BidCount;

        var later = TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 1, 0, TimeSpan.FromHours(-4)));
        market.Tick([vehicle], bids.SnapshotFor(Buyer), later);
        int afterTheirs = market.Apply(bids.Apply(vehicle)).BidCount;
        Assert.True(afterTheirs > afterMine);

        var composed = market.Apply(bids.Apply(vehicle));
        bids.PlaceBid(composed, BidRules.MinNextBid(composed), later, Buyer);

        Assert.True(market.Apply(bids.Apply(vehicle)).BidCount >= afterTheirs,
            "the bid count fell when the buyer retook the lead");
    }

    /// <summary>A vehicle whose auction is live under the supplied clock.</summary>
    private static Vehicle LiveVehicleFor(AuctionClock clock, int? currentBid)
    {
        for (int i = 0; i < 500; i++)
        {
            string candidate = $"composed-{i}";
            if (AuctionSchedule.StatusFor(candidate, clock) == AuctionStatus.Live)
            {
                return TestData.Vehicle(id: candidate, currentBid: currentBid);
            }
        }
        throw new InvalidOperationException("no live id found, which the schedule makes impossible");
    }
    // #endregion composed

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

        var outcome = service.PlaceBid(vehicle, 23_300, Now, Buyer);

        Assert.Equal(BidOutcomeKind.Accepted, outcome.Kind);
        var merged = service.Apply(vehicle);
        Assert.Equal(23_300, merged.CurrentBid);
        Assert.Equal(17, merged.BidCount);
        // The next bid must clear the new high bid, not the old one.
        Assert.Equal(BidOutcomeKind.Rejected, service.PlaceBid(vehicle, 23_400, Now, Buyer).Kind);
        Assert.Equal(BidOutcomeKind.Accepted, service.PlaceBid(vehicle, 23_800, Now, Buyer).Kind);
    }

    [Fact]
    public void Buy_now_marks_the_vehicle_won_without_adding_a_bid()
    {
        var service = new BidService();
        var vehicle = TestData.Vehicle(id: LiveId()) with { BuyNowPrice = 28_000 };

        var outcome = service.BuyNow(vehicle, Now, Buyer);

        Assert.Equal(BidOutcomeKind.Won, outcome.Kind);
        var state = service.SnapshotFor(Buyer)[vehicle.Id];
        Assert.True(state.WonBuyNow);
        Assert.Equal(vehicle.BidCount, state.BidCount);
        Assert.Equal(28_000, state.Amount);
    }

    [Fact]
    public void Reset_clears_every_bid()
    {
        var service = new BidService();
        var vehicle = TestData.Vehicle(id: LiveId(), currentBid: 22_800);
        service.PlaceBid(vehicle, 23_300, Now, Buyer);

        service.Reset();

        Assert.Empty(service.SnapshotFor(Buyer));
        Assert.Equal(22_800, service.Apply(vehicle).CurrentBid);
    }

    [Fact]
    public void Rejected_bids_leave_no_state_behind()
    {
        var service = new BidService();
        var vehicle = TestData.Vehicle(id: LiveId(), currentBid: 22_800);

        Assert.Equal(BidOutcomeKind.Rejected, service.PlaceBid(vehicle, 100, Now, Buyer).Kind);
        Assert.Empty(service.SnapshotFor(Buyer));
    }
}
