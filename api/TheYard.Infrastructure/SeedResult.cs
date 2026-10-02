// The counts the first-boot seed hands back, so the startup log line can say what it did. It has
// a file of its own because it is a type of its own, read by YardDatabase and written by YardSeed.
namespace TheYard.Infrastructure;

/// <summary>What the first boot found, so the log line can say it.</summary>
/// <param name="VehiclesInserted">How many vehicle rows the seed wrote.</param>
/// <param name="PhotosInserted">How many photo rows the seed wrote.</param>
/// <param name="VehiclesTotal">How many vehicle rows the table holds after the seed.</param>
/// <param name="PhotosTotal">How many photo rows the table holds after the seed.</param>
public sealed record SeedResult(int VehiclesInserted, int PhotosInserted, int VehiclesTotal, int PhotosTotal);
