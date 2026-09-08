using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using TheYard.Application;

namespace TheYard.Infrastructure.Cosmos;

/// <summary>
/// The one connection to Azure Cosmos DB, and everything the adapters share:
/// the client, the containers, the seed, the probe the health check runs, and
/// the wrapper that writes every operation's request charge to the store log
/// (ADR: A second store on Cosmos DB, and what it costs).
///
/// <para>One client per process, on purpose. The SDK's client holds the TCP
/// connections, the routing map and the partition key ranges of every
/// container it has touched, and a second client is a second copy of all of
/// that plus a second warm-up.</para>
/// </summary>
public sealed class CosmosStore
{
    private readonly CosmosClient _client;
    private readonly Database _database;
    private readonly string _prefix;
    private readonly IStoreLog _log;
    private readonly Dictionary<string, int> _physicalPartitions = new(StringComparer.Ordinal);
    private (string Id, string Make)? _firstVehicle;
    private (string Id, string Style)? _firstPhoto;

    /// <summary>The wire shape: snake case, like the dataset and the API.</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // #region connect
    /// <summary>
    /// Connect as the container's managed identity, or as whoever is signed in
    /// to the Azure CLI on a developer's machine, and the configuration says
    /// which. There is no third way in, because the account has no keys: local
    /// authentication was disabled when it was created, so there is nothing to
    /// put in a connection string and nothing to leak (ADR: A second store on
    /// Cosmos DB, and what it costs).
    ///
    /// <para>Chosen by a setting rather than probed. The first draft chained
    /// the two and let the identity fail over to the CLI, and the store tests
    /// found out how that fails on a machine with no identity endpoint: the
    /// probe of the instance metadata service retries for seconds and then
    /// reports an authentication failure rather than an absence, which no chain
    /// falls through. A deployed container says "managed-identity" in its
    /// environment and a developer's machine says nothing, and each gets one
    /// credential that either works or says why.</para>
    /// </summary>
    public static CosmosStore Connect(string accountEndpoint, string databaseName, string containerPrefix, string credentialKind, string managedIdentityClientId, IStoreLog log)
    {
        TokenCredential credential = string.Equals(credentialKind, ManagedIdentity, StringComparison.OrdinalIgnoreCase)
            ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(managedIdentityClientId))
            : new AzureCliCredential();
        var options = new CosmosClientOptions
        {
            ApplicationName = "TheYard",
            // Session consistency is the account default and what this
            // application needs; the option is set here so the choice is in
            // the code and not only in the portal (ADR: A second store on
            // Cosmos DB, and what it costs).
            ConsistencyLevel = ConsistencyLevel.Session,
            UseSystemTextJsonSerializerWithOptions = Json,
            // The SDK retries a 429 on its own. Nine tries over thirty seconds
            // is what a seed against a 1000 RU/s database needs; a request that
            // is still being throttled after that is a request worth failing.
            MaxRetryAttemptsOnRateLimitedRequests = 9,
            MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(30),
        };
        var client = new CosmosClient(accountEndpoint, credential, options);
        return new CosmosStore(client, databaseName, containerPrefix, log);
    }

    /// <summary>The value of <c>Cosmos:Credential</c> that means the container's own identity. Anything else means the Azure CLI.</summary>
    public const string ManagedIdentity = "managed-identity";
    // #endregion connect

    public CosmosStore(CosmosClient client, string databaseName, string containerPrefix, IStoreLog log)
    {
        _client = client;
        _database = client.GetDatabase(databaseName);
        _prefix = containerPrefix;
        _log = log;
    }

    /// <summary>
    /// What request is in flight, asked at record time. Set by the host after
    /// the container is built, because the request describer lives in DI and
    /// the store is created before DI exists. Null outside a request, which is
    /// what a startup operation records.
    /// </summary>
    public ICurrentRequest CurrentRequest { get; set; } = NoCurrentRequest.Instance;

    /// <summary>Any container of this database by its catalog name, prefixed like the rest. The experiment reads the catalogue through this.</summary>
    public Container ContainerNamed(string name) => _database.GetContainer(_prefix + name);

    public Container Vehicles => _database.GetContainer(_prefix + Containers.Vehicles);
    public Container Photos => _database.GetContainer(_prefix + Containers.Photos);
    public Container Bids => _database.GetContainer(_prefix + Containers.Bids);
    public Container Users => _database.GetContainer(_prefix + Containers.Users);

    /// <summary>What this process may say about its store: the engine, and nothing else.</summary>
    public string Describe() => "Azure Cosmos DB";

    /// <summary>How the seed and the cold start paid, for the Admin tab's comparison card.</summary>
    public StartupCost Startup { get; private set; } = new(0, 0, 0, 0, 0);

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
                var container = _database.GetContainer(_prefix + name);
                var response = await Timed(container, StoreOperationKind.Metadata, "ReadContainer", [], "n/a", 0,
                    () => container.ReadContainerAsync(), r => r.Cost());
                string actualKey = response.Resource.PartitionKeyPath;
                if (!string.Equals(actualKey, expectedKey, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"container {name} is partitioned on {actualKey} and the code was written for {expectedKey}. "
                        + "Apply infra/cosmos before pointing a container at this account.");
                }
                var ranges = await container.GetFeedRangesAsync();
                _physicalPartitions[name] = ranges.Count;
            }
            checking.Stop();

            var seeding = Stopwatch.StartNew();
            var seeded = await EnsureSeededAsync(seedVehicles, seedPhotos);
            seeding.Stop();
            Startup = new StartupCost(checking.ElapsedMilliseconds, seeding.ElapsedMilliseconds, seeded.SeedCharge, seeded.VehiclesInserted, seeded.PhotosInserted);

            return new DatabaseState(
                true,
                $"{Describe()}, found {Containers.Required.Count} containers in {checking.ElapsedMilliseconds} ms "
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
            return new DatabaseState(false, $"{Describe()}: {ex.GetType().Name}", ex);
        }
    }
    // #endregion prepare

    // #region seed
    /// <summary>
    /// First boot fills the containers from the files that used to be the
    /// catalogue, exactly as the relational seed does. "Empty" rather than
    /// "new", so a process that died mid-seed is not left half seeded forever.
    /// Two hundred and fifty point writes, one at a time, each one's charge
    /// added up: the sum is the number the comparison card shows as the seed
    /// cost, and it is measured rather than estimated
    /// (ADR: A second store on Cosmos DB, and what it costs).
    /// </summary>
    public async Task<CosmosSeedResult> EnsureSeededAsync(IVehicleSource vehicles, IPhotoManifestSource photos)
    {
        double charge = 0;
        int vehiclesAdded = 0;
        int photosAdded = 0;

        int vehicleCount = await CountAsync(Vehicles);
        if (vehicleCount == 0)
        {
            int seq = 0;
            foreach (var vehicle in await vehicles.LoadAsync())
            {
                var document = vehicle.ToDocument(seq++);
                var response = await Timed(Vehicles, StoreOperationKind.PointWrite, "CreateItem (seed)", [], "pinned to the make", 1,
                    () => Vehicles.CreateItemAsync(document, new PartitionKey(document.Make)), r => r.Cost());
                charge += response.RequestCharge;
                vehiclesAdded++;
            }
            _firstVehicle = null;
        }

        int photoCount = await CountAsync(Photos);
        if (photoCount == 0)
        {
            int seq = 0;
            foreach (var photo in await photos.LoadAsync())
            {
                var document = photo.ToDocument(seq++);
                var response = await Timed(Photos, StoreOperationKind.PointWrite, "CreateItem (seed)", [], "pinned to the style", 1,
                    () => Photos.CreateItemAsync(document, new PartitionKey(document.Style)), r => r.Cost());
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
        _firstVehicle ??= await FirstAsync<VehicleDocument, (string, string)>(Vehicles, d => (d.Id, d.Make));
        _firstPhoto ??= await FirstAsync<PhotoDocument, (string, string)>(Photos, d => (d.Id, d.Style));
        if (_firstVehicle is null || _firstPhoto is null)
        {
            return false;
        }

        var vehicle = await ReadAsync<VehicleDocument>(Vehicles, _firstVehicle.Value.Id, _firstVehicle.Value.Make, "pinned to the make");
        var photo = await ReadAsync<PhotoDocument>(Photos, _firstPhoto.Value.Id, _firstPhoto.Value.Style, "pinned to the style");
        return vehicle is not null && photo is not null;
    }

    private async Task<TResult?> FirstAsync<TDocument, TResult>(Container container, Func<TDocument, TResult> pick)
        where TResult : struct
    {
        var page = await QueryAsync<TDocument>(container, new QueryDefinition("SELECT TOP 1 * FROM c"), partitionKey: null, "cross-partition");
        return page.Count == 0 ? null : pick(page[0]);
    }
    // #endregion probe

    /// <summary>How many physical partitions a container has, read once at startup. One, at this size (ADR: The partition key).</summary>
    public int PhysicalPartitionsOf(string containerName) =>
        _physicalPartitions.TryGetValue(containerName, out int count) ? count : 1;

    private string NameOf(Container container) =>
        container.Id.StartsWith(_prefix, StringComparison.Ordinal) ? container.Id[_prefix.Length..] : container.Id;

    // #region operations
    /// <summary>A point read that answers null on 404 rather than throwing, because a missing document is an ordinary answer.</summary>
    public async Task<T?> ReadAsync<T>(Container container, string id, string partitionKey, string partitionLabel) where T : class =>
        (await ReadMeasuredAsync<T>(container, id, partitionKey, partitionLabel)).Item;

    /// <summary>The same point read, with what it cost handed back to the caller as well as to the log. The experiment card reads through this.</summary>
    public async Task<MeasuredItem<T>> ReadMeasuredAsync<T>(Container container, string id, string partitionKey, string partitionLabel) where T : class
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var response = await Timed(container, StoreOperationKind.PointRead, "ReadItem", [new SqlParameterShape("id", "String", id.Length)], partitionLabel, 1,
                () => container.ReadItemAsync<T>(id, new PartitionKey(partitionKey)), r => r.Cost());
            return new MeasuredItem<T>(response.Resource, Math.Round(response.RequestCharge, 2), clock.ElapsedMilliseconds);
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return new MeasuredItem<T>(null, Math.Round(ex.RequestCharge, 2), clock.ElapsedMilliseconds);
        }
    }

    public Task<ItemResponse<T>> CreateAsync<T>(Container container, T document, string partitionKey, string partitionLabel, string what = "CreateItem") =>
        Timed(container, StoreOperationKind.PointWrite, what, [], partitionLabel, 1,
            () => container.CreateItemAsync(document, new PartitionKey(partitionKey)), r => r.Cost());

    /// <summary>A replace that carries the etag it read, so a stale write is refused with 412 rather than winning.</summary>
    public Task<ItemResponse<T>> ReplaceAsync<T>(Container container, T document, string id, string partitionKey, string? etag, string partitionLabel) =>
        Timed(container, StoreOperationKind.PointWrite, "ReplaceItem (If-Match)", [new SqlParameterShape("id", "String", id.Length)], partitionLabel, 1,
            () => container.ReplaceItemAsync(document, id, new PartitionKey(partitionKey), new ItemRequestOptions { IfMatchEtag = etag }), r => r.Cost());

    public Task<ItemResponse<T>> DeleteAsync<T>(Container container, string id, string partitionKey, string partitionLabel) =>
        Timed(container, StoreOperationKind.PointDelete, "DeleteItem", [new SqlParameterShape("id", "String", id.Length)], partitionLabel, 1,
            () => container.DeleteItemAsync<T>(id, new PartitionKey(partitionKey)), r => r.Cost());

    /// <summary>
    /// A query, every page of it, with the charge of every page added up and
    /// one line in the store log saying how many pages it took. Pinned to one
    /// partition when the caller can name it, and a fan-out across every
    /// physical partition when it cannot, which is the difference the Admin tab
    /// exists to show (ADR: The partition key).
    /// </summary>
    public async Task<IReadOnlyList<T>> QueryAsync<T>(Container container, QueryDefinition query, string? partitionKey, string partitionLabel) =>
        (await QueryMeasuredAsync<T>(container, query, partitionKey, partitionLabel)).Items;

    /// <summary>The same query, with the summed charge, the page count and the time handed back as well as logged.</summary>
    public async Task<MeasuredQuery<T>> QueryMeasuredAsync<T>(Container container, QueryDefinition query, string? partitionKey, string partitionLabel)
    {
        var parameters = query.GetQueryParameters()
            .Select(p => new SqlParameterShape(p.Name, p.Value?.GetType().Name ?? "null", p.Value is string s ? s.Length : null))
            .ToList();
        var options = new QueryRequestOptions { MaxItemCount = -1 };
        if (partitionKey is not null)
        {
            options.PartitionKey = new PartitionKey(partitionKey);
        }
        int physical = partitionKey is null ? PhysicalPartitionsOf(NameOf(container)) : 1;

        var results = new List<T>();
        double charge = 0;
        int pages = 0;
        var clock = Stopwatch.StartNew();
        string outcome;
        try
        {
            using var iterator = container.GetItemQueryIterator<T>(query, requestOptions: options);
            while (iterator.HasMoreResults)
            {
                var page = await iterator.ReadNextAsync();
                charge += page.RequestCharge;
                pages++;
                results.AddRange(page);
            }
            outcome = $"{results.Count} document(s) in {pages} page(s)";
        }
        catch (CosmosException ex)
        {
            charge += ex.RequestCharge;
            outcome = "failed: " + ex.StatusCode;
            Record(container, StoreOperationKind.Query, query.QueryText, parameters, partitionLabel, physical, charge, clock.Elapsed, outcome);
            throw;
        }
        Record(container, StoreOperationKind.Query, query.QueryText, parameters, partitionLabel, physical, charge, clock.Elapsed, outcome);
        return new MeasuredQuery<T>(results, Math.Round(charge, 2), pages, clock.ElapsedMilliseconds);
    }

    /// <summary>One partition's deletes, at most a hundred at a time, as one atomic batch.</summary>
    public async Task<double> DeleteBatchAsync(Container container, string partitionKey, IReadOnlyList<string> ids, string partitionLabel)
    {
        double charge = 0;
        for (int start = 0; start < ids.Count; start += 100)
        {
            var slice = ids.Skip(start).Take(100).ToList();
            var batch = container.CreateTransactionalBatch(new PartitionKey(partitionKey));
            foreach (string id in slice)
            {
                batch.DeleteItem(id);
            }
            var response = await Timed(container, StoreOperationKind.Batch, $"TransactionalBatch: {slice.Count} DeleteItem", [], partitionLabel, 1,
                async () =>
                {
                    var r = await batch.ExecuteAsync();
                    if (!r.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException($"the batch was refused with {r.StatusCode}");
                    }
                    return r;
                }, r => r.Cost());
            charge += response.RequestCharge;
        }
        return charge;
    }

    private async Task<int> CountAsync(Container container)
    {
        var counts = await QueryAsync<int>(container, new QueryDefinition("SELECT VALUE COUNT(1) FROM c"), null, "cross-partition");
        return counts.Count == 0 ? 0 : counts[0];
    }

    /// <summary>
    /// Run one operation and write its charge and its duration to the store
    /// log, whether it succeeded or not. The store log is a public page: this
    /// records the kind, the container, the shape of the parameters and the
    /// charge, and never an id that could be an address or a value that could
    /// be anything (ADR: What the store is actually doing).
    /// </summary>
    private async Task<TResponse> Timed<TResponse>(Container container, string kind, string text, IReadOnlyList<SqlParameterShape> parameters, string partitionLabel, int physical, Func<Task<TResponse>> operation, Func<TResponse, Cost> cost)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            var response = await operation();
            var paid = cost(response);
            Record(container, kind, text, parameters, partitionLabel, physical, paid.Charge, clock.Elapsed, paid.Status.ToString());
            return response;
        }
        catch (CosmosException ex)
        {
            Record(container, kind, text, parameters, partitionLabel, physical, ex.RequestCharge, clock.Elapsed, "failed: " + (int)ex.StatusCode);
            throw;
        }
    }

    private void Record(Container container, string kind, string text, IReadOnlyList<SqlParameterShape> parameters, string partitionLabel, int physical, double charge, TimeSpan elapsed, string outcome)
    {
        // Nothing in here may break the operation it observed
        // (ADR: What the database is actually doing).
        try
        {
            _log.Record(new StoreOperation(
                DateTimeOffset.UtcNow,
                NameOf(container),
                kind,
                text,
                parameters,
                partitionLabel,
                physical,
                Math.Round(charge, 2),
                (long)elapsed.TotalMilliseconds,
                outcome,
                CurrentRequest.Describe()));
        }
        catch
        {
            // Deliberately silent, for the reason the SQL interceptor gives.
        }
    }
    // #endregion operations
}

