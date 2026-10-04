// One store and everything that stands on it, and the bundle that attaches a store to it.
// Its own file because the rest of the API reads a backend through this one shape, so a
// reader looking for what a backend holds finds it here and nothing else.
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TheYard.Application;
using TheYard.Infrastructure;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Api;

// #region backend
/// <summary>
/// One store and everything that stands on it: the catalogue loaded from it,
/// the bids replayed out of it, the room bidding against those, the accounts
/// kept in it, and the numbers from bringing it up (ADR: One container, both
/// stores). A container holds one of these per store it is configured for,
/// and a request is served by exactly one of them.
///
/// <para>Nothing in here is shared between stores. Two catalogues cost twice
/// the memory, which the record prices, and buy the one thing the comparison
/// needs: a listing served by the Cosmos DB backend was loaded from Cosmos DB,
/// and the cold start beside it is that store's own.</para>
/// </summary>
public sealed class Backend
{
    /// <summary>The short name the header and the bar use: "sql" or "cosmos".</summary>
    public required string Key { get; init; }

    /// <summary>The store as it describes itself: "Azure SQL Database", "SQLite", "Azure Cosmos DB".</summary>
    public required string Name { get; init; }

    /// <summary>Whether the store came up, and why not if it did not; replaced when the store is attached.</summary>
    public required DatabaseState Database { get; set; }

    /// <summary>How long each step of bringing this store up took, for the Admin tab's startup card.</summary>
    public required StartupTimings Startup { get; init; }

    /// <summary>The catalogue this backend serves: the files until the store is attached, the store after.</summary>
    public required InventoryService Inventory { get; set; }

    /// <summary>The bids on this backend's catalogue, written through to the store once it is attached.</summary>
    public required BidService Bids { get; set; }

    /// <summary>The simulated competing bidders on this backend's catalogue, the room the buyer bids in.</summary>
    public required MarketService Market { get; init; }

    /// <summary>The document store, on the Cosmos DB backend only.</summary>
    public CosmosStore? Cosmos { get; init; }

    /// <summary>The relational contexts, on the SQL backend only, and only once it has come up.</summary>
    public IDbContextFactory<YardDbContext>? Contexts { get; set; }

    /// <summary>The same database without the statement interceptor, for the readings a minute that must not fill the SQL log with themselves.</summary>
    public IDbContextFactory<YardDbContext>? QuietContexts { get; set; }

    /// <summary>
    /// Where this store keeps the requests it served, for the Admin tab's
    /// activity card (ADR: Site activity, and the line an address does not
    /// cross). The null store when the backend did not come up: nothing kept,
    /// and the card says so.
    /// </summary>
    public IActivityStore Activity { get; set; } = NullActivityStore.Instance;

    /// <summary>Is the seed catalogue in the store right now. Two reads, timed by the health check.</summary>
    public required Func<Task<bool>> Probe { get; set; }

    /// <summary>
    /// Identity's store over this backend's accounts, built per request from
    /// the request's scope, or null when the store did not come up and there
    /// are no accounts to keep.
    /// </summary>
    public required Func<IServiceProvider, IUserStore<YardUser>?> UserStore { get; set; }

    /// <summary>
    /// The relational store's own reading of itself for the Machines card, read by the adapter
    /// that owns the database, or an absent reading with its reason on a store that keeps none.
    /// </summary>
    public Task<StoreLoad> ReadLoadAsync(int rows, CancellationToken cancellation, ILogger? logger = null) =>
        ResourceStats.ReadAsync(Contexts, Name, rows, cancellation, logger);

    /// <summary>Whether this backend's catalogue is in memory right now, for the Admin tab's memory reading.</summary>
    public bool CatalogueLoaded => Inventory.IsWarm;

    /// <summary>True on the document store, which logs each operation with its request charge.</summary>
    public bool LogsOperations => Cosmos is not null;

    /// <summary>True on an attached relational store, which logs each statement it runs.</summary>
    public bool LogsStatements => Contexts is not null;

    /// <summary>
    /// Whether this backend keeps accounts and bids across a restart. False is
    /// the fallback the relational store record describes: the catalogue is
    /// served from files, the bidding works, and nothing outlives the process.
    /// </summary>
    public bool Ready => Database.Ready;

    // #region attach
    /// <summary>
    /// The store, attached: everything that stands on it replaced in one call
    /// (ADR: The relational store, the addendum on the second chance). At
    /// startup this is called at once when the store came up; after startup
    /// the second chance calls it with a catalogue it has already warmed, so
    /// the first request after the swap is not the cold one. Each member is
    /// one reference written once, and a request in flight sees either the
    /// files or the store for each of them, both of which answer correctly;
    /// the swap happens at most once in a process's life.
    /// </summary>
    public void Attach(StoreAttachment attachment)
    {
        Database = attachment.Database;
        Contexts = attachment.Contexts;
        QuietContexts = attachment.QuietContexts;
        Inventory = attachment.Inventory;
        Bids = attachment.Bids;
        Activity = attachment.Activity;
        Probe = attachment.Probe;
        UserStore = attachment.UserStore;
    }
    // #endregion attach
}

/// <summary>What a store brings with it when it is attached to its backend: the state that says it came up, and the five things that stand on it.</summary>
/// <param name="Database">The state that says whether the store came up, and why not if it did not.</param>
/// <param name="Contexts">The relational contexts, on the SQL backend only; null otherwise.</param>
/// <param name="QuietContexts">The same database without the statement interceptor, for readings that must not fill the SQL log; null otherwise.</param>
/// <param name="Inventory">The catalogue service reading from this store.</param>
/// <param name="Bids">The bid service writing through to this store.</param>
/// <param name="Activity">Where this store keeps the requests it served.</param>
/// <param name="Probe">Checks whether the seed catalogue is in the store right now.</param>
/// <param name="UserStore">Builds Identity's account store for a request's scope, or returns null when there are no accounts to keep.</param>
public sealed record StoreAttachment(
    DatabaseState Database,
    IDbContextFactory<YardDbContext>? Contexts,
    IDbContextFactory<YardDbContext>? QuietContexts,
    InventoryService Inventory,
    BidService Bids,
    IActivityStore Activity,
    Func<Task<bool>> Probe,
    Func<IServiceProvider, IUserStore<YardUser>?> UserStore);
// #endregion backend
