using TheYard.Application;

namespace TheYard.Api;

/// <summary>
/// One of the signed-in buyer's bids, as the page needs it
/// (ADR: Accounts and per-user bids). `outbid`, `highest_amount` and
/// `market_amount` are the three facts a badge cannot work out for itself:
/// whether somebody has gone higher, what it stands at now, and whether the
/// somebody was the simulated room. They are derived here rather than in the
/// browser for the same reason every other rule in this system is derived on
/// the server, which is that two implementations of one rule eventually
/// disagree.
/// </summary>
public sealed record BidView(
    int Amount,
    int BidCount,
    bool WonBuyNow,
    long AtMs,
    bool Outbid,
    int? MarketAmount,
    int HighestAmount);

public static class BidViews
{
    // #region views
    /// <summary>
    /// This user's map, with everybody else's answer folded into each entry. A
    /// vehicle bought outright is never outbid, whatever anyone does
    /// afterwards: the sale already happened.
    /// </summary>
    public static IReadOnlyDictionary<string, BidView> For(BidService bids, MarketService market, string userId)
    {
        // Mine first, everybody else second. The reads are not atomic, so a bid
        // placed between them shows up in one and not the other; taking the
        // others second means the stale answer is "you have been outbid" rather
        // than "you are winning", and of the two wrong answers only one of them
        // costs somebody an auction.
        var mine = bids.SnapshotFor(userId);
        var standing = bids.Standing();
        var room = market.Snapshot();
        var views = new Dictionary<string, BidView>(StringComparer.Ordinal);
        foreach (var (id, bid) in mine)
        {
            standing.TryGetValue(id, out var held);
            room.TryGetValue(id, out var against);

            int highest = Math.Max(held?.Amount ?? bid.Amount, against?.Amount ?? 0);
            bool someoneElseHolds = held is not null && held.Amount > bid.Amount;
            bool roomHolds = against is not null && against.Amount > bid.Amount;
            bool outbid = !bid.WonBuyNow && (someoneElseHolds || roomHolds);

            views[id] = new BidView(
                bid.Amount,
                // The count everybody has contributed to, not just this
                // buyer's. Reporting one person's alone made the bid count fall
                // when they retook a lead somebody else had raised.
                Math.Max(Math.Max(bid.BidCount, held?.BidCount ?? 0), against?.BidCount ?? 0),
                bid.WonBuyNow,
                bid.AtMs,
                outbid,
                against?.Amount,
                highest);
        }
        return views;
    }
    // #endregion views
}