/// <summary>A point read's answer with its cost, for a caller that wants the number and not only the document.</summary>
public sealed record MeasuredItem<T>(T? Item, double Charge, long DurationMs) where T : class;

/// <summary>A query's answer with its cost: every page's charge added up, the page count, and the wall clock.</summary>
public sealed record MeasuredQuery<T>(IReadOnlyList<T> Items, double Charge, int Pages, long DurationMs);

/// <summary>What one response cost, in request units and as a status code, read off whichever response type the SDK answered with.</summary>
public readonly record struct Cost(double Charge, int Status);

public static class Costs
{
    public static Cost Cost<T>(this Response<T> response) => new(response.RequestCharge, (int)response.StatusCode);

    public static Cost Cost(this TransactionalBatchResponse response) => new(response.RequestCharge, (int)response.StatusCode);
}

/// <summary>What the first boot found and paid, so the log line and the comparison card can say it.</summary>
public sealed record CosmosSeedResult(int VehiclesInserted, int PhotosInserted, int VehiclesTotal, int PhotosTotal, double SeedCharge);

/// <summary>The startup numbers the Admin tab's comparison card shows: how long the containers took to check, how long the seed took and what it cost.</summary>
public sealed record StartupCost(long CheckMs, long SeedMs, double SeedRequestUnits, int VehiclesSeeded, int PhotosSeeded);
