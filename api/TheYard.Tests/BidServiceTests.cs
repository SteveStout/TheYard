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
    public async Task A_bid_is_measured_against_the_composed_price_not_the_buyers_own()
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
        Assert.Equal(BidOutcomeKind.Accepted, (await bids.PlaceBidAsync(vehicle, 23_300, clock, Buyer)).Kind);

        // The room answers twice, so it stands at 24,300.
        var buyer = bids.SnapshotFor(Buyer);
        var later = TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 1, 0, TimeSpan.FromHours(-4)));
        market.Tick([vehicle], buyer, later);
        var laterStill = TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 2, 0, TimeSpan.FromHours(-4)));
        market.Tick([vehicle], buyer, laterStill);
        Assert.Equal(24_300, market.For(vehicle.Id)!.Amount);

        // 23,800 is a raise on the buyer's own bid and $500 under the room.
        var rejected = await bids.PlaceBidAsync(market.Apply(vehicle), 23_800, laterStill, Buyer);

        Assert.Equal(BidOutcomeKind.Rejected, rejected.Kind);
        Assert.Contains("24,800", rejected.Reason);
        // And the price the page advertises is the price it enforces.
        Assert.Equal(24_800, BidRules.MinNextBid(market.Apply(bids.Apply(vehicle))));
    }

    [Fact]
    public async Task Retaking_the_lead_never_lowers_the_bid_count()
    {
        // The count used to come from the buyer's own state, which is behind
        // the room's, so a vehicle went from seven bids to six on being bid on.
        var clock = TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4)));
        var vehicle = LiveVehicleFor(clock, currentBid: 22_800);
        var bids = new BidService();
        var market = new MarketService();

        await bids.PlaceBidAsync(vehicle, 23_300, clock, Buyer);
        int afterMine = market.Apply(bids.Apply(vehicle)).BidCount;

        var later = TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 1, 0, TimeSpan.FromHours(-4)));
        market.Tick([vehicle], bids.SnapshotFor(Buyer), later);
        int afterTheirs = market.Apply(bids.Apply(vehicle)).BidCount;
        Assert.True(afterTheirs > afterMine);

        var composed = market.Apply(bids.Apply(vehicle));
        await bids.PlaceBidAsync(composed, BidRules.MinNextBid(composed), later, Buyer);

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
    public async Task An_accepted_bid_updates_the_overlay_and_the_next_minimum()
    {
        var service = new BidService();
        var vehicle = TestData.Vehicle(id: LiveId(), currentBid: 22_800); // bid_count 16

        var outcome = await service.PlaceBidAsync(vehicle, 23_300, Now, Buyer);

        Assert.Equal(BidOutcomeKind.Accepted, outcome.Kind);
        var merged = service.Apply(vehicle);
        Assert.Equal(23_300, merged.CurrentBid);
        Assert.Equal(17, merged.BidCount);
        // The next bid must clear the new high bid, not the old one.
        Assert.Equal(BidOutcomeKind.Rejected, (await service.PlaceBidAsync(vehicle, 23_400, Now, Buyer)).Kind);
        Assert.Equal(BidOutcomeKind.Accepted, (await service.PlaceBidAsync(vehicle, 23_800, Now, Buyer)).Kind);
    }

    [Fact]
    public async Task Buy_now_marks_the_vehicle_won_without_adding_a_bid()
    {
        var service = new BidService();
        var vehicle = TestData.Vehicle(id: LiveId()) with { BuyNowPrice = 28_000 };

        var outcome = await service.BuyNowAsync(vehicle, Now, Buyer);

        Assert.Equal(BidOutcomeKind.Won, outcome.Kind);
        var state = service.SnapshotFor(Buyer)[vehicle.Id];
        Assert.True(state.WonBuyNow);
        Assert.Equal(vehicle.BidCount, state.BidCount);
        Assert.Equal(28_000, state.Amount);
    }

    // #region sold
    /// <summary>
    /// The sale is everybody's. Before 1.0.0.110 the second account's bid at
    /// the buy-now price was resolved by the same shortcut that sold it the
    /// first time, so one vehicle had two buyers and each was told it was
    /// theirs; the standing already knew it was sold and nobody asked it (ADR:
    /// Accounts and per-user bids, the addendum on the second buyer).
    /// </summary>
    [Fact]
    public async Task Buy_now_ends_the_auction_for_everybody_else()
    {
        var service = new BidService();
        var vehicle = TestData.Vehicle(id: LiveId(), currentBid: 22_800) with { BuyNowPrice = 28_000 };
        const string Stranger = "second-account";

        Assert.False(service.IsSold(vehicle.Id));
        Assert.Equal(BidOutcomeKind.Won, (await service.BuyNowAsync(vehicle, Now, Buyer)).Kind);
        Assert.True(service.IsSold(vehicle.Id));

        // The three doors a second buyer could try: a bid at the buy-now price,
        // which used to win; a bid above the minimum, which used to be accepted;
        // and Buy Now itself.
        var atBuyNow = await service.PlaceBidAsync(vehicle, 28_000, Now, Stranger);
        var overMinimum = await service.PlaceBidAsync(vehicle, 28_500, Now, Stranger);
        var again = await service.BuyNowAsync(vehicle, Now, Stranger);
        foreach (var outcome in new[] { atBuyNow, overMinimum, again })
        {
            Assert.Equal(BidOutcomeKind.Rejected, outcome.Kind);
            Assert.Equal(BidRules.SoldReason, outcome.Reason);
        }

        // Nothing of the stranger's was recorded, and the buyer still holds it.
        Assert.Empty(service.SnapshotFor(Stranger));
        Assert.True(service.SnapshotFor(Buyer)[vehicle.Id].WonBuyNow);
        Assert.Equal(Buyer, service.Standing()[vehicle.Id].HighBidderId);
    }

    /// <summary>
    /// A bid at the buy-now price is a purchase too (the rule in BidRules), so
    /// it sells the vehicle the same way, and the buyer's own second bid is
    /// refused like anybody else's: sold is sold.
    /// </summary>
    [Fact]
    public async Task A_bid_at_the_buy_now_price_sells_it_the_same_way()
    {
        var service = new BidService();
        var vehicle = TestData.Vehicle(id: LiveId(), currentBid: 27_900) with { BuyNowPrice = 28_000 };

        Assert.Equal(BidOutcomeKind.Won, (await service.PlaceBidAsync(vehicle, 30_000, Now, Buyer)).Kind);

        Assert.True(service.IsSold(vehicle.Id));
        Assert.Equal(BidRules.SoldReason, (await service.BuyNowAsync(vehicle, Now, "second-account")).Reason);
        Assert.Equal(BidRules.SoldReason, (await service.PlaceBidAsync(vehicle, 29_000, Now, Buyer)).Reason);
    }

    /// <summary>
    /// A sale replayed from the store is a sale: the process that restarts
    /// learns the vehicle is sold from the same row it learns the price from.
    /// </summary>
    [Fact]
    public async Task A_sale_replayed_from_the_store_still_refuses_the_next_buyer()
    {
        var vehicle = TestData.Vehicle(id: LiveId(), currentBid: 22_800) with { BuyNowPrice = 28_000 };
        var sale = new StoredBid(Buyer, vehicle.Id, new BidState(28_000, 16, WonBuyNow: true, AtMs: 1));
        var store = new FlakyStore(sale);
        var service = new BidService(store);
        // The first replay fails by design of the fake; the next caller retries it.
        await Assert.ThrowsAsync<IOException>(() => service.LoadAsync());

        var outcome = await service.BuyNowAsync(vehicle, Now, "second-account");

        Assert.Equal(BidRules.SoldReason, outcome.Reason);
        Assert.True(service.IsSold(vehicle.Id));
    }
    // #endregion sold

    // #region reset
    [Fact]
    public async Task Reset_clears_the_callers_bids_and_names_the_vehicles_it_touched()
    {
        var service = new BidService();
        var vehicle = TestData.Vehicle(id: LiveId(), currentBid: 22_800);
        await service.PlaceBidAsync(vehicle, 23_300, Now, Buyer);

        var touched = await service.ResetAsync(Buyer);

        Assert.Empty(service.SnapshotFor(Buyer));
        Assert.Equal(22_800, service.Apply(vehicle).CurrentBid);
        // Nobody is bidding on it any more, so the room's answer to it goes too.
        Assert.Equal([vehicle.Id], touched);
    }

    [Fact]
    public async Task Reset_leaves_a_stranger_bidding_on_the_same_vehicle_alone()
    {
        // This is the whole point of the change. Before it, this endpoint took
        // no user at all, so either of two visitors could delete the other's
        // bids, on a site whose changelog says they can outbid each other and
        // both be told the truth.
        var service = new BidService();
        var vehicle = TestData.Vehicle(id: LiveId(), currentBid: 22_800);
        await service.PlaceBidAsync(vehicle, 23_300, Now, Buyer);
        await service.PlaceBidAsync(vehicle, 24_000, Now, "somebody-else");

        var orphaned = await service.ResetAsync(Buyer);

        Assert.Empty(service.SnapshotFor(Buyer));
        Assert.Single(service.SnapshotFor("somebody-else"));
        // And the stranger keeps the lead they earned: the standing is
        // recomputed from who is left rather than deleted with the caller.
        var merged = service.Apply(vehicle);
        Assert.Equal(24_000, merged.CurrentBid);
        // And the room is not told to forget this one. It is still somebody's
        // auction, and the room's answer is what that somebody is bidding
        // against: clearing it would take away their outbid badge and drop the
        // price, which is the same defect one size smaller again.
        Assert.Empty(orphaned);
    }

    /// <summary>
    /// A reset the store refuses leaves everything standing, in memory as in
    /// the store, so the caller is told the truth and the next start does not
    /// bring back bids the page had said were gone (ADR: Three readers with no
    /// memory of the project). Before 1.0.0.111 the dictionaries were cleared
    /// first and this test's snapshot came back empty.
    /// </summary>
    [Fact]
    public async Task A_reset_the_store_refuses_leaves_the_bids_standing()
    {
        var store = new RefusingResetStore();
        var service = new BidService(store);
        var vehicle = TestData.Vehicle(id: LiveId(), currentBid: 22_800);
        Assert.Equal(BidOutcomeKind.Accepted, (await service.PlaceBidAsync(vehicle, 23_300, Now, Buyer)).Kind);

        await Assert.ThrowsAsync<IOException>(() => service.ResetAsync(Buyer));

        Assert.Equal(23_300, service.SnapshotFor(Buyer)[vehicle.Id].Amount);
        Assert.Equal(23_300, service.Apply(vehicle).CurrentBid);
        Assert.Equal(1, store.Clears);
    }

    private sealed class RefusingResetStore : IBidStore
    {
        public int Clears { get; private set; }

        public Task<IReadOnlyList<StoredBid>> LoadAsync() => Task.FromResult<IReadOnlyList<StoredBid>>([]);

        public Task SaveAsync(string userId, string vehicleId, BidState state) => Task.CompletedTask;

        public Task ClearAsync(string userId)
        {
            Clears++;
            return Task.FromException(new IOException("the store is not answering"));
        }
    }

    [Fact]
    public async Task Reset_by_somebody_who_has_not_bid_touches_nothing()
    {
        var service = new BidService();
        var vehicle = TestData.Vehicle(id: LiveId(), currentBid: 22_800);
        await service.PlaceBidAsync(vehicle, 23_300, Now, Buyer);

        Assert.Empty(await service.ResetAsync("a-stranger"));
        Assert.Single(service.SnapshotFor(Buyer));
        Assert.Equal(23_300, service.Apply(vehicle).CurrentBid);
    }
    // #endregion reset

    [Fact]
    public async Task Rejected_bids_leave_no_state_behind()
    {
        var service = new BidService();
        var vehicle = TestData.Vehicle(id: LiveId(), currentBid: 22_800);

        Assert.Equal(BidOutcomeKind.Rejected, (await service.PlaceBidAsync(vehicle, 100, Now, Buyer)).Kind);
        Assert.Empty(service.SnapshotFor(Buyer));
    }

    // #region replay-retry
    /// <summary>
    /// A store that is down for the replay at startup and up afterwards: the
    /// next caller replays it, so the standing arrives the first time the store
    /// answers rather than never (ADR: The ports learn to wait, addendum). The
    /// Lazy this replaced kept the first failure for the life of the process.
    /// </summary>
    [Fact]
    public async Task A_replay_that_failed_is_tried_again_by_the_next_caller()
    {
        var store = new FlakyStore(new StoredBid(Buyer, "v-held", new BidState(23_300, 1, false, 1)));
        var service = new BidService(store);

        await Assert.ThrowsAsync<IOException>(() => service.LoadAsync());
        Assert.Empty(service.SnapshotFor(Buyer));

        await service.LoadAsync();
        Assert.Single(service.SnapshotFor(Buyer));
        Assert.Equal(2, store.LoadCalls);
    }

    private sealed class FlakyStore(params StoredBid[] bids) : IBidStore
    {
        public int LoadCalls { get; private set; }

        public Task<IReadOnlyList<StoredBid>> LoadAsync()
        {
            LoadCalls++;
            return LoadCalls == 1
                ? Task.FromException<IReadOnlyList<StoredBid>>(new IOException("the store is not answering"))
                : Task.FromResult<IReadOnlyList<StoredBid>>(bids);
        }

        public Task SaveAsync(string userId, string vehicleId, BidState state) => Task.CompletedTask;

        public Task ClearAsync(string userId) => Task.CompletedTask;
    }
    // #endregion replay-retry
}
