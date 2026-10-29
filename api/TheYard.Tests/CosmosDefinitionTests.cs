using System.Text.Json;
using TheYard.Infrastructure;

namespace TheYard.Tests;

/// <summary>
/// The document store's shape, asserted without a connection, the way the SQL
/// Server schema is asserted without a server (ADR: The SQL Server backend).
/// The container definitions under infra/cosmos are the authority; the code's
/// catalog has to agree with them; and the documents have to carry every field
/// the domain carries except the one the record says they drop.
/// </summary>
public class CosmosDefinitionTests
{
    private static string DefinitionsRoot() => Path.Combine(Repo.Root(), "infra", "cosmos");

    private static IReadOnlyDictionary<string, JsonElement> Definitions()
    {
        var files = Directory.GetFiles(DefinitionsRoot(), "*.json");
        Assert.NotEmpty(files);
        return files.ToDictionary(
            file => JsonDocument.Parse(File.ReadAllText(file)).RootElement.GetProperty("container").GetString()!,
            file => JsonDocument.Parse(File.ReadAllText(file)).RootElement.Clone(),
            StringComparer.Ordinal);
    }

    // #region conformance
    [Fact]
    public void Every_container_the_code_uses_is_defined_with_the_partition_key_the_code_was_written_for()
    {
        var definitions = Definitions();
        foreach (var (name, path) in Containers.PartitionKeyPaths)
        {
            Assert.True(definitions.ContainsKey(name), $"infra/cosmos has no definition for the {name} container");
            Assert.Equal(path, definitions[name].GetProperty("partitionKeyPath").GetString());
        }
    }

    /// <summary>
    /// The live tests' copies of the store's containers, each under "tests-", as the account lists
    /// them (az cosmosdb sql container list, read 22 October). They share the database's throughput
    /// with the site's own, so they count against the same limit.
    /// </summary>
    private static readonly string[] LiveTestCopies = ["vehicles", "photos", "bids", "users", "activity", "logs", "resets", "machines"];

    /// <summary>The most containers a database with shared throughput holds: Cosmos DB refuses the twenty-sixth ("collection count in database offer has exceeded 25").</summary>
    private const int SharedDatabaseLimit = 25;

    [Fact]
    public void The_shared_database_never_needs_a_twenty_sixth_container()
    {
        // A database with shared throughput refuses its twenty-sixth container, and the nightly card's
        // two waited two days on a create that could never succeed. A new keep shares a container under
        // kinds of its own (Containers.NightlyPrefix) before it asks for one.
        int needed = Definitions().Count + LiveTestCopies.Length;
        Assert.True(needed <= SharedDatabaseLimit, $"infra/cosmos defines {Definitions().Count} containers and the live tests copy {LiveTestCopies.Length}: {needed}, over the database's {SharedDatabaseLimit}");
        Assert.All(LiveTestCopies, name => Assert.True(Containers.PartitionKeyPaths.ContainsKey(name), $"tests-{name} copies no container the code defines"));
    }

    [Fact]
    public void Every_definition_is_a_container_the_code_uses()
    {
        foreach (string name in Definitions().Keys)
        {
            Assert.True(Containers.PartitionKeyPaths.ContainsKey(name), $"infra/cosmos/{name}.json defines a container nothing reads");
        }
    }

    [Fact]
    public void The_parity_containers_index_nothing()
    {
        // The single biggest cost lever on this workload: the default policy
        // indexes every path in every document and charges for it on every
        // write, and nothing here queries by a property (ADR: The partition key).
        // The experiment containers are the exception and are checked next.
        foreach (var (name, definition) in Definitions().Where(entry => Containers.Required.Contains(entry.Key)))
        {
            var policy = definition.GetProperty("indexingPolicy");
            Assert.Equal("consistent", policy.GetProperty("indexingMode").GetString());
            Assert.Equal(0, policy.GetProperty("includedPaths").GetArrayLength());
            var excluded = policy.GetProperty("excludedPaths").EnumerateArray().Select(p => p.GetProperty("path").GetString()).ToArray();
            Assert.Contains("/*", excluded);
        }
    }
    [Fact]
    public void The_experiment_indexes_the_paths_the_queries_use_and_the_control_indexes_everything()
    {
        var definitions = Definitions();
        var tuned = definitions[Containers.Catalogue].GetProperty("indexingPolicy");
        var included = tuned.GetProperty("includedPaths").EnumerateArray().Select(p => p.GetProperty("path").GetString()).ToArray();
        foreach (string path in new[] { "/make/?", "/body_style/?", "/title_status/?", "/province/?", "/starting_bid/?", "/condition_grade/?" })
        {
            Assert.Contains(path, included);
        }
        var control = definitions[Containers.CatalogueDefault].GetProperty("indexingPolicy");
        Assert.Contains("/*", control.GetProperty("includedPaths").EnumerateArray().Select(p => p.GetProperty("path").GetString()));
    }
    // #endregion conformance

