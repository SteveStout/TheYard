// The one connection to Azure Cosmos DB. This file holds what the store is made of: its fields,
// its containers and the settings the host attaches. Each job it does is a part of its own:
//   CosmosStore.Connect.cs      the client and the credential (region connect)
//   CosmosStore.Startup.cs      the container check, the seed and the health probe
//                               (regions prepare, seed, probe)
//   CosmosStore.Operations.cs   every read, write and query, and the log line each one writes
//                               (regions operations, record)
// The small result types live one per file: MeasuredItem.cs, MeasuredQuery.cs, Cost.cs, Costs.cs,
// CosmosSeedResult.cs and StartupCost.cs.
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
public sealed partial class CosmosStore
{
    /// <summary>The SDK client, one per process, holding the connections and the routing map.</summary>
    private readonly CosmosClient _client;

    /// <summary>The database every container of this store lives in.</summary>
    private readonly Database _database;

    /// <summary>What goes in front of every catalog name to make the real container name.</summary>
    private readonly string _prefix;

    /// <summary>The store log every operation's charge and duration is written to.</summary>
    private readonly IStoreLog _log;

    /// <summary>How many physical partitions each container had at startup, by catalog name.</summary>
    private readonly Dictionary<string, int> _physicalPartitions = new(StringComparer.Ordinal);

    /// <summary>The id and make of one seed vehicle, remembered so the probe can point-read it.</summary>
    private (string Id, string Make)? _firstVehicle;

    /// <summary>The id and style of one seed photo, remembered so the probe can point-read it.</summary>
    private (string Id, string Style)? _firstPhoto;

    /// <summary>The wire shape: snake case, like the dataset and the API.</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Wrap a client that is already built. <see cref="Connect"/> builds one
    /// from settings; a test can hand in its own.
    /// </summary>
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

    /// <summary>
    /// The application's logger, set by the host once there is an application,
    /// the way the relational side attaches Entity Framework's command logging
    /// after the container is built. One line per operation at Information,
    /// so the console, and the Admin tab's log card that mirrors it, show the
    /// document store's traffic the way they show every SQL statement
    /// (ADR: What the store is actually doing, addendum). The line carries the
    /// kind, the container, the charge, the time, the partition described and
    /// the query shape; never a value, for the reason the store log gives.
    /// Silent until attached.
    /// </summary>
    public ILogger Logger { get; set; } = NullLogger.Instance;

    /// <summary>Any container of this database by its catalog name, prefixed like the rest. The experiment reads the catalogue through this.</summary>
    public Container ContainerNamed(string name) => _database.GetContainer(_prefix + name);

    /// <summary>The seed vehicles, partitioned on the make.</summary>
    public Container Vehicles => _database.GetContainer(_prefix + Containers.Vehicles);

    /// <summary>The photo manifest, partitioned on the body style.</summary>
    public Container Photos => _database.GetContainer(_prefix + Containers.Photos);

    /// <summary>Every buyer's standing on every vehicle, partitioned on the buyer.</summary>
    public Container Bids => _database.GetContainer(_prefix + Containers.Bids);

    /// <summary>The accounts and the email claims, partitioned on the document id.</summary>
    public Container Users => _database.GetContainer(_prefix + Containers.Users);

    /// <summary>What this process may say about its store: the engine, and nothing else.</summary>
    public string Describe() => "Azure Cosmos DB";

    /// <summary>How the seed and the cold start paid, for the Admin tab's comparison card.</summary>
    public StartupCost Startup { get; private set; } = new(0, 0, 0, 0, 0);

    /// <summary>How many physical partitions a container has, read once at startup. One, at this size (ADR: The partition key).</summary>
    public int PhysicalPartitionsOf(string containerName) =>
        _physicalPartitions.TryGetValue(containerName, out int count) ? count : 1;

    /// <summary>A container's catalog name: its real name with the prefix taken off, which is the name the store log shows.</summary>
    private string NameOf(Container container) =>
        container.Id.StartsWith(_prefix, StringComparison.Ordinal) ? container.Id[_prefix.Length..] : container.Id;
}
