// One store and everything that stands on it, and the bundle that attaches a store to it.
// Its own file because the rest of the API reads a backend through this one shape, so a
// reader looking for what a backend holds finds it here and nothing else.
using TheYard.Application;

namespace TheYard.Api.Shared.Stores;

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
    public required Stock Inventory { get; set; }

    /// <summary>The bids on this backend's catalogue, written through to the store once it is attached.</summary>
    public required BidBook Bids { get; set; }

    /// <summary>The simulated competing bidders on this backend's catalogue, the room the buyer bids in.</summary>
    public required Room Market { get; init; }

    /// <summary>
    /// What the store says about itself for the Admin tab, through the one port both stores
    /// implement: never asked which store it is (ADR: The onion cut into vertical slices, rule 4).
    /// No store attached is the null answer.
    /// </summary>
    public IStoreDiagnostics Diagnostics { get; set; } = NoStoreDiagnostics.Instance;

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
    /// The relational store's own reading of itself for the Machines card, read by the adapter
    /// that owns the database, or an absent reading with its reason on a store that keeps none.
    /// </summary>
    public Task<StoreLoad> ReadLoadAsync(int rows, CancellationToken cancellation) =>
        Diagnostics.ReadLoadAsync(rows, cancellation);

    /// <summary>Whether this backend's catalogue is warm right now, for the Admin tab's memory reading.</summary>
    public bool CatalogueLoaded => Inventory.IsWarm;

    /// <summary>
    /// Where this backend's catalogue is answered from and how much of it this process holds, for the
    /// Machines card, which reads it here so no endpoint names the inventory (ADR: The store does the work).
    /// </summary>
    public object CatalogueHeld()
    {
        var report = Inventory.Report();
        return new
        {
            mode = report.Mode,
            held_in_memory = report.HeldInMemory,
            target = report.Target,
            filled = report.Filled,
            note = report.Note,
        };
    }

    /// <summary>True on the document store, which logs each operation with its request charge.</summary>
    public bool LogsOperations => Diagnostics.LogsOperations;

    /// <summary>True on an attached relational store, which logs each statement it runs.</summary>
    public bool LogsStatements => Diagnostics.LogsStatements;

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
        Diagnostics = attachment.Diagnostics;
        Inventory = attachment.Inventory;
        Bids = attachment.Bids;
        Activity = attachment.Activity;
        Probe = attachment.Probe;
    }
    // #endregion attach
}

/// <summary>What a store brings with it when it is attached to its backend: the state that says it came up, and the five things that stand on it.</summary>
/// <param name="Database">The state that says whether the store came up, and why not if it did not.</param>
/// <param name="Diagnostics">What the store says about itself for the Admin tab.</param>
/// <param name="Inventory">The catalogue service reading from this store.</param>
/// <param name="Bids">The bid service writing through to this store.</param>
/// <param name="Activity">Where this store keeps the requests it served.</param>
/// <param name="Probe">Checks whether the seed catalogue is in the store right now.</param>
public sealed record StoreAttachment(
    DatabaseState Database,
    IStoreDiagnostics Diagnostics,
    Stock Inventory,
    BidBook Bids,
    IActivityStore Activity,
    Func<Task<bool>> Probe);
// #endregion backend
