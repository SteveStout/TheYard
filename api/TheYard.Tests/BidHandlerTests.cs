using TheYard.Application.Features.Bids;

namespace TheYard.Tests;

// #region bid-handler-tests
/// <summary>
/// The Bids slice's handlers with fakes at the auction's ports and no host (ADR: The onion cut into
/// vertical slices, rule 7): each answer the browser's status codes come from, decided where the
/// rules are composed.
/// </summary>
public sealed class BidHandlerTests
{
    [Fact]
    public async Task A_bid_is_refused_in_order_session_then_vehicle_then_rules_and_a_good_one_is_placed()
    {
        var vehicle = TestAuctions.Live("bid");
        var handler = new PlaceBidHandler(TestAuctions.Over(vehicle), TestAuctions.AtNoon);

        Assert.Equal(BidAnswerKind.SessionElsewhere, (await handler.HandleAsync(new PlaceBid("nobody-has-this-id", "u1", false, 1), CancellationToken.None)).Kind);
        Assert.Equal(BidAnswerKind.NoSuchVehicle, (await handler.HandleAsync(new PlaceBid("nobody-has-this-id", "u1", true, 1), CancellationToken.None)).Kind);
        var low = await handler.HandleAsync(new PlaceBid(vehicle.Id, "u1", true, 1), CancellationToken.None);
        Assert.Equal(BidAnswerKind.Rejected, low.Kind);
        Assert.False(string.IsNullOrWhiteSpace(low.Reason));

        var placed = await handler.HandleAsync(new PlaceBid(vehicle.Id, "u1", true, 30_000), CancellationToken.None);
        Assert.Equal(BidAnswerKind.Placed, placed.Kind);
        Assert.Equal(30_000, placed.Vehicle!.Vehicle.CurrentBid);
        Assert.NotNull(placed.Standing);
        Assert.NotNull(placed.Clock);
    }

    [Fact]
    public async Task Buy_now_sells_the_vehicle_and_a_vehicle_without_a_price_refuses_it()
    {
        var priced = TestAuctions.Live("buy", buyNow: 40_000);
        var unpriced = TestAuctions.Live("nobuy");
        var handler = new PlaceBidHandler(TestAuctions.Over(priced, unpriced), TestAuctions.AtNoon);

        var bought = await handler.HandleAsync(new PlaceBid(priced.Id, "u1", true, null), CancellationToken.None);
        Assert.Equal(BidAnswerKind.Placed, bought.Kind);
        Assert.True(bought.Vehicle!.Sold);
        Assert.Equal(BidAnswerKind.Rejected, (await handler.HandleAsync(new PlaceBid(unpriced.Id, "u1", true, null), CancellationToken.None)).Kind);
    }

    [Fact]
    public async Task The_callers_badges_history_round_and_start_over_answer_for_them_alone()
    {
        var vehicle = TestAuctions.Live("mine");
        var auction = TestAuctions.Over(vehicle);
        await new PlaceBidHandler(auction, TestAuctions.AtNoon).HandleAsync(new PlaceBid(vehicle.Id, "u1", true, 30_000), CancellationToken.None);

        Assert.Single(await new MyBidsHandler(auction).HandleAsync("u1", CancellationToken.None));
        Assert.Empty(await new MyBidsHandler(auction).HandleAsync("u2", CancellationToken.None));
        Assert.Empty(await new MyBidsHandler(auction).HandleAsync(null, CancellationToken.None));
        Assert.Equal(1, (await new BidHistoryHandler(auction).HandleAsync("u1", CancellationToken.None)).Count);

        var round = await new RoomRoundHandler(auction, TestAuctions.AtNoon).HandleAsync("u1", CancellationToken.None);
        Assert.True(round.Raised >= 0);
        Assert.Single(round.Bids);

        Assert.False(await new ResetBidsHandler(auction).HandleAsync(null, CancellationToken.None));
        Assert.True(await new ResetBidsHandler(auction).HandleAsync("u1", CancellationToken.None));
        Assert.Empty(await new MyBidsHandler(auction).HandleAsync("u1", CancellationToken.None));
    }
}
// #endregion bid-handler-tests
