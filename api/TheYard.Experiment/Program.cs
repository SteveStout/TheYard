using System.Diagnostics;
using Azure.Identity;
using Microsoft.Azure.Cosmos;
using TheYard.Application;
using TheYard.Infrastructure;
using TheYard.Infrastructure.Cosmos;

// The partition key experiment (ADR: The partition key).
//
//   dotnet run --project api/TheYard.Experiment -- seed  --container catalogue
//   dotnet run --project api/TheYard.Experiment -- query --container catalogue --rounds 10
//
// Seed writes the 100,000 expanded vehicles, the same ones the site serves from
// memory, into one container, in bulk, and prints what it cost in request
// units and minutes. Query runs the set of queries the record names, in paired
// rounds, and prints the median request charge and milliseconds of each. Both
// run as the signed-in Azure CLI principal against the real account: there is
// no key, and this is not something a public endpoint should be able to start.

string endpoint = Environment.GetEnvironmentVariable("Cosmos__AccountEndpoint") ?? "https://cosmos-theyard-ss.documents.azure.com:443/";
string database = Environment.GetEnvironmentVariable("Cosmos__Database") ?? "theyard";
string command = args.Length > 0 ? args[0] : "query";
string container = Option("--container") ?? "catalogue";
int rounds = int.Parse(Option("--rounds") ?? "10");
int count = int.Parse(Option("--count") ?? "100000");
string dataPath = Option("--data") ?? FindUpward(Directory.GetCurrentDirectory(), Path.Combine("data", "vehicles.json"));

return command switch
{
    "seed" => await SeedAsync(),
    "query" => await QueryAsync(),
    _ => Usage(),
};

string? Option(string name)
{
    int at = Array.IndexOf(args, name);
    return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
}

static int Usage()
{
    Console.Error.WriteLine("usage: seed --container <name> [--count N] | query --container <name> [--rounds N]");
    return 2;
}

// #region seed
async Task<int> SeedAsync()
{
    // A client of its own with bulk execution on: the SDK batches concurrent
    // point writes into fewer, larger requests per partition, which is the
    // only way 100,000 documents go in at a sensible pace. The store's client
    // is not bulk, because a bid is one document and bulk mode would hold it
    // back waiting for company.
    using var client = new CosmosClient(endpoint, new AzureCliCredential(), new CosmosClientOptions
    {
        ApplicationName = "TheYard.Experiment",
        UseSystemTextJsonSerializerWithOptions = CosmosStore.Json,
        AllowBulkExecution = true,
        MaxRetryAttemptsOnRateLimitedRequests = 30,
        MaxRetryWaitTimeOnRateLimitedRequests = TimeSpan.FromSeconds(120),
    });
    var target = client.GetDatabase(database).GetContainer(container);
    var properties = await target.ReadContainerAsync();
    int indexed = properties.Resource.IndexingPolicy.IncludedPaths.Count;
    Console.WriteLine($"container {container}: partition key {properties.Resource.PartitionKeyPath}, {indexed} included index path(s)");

    var existing = target.GetItemQueryIterator<int>(new QueryDefinition("SELECT VALUE COUNT(1) FROM c"));
    int already = (await existing.ReadNextAsync()).First();
    if (already > 0)
    {
        Console.WriteLine($"{container} already holds {already} documents; not seeding twice. Delete and recreate the container to measure again.");
        return 1;
    }

    var vehicles = await new SyntheticVehicleSource(new JsonFileVehicleSource(dataPath), count).LoadAsync();
    Console.WriteLine($"seeding {vehicles.Count} documents at {DateTime.Now:HH:mm:ss}");
    var clock = Stopwatch.StartNew();
    double charge = 0;
    int done = 0;
    int failed = 0;
    const int window = 1000;
    for (int start = 0; start < vehicles.Count; start += window)
    {
        var slice = vehicles.Skip(start).Take(window).Select((vehicle, offset) => vehicle.ToDocument(start + offset)).ToList();
        var tasks = slice.Select(document => target.CreateItemAsync(document, new PartitionKey(document.Make))).ToList();
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (CosmosException)
        {
            // Counted below, per task, so one failure does not hide the rest.
        }
        foreach (var task in tasks)
        {
            if (task.IsCompletedSuccessfully)
            {
                charge += task.Result.RequestCharge;
                done++;
            }
            else
            {
                failed++;
            }
        }
        if ((start / window) % 10 == 9)
        {
            Console.WriteLine($"  {done} written, {charge:0} RU, {clock.Elapsed.TotalMinutes:0.0} min, {done / clock.Elapsed.TotalSeconds:0} documents/s");
        }
    }
    clock.Stop();
    Console.WriteLine();
    Console.WriteLine($"seeded {done} documents ({failed} failed) into {container} in {clock.Elapsed.TotalMinutes:0.0} minutes");
    Console.WriteLine($"  {charge:0} RU in total, {charge / Math.Max(done, 1):0.00} RU per document, {done / clock.Elapsed.TotalSeconds:0} documents per second");
    Console.WriteLine($"  at 1000 RU/s the floor for this seed was {charge / 1000 / 60:0.0} minutes; the rest is the client and the round trips");
    return failed == 0 ? 0 : 1;
}
// #endregion seed

