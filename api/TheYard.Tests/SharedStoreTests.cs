using System.Text.RegularExpressions;

namespace TheYard.Tests;

// #region shared-store-tests
/// <summary>
/// Every test class that writes bids through the booted host is in the shared bid store's
/// collection (SharedBidStore). The three gates that went red on 20 October each found one more
/// class writing beside the others on the shared Cosmos DB test database, and the review of 1.0.4.0
/// found a fourth (ProofTests) by reading; this reads for them, so the next one fails here, in two
/// seconds, instead of in a fifteen-minute gate. It reads class by class, so a second class in a
/// file whose first is in the collection is read on its own, and any verb on /api/bids counts.
/// </summary>
public sealed partial class SharedStoreTests
{
    /// <summary>A call that writes a bid, a buy, a reset, a round or a proof through the host, on any verb.</summary>
    [GeneratedRegex("""(?:Post|Put|Patch|Delete|Send)(?:AsJsonAsync|Async)\((?:[^;]*?)"/api/(?:vehicles/[^"]*/(?:bids|buy-now)|bids"|market/tick|admin/proof)|HttpMethod\.(?:Post|Delete|Put|Patch), "/api/(?:vehicles/[^"]*/(?:bids|buy-now)|bids"|market/tick|admin/proof)""")]
    private static partial Regex Writes();

    /// <summary>A class declaration with the comment and attributes above it: its name and the class it derives from.</summary>
    [GeneratedRegex(@"(?m)^(?:///[^\n]*\n)*(?:\[[^\n]*\]\s*\n)*(?:public |internal )?(?<shape>(?:sealed |static |abstract )*)(?:partial )?class (?<name>\w+)(?:\([^)]*\))?(?:\s*:\s*(?<base>\w+))?")]
    private static partial Regex Declaration();

    /// <summary>Each class in the test project: whether it writes, boots the host, is in the collection or says it keeps nothing, read from every part of it.</summary>
    private static Dictionary<string, (bool Writes, bool Host, bool Collected, bool KeepsNothing, bool Abstract, string? Base, string File)> Classes()
    {
        var classes = new Dictionary<string, (bool Writes, bool Host, bool Collected, bool KeepsNothing, bool Abstract, string? Base, string File)>(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(Path.Combine(Repo.Root(), "api", "TheYard.Tests"), "*.cs", SearchOption.TopDirectoryOnly))
        {
            string source = File.ReadAllText(path);
            var found = Declaration().Matches(source);
            for (int index = 0; index < found.Count; index++)
            {
                var match = found[index];
                string part = source[match.Index..(index + 1 < found.Count ? found[index + 1].Index : source.Length)];
                string name = match.Groups["name"].Value;
                var known = classes.GetValueOrDefault(name);
                classes[name] = (
                    known.Writes || Writes().IsMatch(part),
                    known.Host || part.Contains("WebApplicationFactory<Program>", StringComparison.Ordinal),
                    known.Collected || part.Contains("[Collection(SharedBidStore.Name)]", StringComparison.Ordinal),
                    known.KeepsNothing || part.Contains("writes nothing the store keeps", StringComparison.Ordinal),
                    known.Abstract || match.Groups["shape"].Value.Contains("abstract", StringComparison.Ordinal),
                    known.Base ?? (match.Groups["base"].Success ? match.Groups["base"].Value : null),
                    known.File ?? Path.GetFileName(path));
            }
        }
        return classes;
    }

    [Fact]
    public void Every_test_class_that_writes_bids_through_the_host_is_in_the_shared_store_collection()
    {
        var classes = Classes();
        Assert.True(classes.Count > 100, $"only {classes.Count} classes were read, so this is reading the wrong folder");
        // A class boots the host when it or the class it derives from does.
        bool Host(string name) => classes.TryGetValue(name, out var c) && (c.Host || (c.Base is { } b && b != name && Host(b)));
        var outside = classes
            .Where(c => c.Value.Writes && !c.Value.Abstract && Host(c.Key) && !c.Value.Collected && !c.Value.KeepsNothing)
            .Select(c => $"{c.Key} ({c.Value.File})")
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(outside.Count == 0, "these classes write bids through the host outside the shared store's collection: " + string.Join(", ", outside));
    }

    /// <summary>The rule reads a DELETE of the bids as a write, and a second class in a collected file on its own.</summary>
    [Fact]
    public void A_delete_of_the_bids_is_a_write_and_each_class_is_read_on_its_own()
    {
        Assert.Matches(Writes(), """await first.DeleteAsync("/api/bids");""");
        Assert.Matches(Writes(), """new HttpRequestMessage(HttpMethod.Delete, "/api/bids");""");
        Assert.DoesNotMatch(Writes(), """await client.GetStringAsync("/api/bids")""");
        var classes = Classes();
        Assert.True(classes["AuthBidTests"].Collected);
        Assert.False(classes["AuthTests"].Collected);
        Assert.Equal("AuthTestBase", classes["AuthTests"].Base);
    }
}
// #endregion shared-store-tests
