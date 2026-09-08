using System.Text.Json;
using TheYard.Application;
using TheYard.Data;

namespace TheYard.Infrastructure;

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
    public async Task<IReadOnlyList<Vehicle>> LoadAsync() =>
        JsonSerializer.Deserialize<List<Vehicle>>(await File.ReadAllTextAsync(path), VehicleJson.Options)
        ?? throw new InvalidOperationException($"No vehicles could be read from {path}");
}

/// <summary>Adapter: photo manifest from the JSON file scripts/fetch_photos.mjs emits.</summary>
public sealed class JsonFilePhotoManifestSource(string path) : IPhotoManifestSource
{
    public async Task<IReadOnlyList<PhotoEntry>> LoadAsync() =>
        JsonSerializer.Deserialize<List<PhotoEntry>>(await File.ReadAllTextAsync(path), VehicleJson.Options)
        ?? throw new InvalidOperationException($"No photo manifest could be read from {path}");
}
