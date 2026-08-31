using TheBlock.Domain;

namespace TheBlock.Tests;

public class BidRulesTests
{
    private static readonly AuctionClock Now =
        TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4)));

    /// <summary>Finds a probe id whose derived status matches, so timing is controllable.</summary>
    private static string IdWithStatus(AuctionStatus status)
    {
        for (int i = 0; i < 1000; i++)
        {
            string id = $"probe-{i}";
            if (AuctionSchedule.StatusFor(id, Now) == status)
            {
                return id;
            }
        }
        throw new InvalidOperationException($"No probe id found with status {status}");
    }

    [Theory]
    [InlineData(3500, 100)]
    [InlineData(4999, 100)]
    [InlineData(5000, 250)]
    [InlineData(19999, 250)]
    [InlineData(20000, 500)]
    [InlineData(77000, 500)]
    public void Increments_are_tiered(int currentBid, int expected)
    {
        Assert.Equal(expected, BidRules.Increment(currentBid));
    }

    [Fact]
    public void Min_next_bid_adds_the_increment_or_is_the_opening_ask()
    {
        Assert.Equal(23_300, BidRules.MinNextBid(TestData.Vehicle(currentBid: 22_800)));
        Assert.Equal(14_500, BidRules.MinNextBid(TestData.Vehicle(currentBid: null)));
    }

    [Fact]
    public void Live_auction_accepts_a_bid_at_the_minimum_and_rejects_below_it()
    {
        var vehicle = TestData.Vehicle(id: IdWithStatus(AuctionStatus.Live), currentBid: 22_800);

        Assert.Equal(BidOutcomeKind.Accepted, BidRules.ResolveBid(vehicle, 23_300, Now).Kind);
        var rejected = BidRules.ResolveBid(vehicle, 23_299, Now);
        Assert.Equal(BidOutcomeKind.Rejected, rejected.Kind);
        Assert.Contains("at least", rejected.Reason);
    }

    [Theory]
    [InlineData(AuctionStatus.Upcoming)]
    [InlineData(AuctionStatus.Ended)]
    public void Non_live_auctions_reject_every_bid(AuctionStatus status)
    {
        var vehicle = TestData.Vehicle(id: IdWithStatus(status));
        Assert.Equal(BidOutcomeKind.Rejected, BidRules.ResolveBid(vehicle, 1_000_000, Now).Kind);
    }

    [Fact]
    public void Bid_at_or_above_buy_now_wins_at_the_buy_now_price()
    {
        // current 27,900 → min next 28,400, above the 28,000 buy-now: rule 5 wins.
        var vehicle = TestData.Vehicle(id: IdWithStatus(AuctionStatus.Live), currentBid: 27_900);
        vehicle = vehicle with { BuyNowPrice = 28_000 };

        Assert.Equal(BidOutcome.Won(28_000), BidRules.ResolveBid(vehicle, 28_000, Now));
        Assert.Equal(BidOutcome.Won(28_000), BidRules.ResolveBid(vehicle, 30_000, Now));
    }

    [Fact]
    public void Buy_now_wins_only_while_live_and_only_when_priced()
    {
        var live = TestData.Vehicle(id: IdWithStatus(AuctionStatus.Live)) with { BuyNowPrice = 28_000 };
        var ended = TestData.Vehicle(id: IdWithStatus(AuctionStatus.Ended)) with { BuyNowPrice = 28_000 };
        var unpriced = TestData.Vehicle(id: IdWithStatus(AuctionStatus.Live));

        Assert.Equal(BidOutcomeKind.Won, BidRules.ResolveBuyNow(live, Now).Kind);
        Assert.Equal(BidOutcomeKind.Rejected, BidRules.ResolveBuyNow(ended, Now).Kind);
        Assert.Equal(BidOutcomeKind.Rejected, BidRules.ResolveBuyNow(unpriced, Now).Kind);
    }
}
