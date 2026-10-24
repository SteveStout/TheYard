// What the store does before it serves anything, and what the health check asks it afterwards:
// check that the containers exist with the partition keys the code expects, fill them from the
// seed files when they are short, and prove with two point reads that the catalogue is there.
// A class of its own because none of it runs on an ordinary request, and the store with it was
// over three hundred lines (ADR-088). The store builds one and hands it out as Starting.
using System.Diagnostics;
using Microsoft.Azure.Cosmos;
using TheYard.Application.Shared.Auction;
using TheYard.Application.Shared.Stores;

namespace TheYard.Infrastructure.Cosmos.Shared.Stores;

/// <summary>The startup work of a <see cref="CosmosStore"/>: prepare, seed and probe.</summary>
/// <param name="store">The store it brings up.</param>
public sealed class CosmosStoreStartup(CosmosStore store)
{
    /// <summary>The id and make of one seed vehicle, remembered so the probe can point-read it.</summary>
    private (string Id, string Make)? _firstVehicle;

    /// <summary>The id and style of one seed photo, remembered so the probe can point-read it.</summary>
    private (string Id, string Style)? _firstPhoto;

    // #region prepare
    /// <summary>
    /// Bring the store up, or report that it could not be brought up, in the
    /// same shape as the relational side. This process holds a data-plane role
    /// and cannot create a container, so the only honest thing it can do is
    /// check that the containers it maps to are there, with the partition keys
    /// the code was written for, and refuse the store if they are not
    /// (ADR: Data first, and the database in source control, addendum).
    /// </summary>
    public async Task<DatabaseState> PrepareAsync(IVehicleSource seedVehicles, IPhotoManifestSource seedPhotos)
    {
        try
        {
            var checking = Stopwatch.StartNew();
            foreach (string name in Containers.Required)
            {
                string expectedKey = Containers.PartitionKeyPaths[name];
                var container = store.ContainerNamed(name);
                var response = await store.Recorder.Timed(container, StoreOperationKind.Metadata, "ReadContainer", [], "n/a", 0,
                    () => container.ReadContainerAsync(), r => r.Cost());
                string actualKey = response.Resource.PartitionKeyPath;
                if (!string.Equals(actualKey, expectedKey, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"container {name} is partitioned on {actualKey} and the code was written for {expectedKey}. "
                        + "Apply infra/cosmos before pointing a container at this account.");
                }
                var ranges = await container.GetFeedRangesAsync();
                store.KeepPhysicalPartitions(name, ranges.Count);
            }
            checking.Stop();

            var seeding = Stopwatch.StartNew();
            var seeded = await EnsureSeededAsync(seedVehicles, seedPhotos);
            seeding.Stop();
            store.Startup = new StartupCost(checking.ElapsedMilliseconds, seeding.ElapsedMilliseconds, seeded.SeedCharge, seeded.VehiclesInserted, seeded.PhotosInserted);

            return new DatabaseState(
                true,
                $"{store.Describe()}, found {Containers.Required.Count} containers in {checking.ElapsedMilliseconds} ms "
                + $"and seeded in {seeding.ElapsedMilliseconds} ms for {seeded.SeedCharge:0.#} RU, "
                + $"inserting {seeded.VehiclesInserted} vehicles and {seeded.PhotosInserted} photos, "
                + $"now holding {seeded.VehiclesTotal} and {seeded.PhotosTotal}")
            {
                SchemaMs = checking.ElapsedMilliseconds,
                SeedMs = seeding.ElapsedMilliseconds,
                SeedRequestUnits = Math.Round(seeded.SeedCharge, 2),
            };
        }
        catch (Exception ex)
        {
            // Deliberately every exception, for the same reason as the
            // relational side: the caller keeps serving from files, and it
            // cannot do that if this throws. The type travels; the message,
            // which names the account, does not (ADR: The relational store).
            return new DatabaseState(false, $"{store.Describe()}: {ex.GetType().Name}", ex);
        }
    }
    // #endregion prepare

