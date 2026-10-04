using TheYard.Domain;

namespace TheYard.Tests;

/// <summary>
/// The one rule for raising a vehicle's shown price, and the reserve state read from it. Each
/// has one copy in the code (StandingRules), held here once.
/// </summary>
public sealed class StandingRulesTests
{
    [Fact]
    public void A_bid_raises_the_price_only_when_it_is_higher_and_keeps_the_larger_count()
    {
        var vehicle = TestData.Vehicle(currentBid: 22_800);

        var raised = vehicle.RaisedTo(23_300, 3);
        var lower = vehicle.RaisedTo(20_000, 99);

        Assert.Equal(23_300, raised.CurrentBid);
        Assert.Equal(16, raised.BidCount);
        Assert.Same(vehicle, lower);
        Assert.Equal(40, TestData.Vehicle(currentBid: null).RaisedTo(500, 40).BidCount);
    }

    [Theory]
    [InlineData(null, 30_000, ReserveStatus.NoReserve)]
    [InlineData(25_000, null, ReserveStatus.NotMet)]
    [InlineData(25_000, 24_999, ReserveStatus.NotMet)]
    [InlineData(25_000, 25_000, ReserveStatus.Met)]
    public void The_reserve_is_met_once_the_standing_bid_reaches_it(int? reserve, int? standing, ReserveStatus expected)
    {
        var vehicle = TestData.Vehicle(currentBid: standing) with { ReservePrice = reserve };

        Assert.Equal(expected, vehicle.Reserve);
    }
}
