using System.Globalization;
using TheYard.Data;

namespace TheYard.Domain;

public enum BidOutcomeKind
{
    Rejected,
    Accepted,
    Won,
}

/// <summary>The result of a bid or buy-now attempt.</summary>
public readonly record struct BidOutcome(BidOutcomeKind Kind, int Amount, string? Reason)
{
    public static BidOutcome Rejected(string reason) => new(BidOutcomeKind.Rejected, 0, reason);
    public static BidOutcome Accepted(int amount) => new(BidOutcomeKind.Accepted, amount, null);
    public static BidOutcome Won(int amount) => new(BidOutcomeKind.Won, amount, null);
}

/// <summary>
/// The auction bidding rules, server-side and authoritative:
///  - tiered increments: under $5k +$100, $5k–$19,999 +$250, $20k and up +$500;
///  - the minimum next bid is the high bid plus its tier's increment, or the
///    opening ask before any bids exist;
///  - bids are valid only while the auction is live;
///  - a bid at or above buy_now_price wins outright at the buy-now price,
///    even when it fails the minimum-increment check;
///  - a vehicle somebody has bought is sold, to everybody: no bid and no second
///    purchase, whatever the clock says.
/// </summary>
public static class BidRules
{
    public static int Increment(int currentBid) =>
        currentBid < 5_000 ? 100 : currentBid < 20_000 ? 250 : 500;

    public static int MinNextBid(Vehicle vehicle) =>
        vehicle.CurrentBid is { } bid ? bid + Increment(bid) : vehicle.StartingBid;

    // #region sold
    /// <summary>
    /// The sentence a sold vehicle answers every bid and every purchase with.
    /// One string, because the store's fact and the page's words should not be
    /// able to drift apart.
    /// </summary>
    public const string SoldReason = "This vehicle has been sold.";

    /// <summary>
    /// <paramref name="sold"/> is whether anybody has bought this vehicle
    /// outright, which the rules cannot know from the vehicle alone: the
    /// dataset has no such field and the schedule has no such state. The caller
    /// that holds everybody's standing says so, and it is asked before the
    /// clock and before the buy-now shortcut: until 1.0.0.110 the shortcut came
    /// first, and a second account bidding the buy-now price on a vehicle
    /// already bought was told it had won it too (ADR: Accounts and per-user
    /// bids, the addendum on the second buyer).
    /// </summary>
    public static BidOutcome ResolveBid(Vehicle vehicle, int amount, AuctionClock clock, bool sold)
    {
        if (sold)
        {
            return BidOutcome.Rejected(SoldReason);
        }
        var status = AuctionSchedule.StatusFor(vehicle.Id, clock);
        if (status == AuctionStatus.Live && vehicle.BuyNowPrice is { } buyNow && amount >= buyNow)
        {
            // Instant win, charged the buy-now price rather than the overbid.
            return BidOutcome.Won(buyNow);
        }
        if (status == AuctionStatus.Upcoming)
        {
            return BidOutcome.Rejected("This auction has not started yet.");
        }
        if (status == AuctionStatus.Ended)
        {
            return BidOutcome.Rejected("This auction has ended.");
        }

        int min = MinNextBid(vehicle);
        return amount < min
            ? BidOutcome.Rejected($"Bid must be at least ${min.ToString("N0", CultureInfo.InvariantCulture)}.")
            : BidOutcome.Accepted(amount);
    }

    public static BidOutcome ResolveBuyNow(Vehicle vehicle, AuctionClock clock, bool sold)
    {
        if (sold)
        {
            return BidOutcome.Rejected(SoldReason);
        }
        if (vehicle.BuyNowPrice is not { } buyNow)
        {
            return BidOutcome.Rejected("This vehicle has no Buy Now price.");
        }
        return AuctionSchedule.StatusFor(vehicle.Id, clock) == AuctionStatus.Live
            ? BidOutcome.Won(buyNow)
            : BidOutcome.Rejected("Buy Now is only available while the auction is live.");
    }
    // #endregion sold
}
