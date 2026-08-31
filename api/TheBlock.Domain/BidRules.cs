using System.Globalization;
using TheBlock.Data;

namespace TheBlock.Domain;

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
///    even when it fails the minimum-increment check.
/// </summary>
public static class BidRules
{
    public static int Increment(int currentBid) =>
        currentBid < 5_000 ? 100 : currentBid < 20_000 ? 250 : 500;

    public static int MinNextBid(Vehicle vehicle) =>
        vehicle.CurrentBid is { } bid ? bid + Increment(bid) : vehicle.StartingBid;

    public static BidOutcome ResolveBid(Vehicle vehicle, int amount, AuctionClock clock)
    {
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

    public static BidOutcome ResolveBuyNow(Vehicle vehicle, AuctionClock clock)
    {
        if (vehicle.BuyNowPrice is not { } buyNow)
        {
            return BidOutcome.Rejected("This vehicle has no Buy Now price.");
        }
        return AuctionSchedule.StatusFor(vehicle.Id, clock) == AuctionStatus.Live
            ? BidOutcome.Won(buyNow)
            : BidOutcome.Rejected("Buy Now is only available while the auction is live.");
    }
}
