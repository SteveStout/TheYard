// The one connection to Azure Cosmos DB. This file holds what the store is made of: its fields,
// its containers and the settings the host attaches. Every read, write and query, and the log line
// each one writes, is CosmosStore.Operations.cs (regions operations, record). Two jobs are classes
// of their own, because the store over three hundred lines was more than one subject (ADR-088):
//   CosmosConnection.cs     the client and the credential (region connect)
//   CosmosStoreStartup.cs   the container check, the seed and the health probe
//                           (regions prepare, seed, probe)
// The small result types live one per file: MeasuredItem.cs, MeasuredQuery.cs, Cost.cs, Costs.cs,
// CosmosSeedResult.cs and StartupCost.cs.
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using TheYard.Application.Shared.Stores;

namespace TheYard.Infrastructure.Cosmos.Shared.Stores;

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

    /// <summary>How many physical partitions each container had at startup, by catalog name.</summary>
    private readonly Dictionary<string, int> _physicalPartitions = new(StringComparer.Ordinal);

    /// <summary>The wire shape: snake case, like the dataset and the API.</summary>
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Wrap a client that is already built. <see cref="CosmosConnection.Connect"/> builds one
    /// from settings; a test can hand in its own.
    /// </summary>
    public CosmosStore(CosmosClient client, string databaseName, string containerPrefix, IStoreLog log)
    {
        _client = client;
        _database = client.GetDatabase(databaseName);
        _prefix = containerPrefix;
        Recorder = new CosmosOperationLog(log, containerPrefix);
        Starting = new CosmosStoreStartup(this);
    }

    /// <summary>The wrapper every operation goes through, and what it writes (CosmosOperationLog.cs).</summary>
    public CosmosOperationLog Recorder { get; }

    /// <summary>The container check, the seed and the health probe (CosmosStoreStartup.cs).</summary>
    public CosmosStoreStartup Starting { get; }


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
    public StartupCost Startup { get; internal set; } = new(0, 0, 0, 0, 0);

    /// <summary>How many physical partitions a container has, read once at startup. One, at this size (ADR: The partition key).</summary>
    public int PhysicalPartitionsOf(string containerName) =>
        _physicalPartitions.TryGetValue(containerName, out int count) ? count : 1;

    /// <summary>Keeps how many physical partitions a container has; the startup check reads it once.</summary>
    internal void KeepPhysicalPartitions(string containerName, int count) => _physicalPartitions[containerName] = count;
}
