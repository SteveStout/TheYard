// One buyer's standing on one vehicle, as BidService keeps it and the store writes it. It has a
// file of its own because the store, the simulated room and the endpoints all pass it around,
// and none of them should have to open BidService to find its shape.
namespace TheYard.Application;

/// <summary>
/// One buyer's standing on one vehicle. AtMs is when the bid was placed, which
/// the simulated room reads to decide whether enough time has passed to answer
/// it (ADR: Competing bidders).
/// </summary>
/// <param name="Amount">The buyer's bid, in whole dollars.</param>
/// <param name="BidCount">How many bids the vehicle had once this one was placed, counting everybody's.</param>
/// <param name="WonBuyNow">True when the bid bought the vehicle outright at or above its buy-now price.</param>
/// <param name="AtMs">When the bid was placed, in milliseconds since the epoch, UTC.</param>
public sealed record BidState(int Amount, int BidCount, bool WonBuyNow, long AtMs);