    // #region seed
    /// <summary>
    /// First boot fills the containers from the seed files in the repository,
    /// exactly as the relational seed does. "Short" rather than
    /// "empty": the relational seed is one transaction and is either all there
    /// or not there, but these are two hundred and fifty point writes one at a
    /// time, and a process that died after a hundred of them would have left a
    /// container that was not empty and not seeded either. A check for empty
    /// would pass that container by (ADR: Reviewing my own work, the second
    /// pass). So a container holding fewer documents than the seed file is
    /// seeded again with upserts, which put back what is missing and rewrite
    /// what is there at about the cost of a create each. Each one's charge is
    /// added up: the sum is the number the comparison card shows as the seed
    /// cost, and it is measured rather than estimated
    /// (ADR: A second store on Cosmos DB, and what it costs).
    /// </summary>
    public async Task<CosmosSeedResult> EnsureSeededAsync(IVehicleSource vehicles, IPhotoManifestSource photos)
    {
        double charge = 0;
        int vehiclesAdded = 0;
        int photosAdded = 0;

        int vehicleCount = await store.CountAsync(store.Vehicles);
        var seedVehicles = await vehicles.LoadAsync();
        if (vehicleCount < seedVehicles.Count)
        {
            int seq = 0;
            foreach (var vehicle in seedVehicles)
            {
                var document = vehicle.ToDocument(seq++);
                var response = await store.Recorder.Timed(store.Vehicles, StoreOperationKind.PointWrite, "UpsertItem (seed)", [], "pinned to the make", 1,
                    () => store.Vehicles.UpsertItemAsync(document, new PartitionKey(document.Make)), r => r.Cost());
                charge += response.RequestCharge;
                vehiclesAdded++;
            }
            _firstVehicle = null;
        }

        int photoCount = await store.CountAsync(store.Photos);
        var seedPhotos = await photos.LoadAsync();
        if (photoCount < seedPhotos.Count)
        {
            int seq = 0;
            foreach (var photo in seedPhotos)
            {
                var document = photo.ToDocument(seq++);
                var response = await store.Recorder.Timed(store.Photos, StoreOperationKind.PointWrite, "UpsertItem (seed)", [], "pinned to the style", 1,
                    () => store.Photos.UpsertItemAsync(document, new PartitionKey(document.Style)), r => r.Cost());
                charge += response.RequestCharge;
                photosAdded++;
            }
            _firstPhoto = null;
        }

        return new CosmosSeedResult(vehiclesAdded, photosAdded, vehiclesAdded > 0 ? vehiclesAdded : vehicleCount, photosAdded > 0 ? photosAdded : photoCount, charge);
    }
    // #endregion seed

    // #region probe
    /// <summary>
    /// The health check's question: is the seed catalogue in the store. Two
    /// point reads, about a request unit each, rather than two count queries,
    /// because an open Admin tab asks every thirty seconds and a count is a scan
    /// of the container every time it is asked.
    /// </summary>
    public async Task<bool> ProbeAsync()
    {
        _firstVehicle ??= await FirstAsync<VehicleDocument, (string, string)>(store.Vehicles, d => (d.Id, d.Make));
        _firstPhoto ??= await FirstAsync<PhotoDocument, (string, string)>(store.Photos, d => (d.Id, d.Style));
        if (_firstVehicle is null || _firstPhoto is null)
        {
            return false;
        }

        var vehicle = await store.ReadAsync<VehicleDocument>(store.Vehicles, _firstVehicle.Value.Id, _firstVehicle.Value.Make, "pinned to the make");
        var photo = await store.ReadAsync<PhotoDocument>(store.Photos, _firstPhoto.Value.Id, _firstPhoto.Value.Style, "pinned to the style");
        return vehicle is not null && photo is not null;
    }

    /// <summary>
    /// Any one document of a container, reduced to the fields the probe needs
    /// to point-read it again, or null when the container is empty. The probe
    /// keeps the answer, so this query is not paid on every health check.
    /// </summary>
    private async Task<TResult?> FirstAsync<TDocument, TResult>(Container container, Func<TDocument, TResult> pick)
        where TResult : struct
    {
        var page = await store.QueryAsync<TDocument>(container, new QueryDefinition("SELECT TOP 1 * FROM c"), partitionKey: null, "cross-partition");
        return page.Count == 0 ? null : pick(page[0]);
    }
    // #endregion probe
}