    // #region documents
    [Fact]
    public void A_partition_key_path_names_a_field_the_document_actually_has()
    {
        // The definition says /make and /user_id; the serializer decides what
        // the document calls its fields. If those ever disagree, every write is
        // refused for naming a partition key value the document does not carry.
        string vehicle = JsonSerializer.Serialize(TestData.Vehicle().ToDocument(0), CosmosStore.Json);
        string bid = JsonSerializer.Serialize(new BidDocument { Id = "v", UserId = "u" }, CosmosStore.Json);
        string photo = JsonSerializer.Serialize(new PhotoDocument { Id = "f", Style = "suv" }, CosmosStore.Json);
        string user = JsonSerializer.Serialize(new UserDocument { Id = "u" }, CosmosStore.Json);

        Assert.Contains("\"make\":", vehicle);
        Assert.Contains("\"user_id\":", bid);
        Assert.Contains("\"style\":", photo);
        Assert.Contains("\"id\":", user);
        // And the etag is the store's name for it, not the serializer's.
        Assert.DoesNotContain("e_tag", vehicle);
    }

    [Fact]
    public async Task Every_field_but_images_survives_the_round_trip_through_a_document()
    {
        var source = await new JsonFileVehicleSource(Repo.DataFile("vehicles.json")).LoadAsync(CancellationToken.None);
        var roundTripped = source.Select((vehicle, index) =>
        {
            string json = JsonSerializer.Serialize(vehicle.ToDocument(index), CosmosStore.Json);
            return JsonSerializer.Deserialize<VehicleDocument>(json, CosmosStore.Json)!.ToVehicle();
        }).ToList();

        Assert.Equal(source.Count, roundTripped.Count);
        // The lists by sequence and the rest by the record, for the reason the
        // relational round trip gives: a list's Equals is reference equality.
        IReadOnlyList<string> none = Array.Empty<string>();
        for (int i = 0; i < source.Count; i++)
        {
            Assert.Equal(source[i].DamageNotes, roundTripped[i].DamageNotes);
            Assert.Empty(roundTripped[i].Images);
            Assert.Equal(
                source[i] with { DamageNotes = none, Images = none },
                roundTripped[i] with { DamageNotes = none, Images = none });
        }
    }

    [Fact]
    public async Task Every_body_style_in_the_seed_has_a_photo_pool_which_is_why_images_are_not_stored()
    {
        // The precondition for dropping the field. The day a style arrives with
        // no pool, the relational store would fall back to the dataset's
        // placeholder URLs and the document store to nothing, and this is the
        // test that says so before a visitor does (ADR: A second store on
        // Cosmos DB, and what it costs).
        var vehicles = await new JsonFileVehicleSource(Repo.DataFile("vehicles.json")).LoadAsync(CancellationToken.None);
        var manifest = await new JsonFilePhotoManifestSource(
            Path.Combine(Repo.Root(), "api", "TheYard.Api", "photo-manifest.json")).LoadAsync(CancellationToken.None);
        var pools = manifest.Select(photo => photo.Style.ToLowerInvariant()).ToHashSet();

        foreach (string style in vehicles.Select(v => v.BodyStyle.ToLowerInvariant()).Distinct())
        {
            Assert.Contains(style, pools);
        }
    }

