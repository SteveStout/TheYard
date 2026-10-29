using System.Text.Json;
using TheYard.Application.Shared.Auction;
using TheYard.Data;

namespace TheYard.Infrastructure.Shared.Auction;

/// <summary>Shared serializer settings: the dataset is snake_case on disk and on the wire.</summary>
public static class VehicleJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
    };
}

/// <summary>Adapter: vehicles from a JSON file (data/vehicles.json).</summary>
public sealed class JsonFileVehicleSource(string path) : IVehicleSource
{
    /// <summary>Every vehicle in the file, read in one go.</summary>
    /// <param name="cancellation">The caller's, carried to the file read.</param>
    public async Task<IReadOnlyList<Vehicle>> LoadAsync(CancellationToken cancellation) =>
        JsonSerializer.Deserialize<List<Vehicle>>(await File.ReadAllTextAsync(path, cancellation), VehicleJson.Options)
        ?? throw new InvalidOperationException($"No vehicles could be read from {path}");
}

/// <summary>Adapter: photo manifest from the JSON file scripts/fetch_photos.mjs emits.</summary>
public sealed class JsonFilePhotoManifestSource(string path) : IPhotoManifestSource
{
    /// <summary>Every photo entry in the file, read in one go.</summary>
    /// <param name="cancellation">The caller's, carried to the file read.</param>
    public async Task<IReadOnlyList<PhotoEntry>> LoadAsync(CancellationToken cancellation) =>
        JsonSerializer.Deserialize<List<PhotoEntry>>(await File.ReadAllTextAsync(path, cancellation), VehicleJson.Options)
        ?? throw new InvalidOperationException($"No photo manifest could be read from {path}");
}
