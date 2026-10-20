namespace TheYard.Tests;

// #region shared-bid-store
/// <summary>
/// The test classes that write bids, buy outright, run the room's round or run the proof, which
/// run one after another instead of side by side. Booted on Azure Cosmos DB every test host shares
/// one database, and a bid, a buy or a reset in one class moves the standing another class has
/// just read, so its bid at that minimum is refused (1.0.3.191, 1.0.3.192 and 1.0.3.194's first
/// gates on Cosmos DB). The room's own raises stay in each host's memory and cross nothing; its
/// round is in the collection because the classes that post it also bid. On SQLite each host has
/// its own file, and the order changes nothing. SharedStoreTests holds every class that writes to
/// the collection, so a new writer joins it in the commit that writes.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SharedBidStore
{
    /// <summary>The collection's name.</summary>
    public const string Name = "The shared bid store";
}
// #endregion shared-bid-store
