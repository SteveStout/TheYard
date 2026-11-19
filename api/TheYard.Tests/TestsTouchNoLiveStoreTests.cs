namespace TheYard.Tests;

/// <summary>
/// Automated tests never touch a real database or permanent storage (ADR: Automated tests never touch a real database or
/// permanent storage): the one fixture that opens a Cosmos DB store refuses any address that is not this machine, no other
/// test opens one but the listed exception, and every share or blob client a test builds rides a fake transport.
/// </summary>
public sealed class TestsTouchNoLiveStoreTests
{
    /// <summary>The test suites' folders, each read for what its files open.</summary>
    private static readonly string[] Suites = ["api/TheYard.Tests", "functions/jobhunt.Tests", "server/JobHunt.Server.Tests"];

    /// <summary>
    /// The files that may open a Cosmos DB store, each with why. Nothing is added here: a new one is a defect, and the
    /// exception below has its plan in the record.
    /// </summary>
    private static readonly Dictionary<string, string> MayOpenCosmos = new(StringComparer.Ordinal)
    {
        ["CosmosStoreTests.cs"] = "CosmosLive, the fixture every store test shares, which refuses any address that is not this machine",
        ["CosmosKeysetTests.cs"] = "the listed exception: one read of the live catalogue's request units, which the emulator does not report; moves to the emulator by the record's plan",
    };

    private static IEnumerable<(string Name, string Text)> TestFiles() =>
        Suites.Select(suite => Path.Combine(Repo.Root(), suite))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => Path.GetFileName(file) != nameof(TestsTouchNoLiveStoreTests) + ".cs")
            .Select(file => (Path.GetFileName(file), File.ReadAllText(file)));

    [Fact]
    public void The_store_tests_refuse_any_address_that_is_not_this_machine()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => CosmosLive.Checked("https://cosmos-theyard-ss.documents.azure.com:443/"));
        Assert.Contains("not this machine", refused.Message, StringComparison.Ordinal);
        Assert.Equal("http://localhost:8081/", CosmosLive.Checked("http://localhost:8081/"));
        Assert.Equal("https://127.0.0.1:8081/", CosmosLive.Checked("https://127.0.0.1:8081/"));
    }

    [Fact]
    public void No_test_opens_a_cosmos_store_but_the_guarded_fixture_and_the_listed_exception()
    {
        var opening = TestFiles()
            .Where(file => file.Text.Contains("CosmosConnection.Connect(", StringComparison.Ordinal) || file.Text.Contains("new CosmosClient(", StringComparison.Ordinal))
            .Select(file => file.Name)
            .ToList();
        Assert.All(opening, name => Assert.True(MayOpenCosmos.ContainsKey(name), $"{name} opens a Cosmos DB store; a test uses a fake of the port, or CosmosLive on the emulator"));
        Assert.Contains("IsLoopback", TestFiles().Single(file => file.Name == "CosmosStoreTests.cs").Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_share_or_blob_client_a_test_builds_rides_a_fake_transport()
    {
        string[] clients = ["new ShareClient(", "new ShareServiceClient(", "new ShareDirectoryClient(", "new ShareFileClient(", "new BlobServiceClient(", "new BlobContainerClient(", "new BlobClient("];
        var bare = TestFiles()
            .Where(file => clients.Any(client => file.Text.Contains(client, StringComparison.Ordinal)) && !file.Text.Contains("Transport", StringComparison.Ordinal))
            .Select(file => file.Name)
            .ToList();
        Assert.True(bare.Count == 0, $"a storage client with no fake transport: {string.Join(", ", bare)}");
    }
}
