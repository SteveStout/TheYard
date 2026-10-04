using System.ComponentModel;
using TheYard.Data;

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

/// <summary>One line of the account page's history: the vehicle, its title, and the caller's bid on it.</summary>
/// <param name="VehicleId">The id of the vehicle bid on.</param>
/// <param name="Title">Year, make and model, or (withdrawn) if the vehicle has left the catalogue.</param>
/// <param name="Bid">The caller's standing on the vehicle.</param>
public sealed record BidHistoryEntry(
    string VehicleId,
    [property: Description("Year, make and model, or (withdrawn) if the vehicle has left the catalogue.")] string Title,
    BidView Bid);

/// <summary>The account page's history, newest first.</summary>
/// <param name="Count">How many vehicles the caller has bid on.</param>
/// <param name="Bids">One entry per vehicle, newest first.</param>
public sealed record BidHistory(int Count, IReadOnlyList<BidHistoryEntry> Bids);

/// <summary>
/// A vehicle as a visitor meets it: at the price it stands at (the highest of the dataset,
/// everybody's bids and the room) and whether anybody has bought it, which closes it to everybody.
/// </summary>
/// <param name="Vehicle">The vehicle at its standing price.</param>
/// <param name="Sold">True when anybody has bought it outright.</param>
public sealed record StandingVehicle(Vehicle Vehicle, bool Sold);

/// <summary>One page of the catalogue as a visitor meets it, and how many vehicles matched in all.</summary>
/// <param name="Total">How many vehicles matched the filter, across every page.</param>
/// <param name="Vehicles">This page's vehicles, each standing at its price with its sold flag.</param>
public sealed record StandingPage(int Total, IReadOnlyList<StandingVehicle> Vehicles);
