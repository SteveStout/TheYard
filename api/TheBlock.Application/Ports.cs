using TheBlock.Data;

namespace TheBlock.Application;

// #region ports
// The two seams the layers meet at. Application declares what it needs and
// never learns where the data lives; Infrastructure implements these against
// JSON files, the tests against in-memory arrays, and the 100,000-record
// scale-up is a decorator over IVehicleSource that nothing above it can see.
/// <summary>Port: where the vehicle dataset comes from.</summary>
public interface IVehicleSource
{
    IReadOnlyList<Vehicle> Load();
}

/// <summary>Port: where the photo manifest comes from.</summary>
public interface IPhotoManifestSource
{
    IReadOnlyList<PhotoEntry> Load();
}
// #endregion ports
