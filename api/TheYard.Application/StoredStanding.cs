// One vehicle's standing as the store hands it back: the standing itself and the version a
// compare-and-set has to name. It has a file of its own because it is the shape of the bid
// port's standing read, shared by every store that implements it.
namespace TheYard.Application;

/// <summary>One vehicle's standing as the store keeps it, with the version a write has to match.</summary>
/// <param name="VehicleId">The id of the vehicle the standing is on.</param>
/// <param name="Standing">The price, the count, the high bidder and whether it has sold.</param>
/// <param name="Version">
/// The store's token for this exact standing, opaque to everybody but the store that issued it.
/// A write that names an older one is refused.
/// </param>
public sealed record StoredStanding(string VehicleId, VehicleStanding Standing, string Version);