// #region query
async Task<int> QueryAsync()
{
    var log = new ListLog();
    var store = CosmosStore.Connect(endpoint, database, "", "azure-cli", "2888a6ca-be1c-46a5-a1de-c666b1d193e5", log);
    var target = store.Vehicles.Database.GetContainer(container);
    var ranges = await target.GetFeedRangesAsync();
    Console.WriteLine($"container {container}: {ranges.Count} physical partition(s)");

    // One known document, read the cheap way and the expensive way.
    var sample = await store.QueryAsync<VehicleDocument>(target, new QueryDefinition("SELECT TOP 1 * FROM c WHERE c.make = 'Ford'"), "Ford", "pinned to the make");
    if (sample.Count == 0)
    {
        Console.Error.WriteLine($"{container} has no Ford in it; seed first");
        return 1;
    }
    string id = sample[0].Id;

    var queries = new (string Name, string? Partition, QueryDefinition Query)[]
    {
        ("make = Ford, by price, page of 100 (pinned)", "Ford",
            new QueryDefinition("SELECT TOP 100 * FROM c WHERE c.make = @make ORDER BY c.starting_bid").WithParameter("@make", "Ford")),
        ("province = Ontario, by price, page of 100 (cross-partition)", null,
            new QueryDefinition("SELECT TOP 100 * FROM c WHERE c.province = @province ORDER BY c.starting_bid").WithParameter("@province", "Ontario")),
        ("no filter, by price, page of 100 (cross-partition)", null,
            new QueryDefinition("SELECT TOP 100 * FROM c ORDER BY c.starting_bid")),
        ("SUV, clean title, grade 4+, page of 100 (cross-partition)", null,
            new QueryDefinition("SELECT TOP 100 * FROM c WHERE c.body_style = 'SUV' AND c.title_status = 'clean' AND c.condition_grade >= 4")),
        ("count of Ford (pinned aggregate)", "Ford",
            new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE c.make = @make").WithParameter("@make", "Ford")),
        ("id only, no make (cross-partition lookup)", null,
            new QueryDefinition("SELECT * FROM c WHERE c.id = @id").WithParameter("@id", id)),
        ("free text CONTAINS on model (cross-partition scan)", null,
            new QueryDefinition("SELECT TOP 100 * FROM c WHERE CONTAINS(LOWER(c.model), 'cx-5')")),
    };

    var charges = queries.ToDictionary(q => q.Name, _ => new List<double>());
    var times = queries.ToDictionary(q => q.Name, _ => new List<long>());
    var pointCharges = new List<double>();
    var pointTimes = new List<long>();

    for (int round = 0; round < rounds; round++)
    {
        foreach (var (name, partition, query) in queries)
        {
            log.Operations.Clear();
            var clock = Stopwatch.StartNew();
            if (name.StartsWith("count", StringComparison.Ordinal))
            {
                await store.QueryAsync<int>(target, query, partition, partition is null ? "cross-partition" : "pinned to the make");
            }
            else
            {
                await store.QueryAsync<VehicleDocument>(target, query, partition, partition is null ? "cross-partition" : "pinned to the make");
            }
            clock.Stop();
            charges[name].Add(log.Operations.Sum(o => o.RequestCharge));
            times[name].Add(clock.ElapsedMilliseconds);
        }
        log.Operations.Clear();
        var point = Stopwatch.StartNew();
        await store.ReadAsync<VehicleDocument>(target, id, "Ford", "pinned to the make");
        point.Stop();
        pointCharges.Add(log.Operations.Sum(o => o.RequestCharge));
        pointTimes.Add(point.ElapsedMilliseconds);
        Console.Error.WriteLine($"round {round + 1} of {rounds}");
    }

    Console.WriteLine();
    Console.WriteLine($"| query on {container} | partitions | RU (median) | ms (median) |");
    Console.WriteLine("| --- | --- | --- | --- |");
    Console.WriteLine($"| point read, id and make known | 1 logical | {Median(pointCharges):0.00} | {Median(pointTimes.Select(t => (double)t)):0} |");
    foreach (var (name, partition, _) in queries)
    {
        string scope = partition is null ? $"all ({ranges.Count} physical)" : "1 logical";
        Console.WriteLine($"| {name} | {scope} | {Median(charges[name]):0.00} | {Median(times[name].Select(t => (double)t)):0} |");
    }
    return 0;
}
// #endregion query

static double Median(IEnumerable<double> values)
{
    var ordered = values.OrderBy(v => v).ToArray();
    return ordered.Length == 0 ? 0 : ordered[(int)Math.Ceiling(ordered.Length * 0.5) - 1];
}

static string FindUpward(string startDirectory, string relativePath)
{
    for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
    {
        string candidate = Path.Combine(dir.FullName, relativePath);
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }
    throw new FileNotFoundException($"Could not locate {relativePath} in or above {startDirectory}");
}

/// <summary>A store log that keeps everything, so a round can add up what it cost.</summary>
sealed class ListLog : IStoreLog
{
    public List<StoreOperation> Operations { get; } = [];

    public void Record(StoreOperation operation) => Operations.Add(operation);
}
