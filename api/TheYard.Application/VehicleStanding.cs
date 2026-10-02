// One vehicle's bidding across everybody: the price, the count, and who holds it. It has a file
// of its own because the listing overlay and the outbid badge both read it, and it is the answer
// BidService's hot path hands back.
namespace TheYard.Application;

/// <summary>
/// One vehicle's bidding, across everybody: what it stands at, how many bids
/// got it there, and who holds it. This is what the listing overlay reads and
/// what "you have been outbid" is measured against
/// (ADR: Accounts and per-user bids).
/// </summary>
/// <param name="Amount">The highest bid on the vehicle, in whole dollars.</param>
/// <param name="BidCount">How many bids the vehicle has had, counting everybody's.</param>
/// <param name="HighBidderId">The user id of the buyer holding the highest bid.</param>
/// <param name="SoldBuyNow">True once any buyer has bought the vehicle outright.</param>
/// <param name="AtMs">When the standing bid was placed, in milliseconds since the epoch, UTC.</param>
public sealed record VehicleStanding(int Amount, int BidCount, string HighBidderId, bool SoldBuyNow, long AtMs);
