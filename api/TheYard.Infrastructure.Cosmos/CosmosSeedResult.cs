// What the first boot's seed wrote and paid. Its own file so the store's startup part reads as
// the steps it takes; the counts end up in the startup log line and the comparison card.
namespace TheYard.Infrastructure.Cosmos;

/// <summary>What the first boot found and paid, so the log line and the comparison card can say it.</summary>
/// <param name="VehiclesInserted">How many vehicle documents the seed wrote.</param>
/// <param name="PhotosInserted">How many photo documents the seed wrote.</param>
/// <param name="VehiclesTotal">How many vehicle documents the container holds after the seed.</param>
/// <param name="PhotosTotal">How many photo documents the container holds after the seed.</param>
/// <param name="SeedCharge">What the seed cost, in request units.</param>
public sealed record CosmosSeedResult(int VehiclesInserted, int PhotosInserted, int VehiclesTotal, int PhotosTotal, double SeedCharge);
