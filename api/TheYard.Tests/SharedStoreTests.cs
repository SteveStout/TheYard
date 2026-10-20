using System.Text.RegularExpressions;

namespace TheYard.Tests;

// #region shared-store-tests
/// <summary>
/// Every test class that writes bids through the booted host is in the shared bid store's
/// collection (SharedBidStore). The three gates that went red on 20 October each found one more
/// class writing beside the others on the shared Cosmos DB test database, and the review of 1.0.4.0
/// found a fourth (ProofTests) by reading; this reads for them, so the next one fails here, in two
/// seconds, instead of in a fifteen-minute gate.
/// </summary>
public sealed class SharedStoreTests
{
    /// <summary>A call that writes a bid, a buy, a reset, a round or a proof through the host.</summary>
    private static readonly Regex Writes = new(
        """Post(?:AsJsonAsync|Async)\((?:[^;]*?)"/api/(?:vehicles/[^"]*/(?:bids|buy-now)|market/tick|admin/proof)|HttpMethod\.(?:Post|Delete), "/api/(?:vehicles/[^"]*/(?:bids|buy-now)|bids"|market/tick|admin/proof)""",
        RegexOptions.Compiled);

    [Fact]
    public void Every_test_class_that_writes_bids_through_the_host_is_in_the_shared_store_collection()
    {
        var outside = new List<string>();
        foreach (string path in Directory.EnumerateFiles(Path.Combine(Repo.Root(), "api", "TheYard.Tests"), "*.cs", SearchOption.TopDirectoryOnly))
        {
            string source = File.ReadAllText(path);
            if (!Writes.IsMatch(source) || !source.Contains("WebApplicationFactory<Program>", StringComparison.Ordinal))
            {
                continue;
            }
            // A class whose every write is refused before it lands (an anonymous bid, a bid of one
            // dollar) says so on the class with this sentence, and stays out.
            if (source.Contains("writes nothing the store keeps", StringComparison.Ordinal))
            {
                continue;
            }
            if (!source.Contains("[Collection(SharedBidStore.Name)]", StringComparison.Ordinal))
            {
                outside.Add(Path.GetFileName(path));
            }
        }
        Assert.True(outside.Count == 0, "these classes write bids through the host outside the shared store's collection: " + string.Join(", ", outside));
    }
}
// #endregion shared-store-tests
