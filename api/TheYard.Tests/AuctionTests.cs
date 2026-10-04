using TheYard.Application;
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Tests;

/// <summary>
/// The Application ring's Auction: the one place the catalogue, everybody's bids and the room
/// are composed (ADR: Onion and SOLID, how this codebase holds them). Each test holds one rule an
/// endpoint must never decide for itself: the price a vehicle shows, the price a bid is measured
/// against, what a reset takes with it, and what a buyer's badge says.
/// </summary>
public sealed class AuctionTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4));

    private static AuctionClock At(DateTimeOffset now) => TestData.ClockAt(now);

    /// <summary>A vehicle whose auction is live at noon on the test clock, with no buy-now price.</summary>
    private static Vehicle Live(string prefix, int currentBid = 22_800)
    {
        var clock = At(Noon);
        string id = Enumerable.Range(0, 500).Select(i => $"{prefix}-{i}").First(candidate => AuctionSchedule.StatusFor(candidate, clock) == AuctionStatus.Live);
        return TestData.Vehicle(id: id, currentBid: currentBid);
    }

    private static (Auction Auction, MarketService Market) Build(params Vehicle[] vehicles)
    {
        var market = new MarketService();
        var inventory = new InventoryService(new Vehicles(vehicles), new NoPhotos());
        return (new Auction(inventory, new BidService(), market), market);
    }

    // #region overlays
    [Fact]
    public void A_vehicle_with_no_bids_anywhere_stands_at_the_dataset_price()
    {
        var vehicle = Live("a");
        var (auction, _) = Build(vehicle);

        var found = auction.Find(vehicle.Id)!;

        Assert.Equal(22_800, found.Vehicle.CurrentBid);
        Assert.False(found.Sold);
    }

    [Fact]
    public void The_shown_price_is_the_room_when_the_room_is_highest()
    {
        var vehicle = Live("a");
        var (auction, market) = Build(vehicle);
        market.Tick([vehicle], new Dictionary<string, BidState>(), At(Noon));

        Assert.Equal(23_300, auction.Find(vehicle.Id)!.Vehicle.CurrentBid);
        Assert.Equal(23_300, auction.Search(new VehicleFilter(), At(Noon), VehicleSort.EndingSoonest, 10, 0).Vehicles.Single().Vehicle.CurrentBid);
    }
    // #endregion overlays

    [Fact]
    public async Task A_bid_is_measured_against_the_room_and_not_the_dataset()
    {
        var vehicle = Live("a");
        var (auction, market) = Build(vehicle);
        market.Tick([vehicle], new Dictionary<string, BidState>(), At(Noon));

        // 23,300 is the room's standing bid, so the dataset's 22,800 plus one increment is too low.
        var refused = await auction.PlaceBidAsync(auction.Find(vehicle.Id)!, 23_300, At(Noon.AddSeconds(1)), "buyer");
        var accepted = await auction.PlaceBidAsync(auction.Find(vehicle.Id)!, 23_800, At(Noon.AddSeconds(2)), "buyer");

        Assert.Equal(BidOutcomeKind.Rejected, refused.Kind);
        Assert.Equal(BidOutcomeKind.Accepted, accepted.Kind);
    }

    [Fact]
    public async Task A_bought_vehicle_comes_back_sold_to_everybody_from_find_and_search()
    {
        var bought = Live("b") with { BuyNowPrice = 40_000 };
        var (auction, _) = Build(bought);

        await auction.BuyNowAsync(auction.Find(bought.Id)!, At(Noon), "buyer");

        Assert.True(auction.Find(bought.Id)!.Sold);
        Assert.True(auction.Search(new VehicleFilter(), At(Noon), VehicleSort.EndingSoonest, 10, 0).Vehicles.Single().Sold);
    }

    [Fact]
    public async Task A_badge_says_outbid_when_the_room_goes_higher_and_never_after_a_buy_now()
    {
        var contested = Live("a");
        var bought = Live("b") with { BuyNowPrice = 40_000 };
        var (auction, market) = Build(contested, bought);
        await auction.PlaceBidAsync(auction.Find(contested.Id)!, 23_300, At(Noon), "buyer");
        await auction.BuyNowAsync(auction.Find(bought.Id)!, At(Noon), "buyer");

        // The room answers once the grace period has passed, and never on a vehicle somebody bought.
        auction.RoomRound(At(Noon.AddSeconds(30)));
        var badges = auction.BidsOf("buyer");

        Assert.True(badges[contested.Id].Outbid);
        Assert.Equal(market.For(contested.Id)!.Amount, badges[contested.Id].MarketAmount);
        Assert.Null(market.For(bought.Id));
        Assert.False(badges[bought.Id].Outbid);
        Assert.True(badges[bought.Id].WonBuyNow);
    }

    [Fact]
    public async Task The_history_is_newest_first_and_names_each_vehicle()
    {
        var first = Live("a");
        var second = Live("b");
        var (auction, _) = Build(first, second);
        await auction.PlaceBidAsync(auction.Find(first.Id)!, 23_300, At(Noon), "buyer");
        await auction.PlaceBidAsync(auction.Find(second.Id)!, 23_300, At(Noon.AddSeconds(5)), "buyer");

        var history = auction.HistoryOf("buyer");

        Assert.Equal(2, history.Count);
        Assert.Equal([second.Id, first.Id], history.Bids.Select(entry => entry.VehicleId));
        Assert.Equal($"{first.Year} {first.Make} {first.Model}", history.Bids[1].Title);
    }

    [Fact]
    public async Task A_reset_takes_the_room_away_only_where_nobody_else_is_bidding()
    {
        var mine = Live("a");
        var shared = Live("b");
        var (auction, market) = Build(mine, shared);
        await auction.PlaceBidAsync(auction.Find(mine.Id)!, 23_300, At(Noon), "me");
        await auction.PlaceBidAsync(auction.Find(shared.Id)!, 23_300, At(Noon), "me");
        await auction.PlaceBidAsync(auction.Find(shared.Id)!, 23_800, At(Noon.AddSeconds(1)), "someone else");
        auction.RoomRound(At(Noon.AddSeconds(30)));

        await auction.ResetAsync("me");

        Assert.Null(market.For(mine.Id));
        Assert.NotNull(market.For(shared.Id));
        Assert.Empty(auction.BidsOf("me"));
    }

    [Fact]
    public void The_room_round_bids_on_live_auctions_even_when_nobody_has_bid()
    {
        var vehicle = Live("a");
        var (auction, market) = Build(vehicle);

        var raised = auction.RoomRound(At(Noon));

        Assert.Equal([vehicle.Id], raised);
        Assert.NotNull(market.For(vehicle.Id));
    }
}

/// <summary>A catalogue held in memory, for the tests above.</summary>
file sealed class Vehicles(params Vehicle[] vehicles) : IVehicleSource
{
    public Task<IReadOnlyList<Vehicle>> LoadAsync() => Task.FromResult<IReadOnlyList<Vehicle>>(vehicles);
}

/// <summary>An empty photo manifest, so the gallery leaves each vehicle's own images alone.</summary>
file sealed class NoPhotos : IPhotoManifestSource
{
    public Task<IReadOnlyList<PhotoEntry>> LoadAsync() => Task.FromResult<IReadOnlyList<PhotoEntry>>([]);
}
