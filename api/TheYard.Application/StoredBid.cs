// One bid as the store hands it back: who, on what, and their standing. It has a file of its own
// because it is the shape of the bid port's read, shared by every store that implements it.
namespace TheYard.Application;

/// <summary>One stored bid, as the store hands it back.</summary>
/// <param name="UserId">The id of the buyer who placed the bid.</param>
/// <param name="VehicleId">The id of the vehicle the bid is on.</param>
/// <param name="State">The buyer's standing on that vehicle.</param>
public sealed record StoredBid(string UserId, string VehicleId, BidState State);
