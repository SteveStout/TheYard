namespace TheYard.Application;

/// <summary>
/// One of the signed-in buyer's bids, as the page needs it. Outbid, HighestAmount and
/// MarketAmount are the three facts a badge cannot work out for itself: whether somebody has
/// gone higher, what the vehicle stands at now, and whether that somebody was the simulated
/// room. The server works them out so the browser never holds a second copy of the rule.
/// </summary>
/// <param name="Amount">The buyer's own bid, in whole dollars.</param>
/// <param name="BidCount">How many bids the vehicle has had, counting everybody's, including the simulated room.</param>
/// <param name="WonBuyNow">True when the buyer bought the vehicle outright.</param>
/// <param name="AtMs">When the buyer's bid was placed, in milliseconds since the epoch, UTC.</param>
/// <param name="Outbid">True when another buyer or the simulated room has gone higher and the vehicle was not bought outright.</param>
/// <param name="MarketAmount">What the simulated room has bid, in whole dollars, or null when it has not bid.</param>
/// <param name="HighestAmount">The highest bid on the vehicle from anyone, in whole dollars.</param>
public sealed record BidView(
    int Amount,
    int BidCount,
    bool WonBuyNow,
    long AtMs,
    bool Outbid,
    int? MarketAmount,
    int HighestAmount);
