using TheYard.Application;
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Tests;

/// <summary>
/// The simulated room (ADR: Competing bidders). Every test here is about a rule
/// the room must not break, because the room bidding wrongly would be an
/// auction with two implementations of its own rules, which is the thing the
/// whole domain layer exists to prevent.
/// </summary>
public class MarketServiceTests
{
    /// <summary>A clock whose "now" can be pushed forward without moving the anchor.</summary>
    private static AuctionClock At(DateTimeOffset now) => TestData.ClockAt(now);

    private static readonly DateTimeOffset Noon =
        new(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4));

    /// <summary>A vehicle whose auction is live under the clock the tests use.</summary>
    private static Vehicle LiveVehicle(string id = "live-1", int? currentBid = 22_800)
    {
        var clock = At(Noon);
        for (int i = 0; i < 500; i++)
        {
            string candidate = $"{id}-{i}";
            if (AuctionSchedule.StatusFor(candidate, clock) == AuctionStatus.Live)
            {
                return TestData.Vehicle(id: candidate, currentBid: currentBid);
            }
        }
        throw new InvalidOperationException("no live id found, which the schedule makes impossible");
    }

    private static Dictionary<string, BidState> NoBuyer() => [];

    // #region rules
    [Fact]
    public void The_room_bids_the_minimum_next_bid_and_nothing_more()
    {
        var vehicle = LiveVehicle(currentBid: 22_800);
        var market = new MarketService();

        var raised = market.Tick([vehicle], NoBuyer(), At(Noon));

        Assert.Equal([vehicle.Id], raised);
        // 22,800 is in the $20k tier, so the increment is $500.
        Assert.Equal(23_300, market.For(vehicle.Id)!.Amount);
    }

    [Fact]
    public void The_room_will_not_bid_on_an_auction_that_is_not_live()
    {
        var clock = At(Noon);
        string ended = Enumerable.Range(0, 500)
            .Select(i => $"ended-{i}")
            .First(id => AuctionSchedule.StatusFor(id, clock) != AuctionStatus.Live);
        var market = new MarketService();

        var raised = market.Tick([TestData.Vehicle(id: ended)], NoBuyer(), clock);

        Assert.Empty(raised);
        Assert.Null(market.For(ended));
    }

    [Fact]
    public void The_room_stops_at_twice_the_opening_ask()
    {
        var vehicle = LiveVehicle(currentBid: null);
        int ceiling = MarketService.CeilingFor(vehicle);
        var market = new MarketService();

        // The clock advances past the grace period between rounds, because the
        // room now waits before raising the same vehicle again whether or not
        // the buyer is in on it. A generous bound so a runaway loop fails as an
        // assertion rather than as a hung suite.
        var when = Noon;
        for (int i = 0; i < 5_000; i++)
        {
            when = when.AddSeconds(market.Grace.TotalSeconds + 1);
            if (market.Tick([vehicle], NoBuyer(), At(when)).Count == 0)
            {
                break;
            }
        }

        int last = market.For(vehicle.Id)!.Amount;
        Assert.True(last < ceiling + BidRules.Increment(last),
            $"the room reached {last} against a ceiling of {ceiling}");
        Assert.Empty(market.Tick([vehicle], NoBuyer(), At(when.AddHours(1))));
    }

    [Fact]
    public void The_room_never_takes_a_vehicle_out_from_under_the_visitor_at_buy_now()
    {
        // A bid at or above buy-now wins outright under BidRules. The room is
        // here to compete, not to end the auction, so it stops one step short.
        // At $1,000 the increment is $100, which lands exactly on buy-now.
        var vehicle = LiveVehicle(currentBid: 1_000) with { BuyNowPrice = 1_100 };
        var market = new MarketService();

        Assert.Empty(market.Tick([vehicle], NoBuyer(), At(Noon)));
    }
    // #endregion rules

    // #region outbidding
    [Fact]
    public void The_buyers_lead_is_left_alone_until_the_grace_period_passes()
    {
        var vehicle = LiveVehicle(currentBid: 22_800);
        var clock = At(Noon);
        var buyer = new Dictionary<string, BidState>
        {
            [vehicle.Id] = new BidState(23_300, 4, WonBuyNow: false, AtMs: clock.NowMs),
        };
        var market = new MarketService();

        Assert.Empty(market.Tick([vehicle], buyer, clock));

        var later = At(Noon.AddSeconds(market.Grace.TotalSeconds + 1));
        var raised = market.Tick([vehicle], buyer, later);

        Assert.Equal([vehicle.Id], raised);
        Assert.True(market.For(vehicle.Id)!.Amount > 23_300);
    }

    [Fact]
    public void A_vehicle_bought_outright_is_never_bid_on_again()
    {
        var vehicle = LiveVehicle(currentBid: 22_800);
        var clock = At(Noon.AddHours(1));
        var buyer = new Dictionary<string, BidState>
        {
            [vehicle.Id] = new BidState(30_000, 4, WonBuyNow: true, AtMs: At(Noon).NowMs),
        };
        var market = new MarketService();

        Assert.Empty(market.Tick([vehicle], buyer, clock));
    }

    [Fact]
    public void The_buyers_vehicles_are_answered_before_the_rest_of_the_room()
    {
        var contested = LiveVehicle("contested", currentBid: 22_800);
        var others = Enumerable.Range(0, 6)
            .Select(i => LiveVehicle($"other-{i}", currentBid: 22_800))
            .ToList();
        var clock = At(Noon.AddMinutes(5));
        var buyer = new Dictionary<string, BidState>
        {
            [contested.Id] = new BidState(23_300, 4, WonBuyNow: false, AtMs: At(Noon).NowMs),
        };
        var market = new MarketService();

        // One bid allowed, and the visitor's vehicle is the one that gets it.
        var raised = market.Tick([.. others, contested], buyer, clock, maxBids: 1);

        Assert.Equal([contested.Id], raised);
    }
    // #endregion outbidding

    [Fact]
    public void The_rooms_bid_only_shows_where_it_is_actually_higher()
    {
        var vehicle = LiveVehicle(currentBid: 22_800);
        var market = new MarketService();
        market.Tick([vehicle], NoBuyer(), At(Noon));

        // The buyer has since gone above the room: the overlay must not pull
        // the price back down.
        var buyerAhead = vehicle with { CurrentBid = 50_000, BidCount = 9 };
        Assert.Equal(50_000, market.Apply(buyerAhead).CurrentBid);
        // And where the room is ahead, it shows.
        Assert.Equal(23_300, market.Apply(vehicle).CurrentBid);
    }

    [Fact]
    public void An_uncontested_vehicle_is_not_raised_twice_inside_the_grace_period()
    {
        // The grace check used to sit inside the "does the buyer hold this?"
        // branch, so a vehicle nobody had bid on was raised on every single
        // tick. At eight seconds a round that doubled the top listing's price
        // in about two minutes while the rest of the grid never moved.
        var vehicle = LiveVehicle(currentBid: 22_800);
        var market = new MarketService();

        Assert.Single(market.Tick([vehicle], NoBuyer(), At(Noon)));
        Assert.Empty(market.Tick([vehicle], NoBuyer(), At(Noon.AddSeconds(1))));
        Assert.Empty(market.Tick([vehicle], NoBuyer(), At(Noon.AddSeconds(5))));
        Assert.Single(market.Tick([vehicle], NoBuyer(), At(Noon.AddSeconds(25))));
    }

    [Fact]
    public void Resetting_clears_the_room_with_the_buyer()
    {
        var vehicle = LiveVehicle(currentBid: 22_800);
        var market = new MarketService();
        market.Tick([vehicle], NoBuyer(), At(Noon));
        Assert.NotNull(market.For(vehicle.Id));

        market.Reset();

        Assert.Null(market.For(vehicle.Id));
        Assert.Equal(22_800, market.Apply(vehicle).CurrentBid);
    }
}
