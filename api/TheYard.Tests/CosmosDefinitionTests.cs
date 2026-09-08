using System.Text.Json;
using TheYard.Infrastructure;
using TheYard.Infrastructure.Cosmos;

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
        foreach (var (name, definition) in Definitions())
        {
            var policy = definition.GetProperty("indexingPolicy");
            Assert.Equal("consistent", policy.GetProperty("indexingMode").GetString());
            Assert.Equal(0, policy.GetProperty("includedPaths").GetArrayLength());
            var excluded = policy.GetProperty("excludedPaths").EnumerateArray().Select(p => p.GetProperty("path").GetString()).ToArray();
            Assert.Contains("/*", excluded);
        }
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
        var source = await new JsonFileVehicleSource(Repo.DataFile("vehicles.json")).LoadAsync();
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
        var vehicles = await new JsonFileVehicleSource(Repo.DataFile("vehicles.json")).LoadAsync();
        var manifest = await new JsonFilePhotoManifestSource(
            Path.Combine(Repo.Root(), "api", "TheYard.Api", "photo-manifest.json")).LoadAsync();
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
}