    [Fact]
    public void A_document_is_thirty_per_cent_lighter_without_the_images()
    {
        // The number the record quotes, held by a test rather than remembered.
        var vehicle = TestData.Vehicle(images: ["https://placehold.co/800x600?text=Photo+1", "https://placehold.co/800x600?text=Photo+2", "https://placehold.co/800x600?text=Photo+3"]);
        int withImages = JsonSerializer.Serialize(vehicle, VehicleJson.Options).Length;
        int asStored = JsonSerializer.Serialize(vehicle.ToDocument(0), CosmosStore.Json).Length;
        Assert.True(asStored < withImages * 0.8, $"stored {asStored} bytes against {withImages} with images");
    }
    // #endregion documents

    // #region choose
    [Fact]
    public void The_document_store_is_chosen_by_the_presence_of_an_endpoint_and_a_placeholder_is_not_one()
    {
        Assert.Equal(YardProvider.Cosmos, YardConnection.Choose("https://example.documents.azure.com:443/", "Server=tcp:x", "Data Source=y").Provider);
        Assert.Equal(YardProvider.SqlServer, YardConnection.Choose("__YARD_COSMOS_ENDPOINT__", "Server=tcp:x", "Data Source=y").Provider);
        Assert.Equal(YardProvider.SqlServer, YardConnection.Choose("", "Server=tcp:x", "Data Source=y").Provider);
        Assert.Equal(YardProvider.Sqlite, YardConnection.Choose(null, null, "Data Source=y").Provider);
    }

    [Fact]
    public void What_the_process_says_about_the_document_store_names_the_engine_and_nothing_else()
    {
        var connection = YardConnection.Choose("https://cosmos-theyard-ss.documents.azure.com:443/", null, "Data Source=y");
        Assert.Equal("Azure Cosmos DB", connection.Describe());
        Assert.DoesNotContain("documents.azure.com", connection.Describe());
    }
    // #endregion choose

    // #region catalogue-index
    /// <summary>
    /// The catalogue's queries filter and order on indexed paths only (ADR: The store does the
    /// work): every path a WHERE names is included, and every two-key ORDER BY a page is read in has
    /// the composite index Cosmos DB needs to serve it, in the direction it is read.
    /// </summary>
    [Fact]
    public void The_catalogue_indexes_every_path_its_queries_name_and_every_order_a_page_is_read_in()
    {
        var policy = Definitions()[Containers.Catalogue].GetProperty("indexingPolicy");
        var included = policy.GetProperty("includedPaths").EnumerateArray().Select(p => p.GetProperty("path").GetString()).ToHashSet();
        var composites = policy.GetProperty("compositeIndexes").EnumerateArray()
            .Select(index => string.Join(", ", index.EnumerateArray().Select(key => $"{key.GetProperty("path").GetString()} {key.GetProperty("order").GetString()}")))
            .ToHashSet();

        foreach (string path in new[] { "seq", "price", "start_offset", "end_offset", "search", "condition_grade", "bid_count", "make", "body_style", "title_status", "province" })
        {
            Assert.Contains($"/{path}/?", included);
        }
        foreach (string order in new[] { "/end_offset ascending", "/end_offset descending", "/start_offset ascending", "/price ascending", "/price descending", "/condition_grade descending", "/bid_count descending" })
        {
            Assert.Contains($"{order}, /seq ascending", composites);
        }
    }

    [Fact]
    public void A_catalogue_document_carries_the_fields_its_queries_name_and_comes_back_as_the_vehicle()
    {
        var vehicle = TestData.Vehicle(id: "seed-1-0004d");
        var entry = new TheYard.Application.Shared.Auction.CatalogueEntry(77, vehicle);
        string json = JsonSerializer.Serialize(entry.ToCatalogueDocument(), CosmosStore.Json);
        var back = JsonSerializer.Deserialize<CatalogueDocument>(json, CosmosStore.Json)!.ToEntry();
        var slot = TheYard.Application.Shared.Auction.CatalogueSlot.For(vehicle.Id);

        foreach (string field in new[] { "\"seq\":77", $"\"start_offset\":{slot.StartOffsetMs}", $"\"end_offset\":{slot.EndOffsetMs}", "\"price\":22800", "\"search\":\"2023 ford bronco" })
        {
            Assert.Contains(field, json, StringComparison.Ordinal);
        }
        Assert.Equal(77, back.Seq);
        Assert.Equivalent(vehicle with { Images = [] }, back.Vehicle, strict: true);
    }
    // #endregion catalogue-index
}
