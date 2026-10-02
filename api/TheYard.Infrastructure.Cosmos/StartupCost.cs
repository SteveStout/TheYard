// The startup numbers the store keeps for the Admin tab: how long the container check and the
// seed took, and what the seed cost. Its own file because the comparison card reads it long
// after startup is over.
namespace TheYard.Infrastructure.Cosmos;

/// <summary>The startup numbers the Admin tab's comparison card shows: how long the containers took to check, how long the seed took and what it cost.</summary>
/// <param name="CheckMs">How long checking the containers took, in milliseconds.</param>
/// <param name="SeedMs">How long the seed took, in milliseconds.</param>
/// <param name="SeedRequestUnits">What the seed cost, in request units.</param>
/// <param name="VehiclesSeeded">How many vehicle documents the seed wrote.</param>
/// <param name="PhotosSeeded">How many photo documents the seed wrote.</param>
public sealed record StartupCost(long CheckMs, long SeedMs, double SeedRequestUnits, int VehiclesSeeded, int PhotosSeeded);
