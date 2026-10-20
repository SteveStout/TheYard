namespace TheYard.Tests;

// #region shared-bid-store
/// <summary>
/// The test classes that write bids, buy outright or run the room's round, which run one after
/// another instead of side by side. Booted on Azure Cosmos DB every test host shares one database,
/// and a round of the room or a bid in one class raises the minimum another class has just read, so
/// its bid at that minimum is refused (1.0.3.191, 1.0.3.192 and 1.0.3.194's first gates on Cosmos
/// DB). On SQLite each host has its own file, and the order changes nothing.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SharedBidStore
{
    /// <summary>The collection's name.</summary>
    public const string Name = "The shared bid store";
}
// #endregion shared-bid-store
