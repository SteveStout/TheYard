using TheBlock.Application;

namespace TheBlock.Api;

/// <summary>
/// One of the buyer's bids, as the page needs it (ADR: Competing bidders).
/// `outbid` and `market_amount` are the two facts a badge cannot work out for
/// itself: whether the room has since gone higher, and by how much. They are
/// derived here rather than in the browser for the same reason every other rule
/// in this system is derived on the server, which is that two implementations
/// of one rule eventually disagree.
/// </summary>
public sealed record BidView(
    int Amount,
    int BidCount,
    bool WonBuyNow,
    long AtMs,
    bool Outbid,
    int? MarketAmount);

public static class BidViews
{
    // #region views
    /// <summary>
    /// The buyer's map with the room's answer folded into each entry. A vehicle
    /// bought outright is never outbid, whatever the room does afterwards: the
    /// sale already happened.
    /// </summary>
    public static IReadOnlyDictionary<string, BidView> For(BidService bids, MarketService market)
    {
        // The buyer first, the room second. The two reads are not atomic, so a
        // bid placed between them shows up in one and not the other; taking
        // the room second means the stale answer is "you have been outbid"
        // rather than "you are winning", and of the two wrong answers only one
        // of them costs somebody an auction.
        var mine = bids.Snapshot();
        var room = market.Snapshot();
        var views = new Dictionary<string, BidView>(StringComparer.Ordinal);
        foreach (var (id, bid) in mine)
        {
            room.TryGetValue(id, out var against);
            bool outbid = !bid.WonBuyNow && against is not null && against.Amount > bid.Amount;
            views[id] = new BidView(
                bid.Amount,
                // The count both sides have contributed to, not just the
                // buyer's. Reporting the buyer's alone made the bid count fall
                // when they retook a lead the room had raised.
                Math.Max(bid.BidCount, against?.BidCount ?? 0),
                bid.WonBuyNow,
                bid.AtMs,
                outbid,
                against?.Amount);
        }
        return views;
    }
    // #endregion views
}
