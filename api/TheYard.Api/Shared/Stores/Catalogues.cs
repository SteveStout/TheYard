// The two stores' catalogues, built for the composition root. A file of its own because each one
// names three adapters and a rule about who fills, and StoreRegistration reads more plainly with
// one line per store (ADR: The store does the work).
using TheYard.Application;
using TheYard.Infrastructure;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Api.Shared.Stores;

/// <summary>
/// Each store's catalogue: a query per listing against the store, and the expansion of the seed
/// only to fill it, and only when the deploy says so with Catalogue:Keep. The one web app fills
/// both stores' catalogues, being the only process that keeps them; were the sites split again,
/// each would fill the store it defaults to and watch the other, since two fills of one store
/// would pay for it twice.
/// </summary>
internal static class Catalogues
{
    /// <summary>
    /// Nothing for a process the deploy has not told to keep its catalogues, which is every test
    /// host and a developer's machine; the fill for every store when one process serves both
    /// sites (ADR: One web app for every .NET process); otherwise the fill for the store this site
    /// defaults to and a watch on the other, which the other site fills.
    /// </summary>
    public static CatalogueUpkeep UpkeepFor(bool keep, string defaultStore, string store, bool servesBothSites = false) =>
        !keep ? CatalogueUpkeep.Off
        : servesBothSites || store == defaultStore ? CatalogueUpkeep.Fill
        : CatalogueUpkeep.Watch;

    /// <summary>The relational store's catalogue, over the Catalogue table, filled whole at warm-up on a local SQLite file.</summary>
    public static InventoryService Sql(ContextFactory contexts, YardConnection yard, int targetCount, CatalogueUpkeep upkeep) =>
        new(
            new EfVehicleCatalogue(contexts, targetCount, local: yard.Provider == YardProvider.Sqlite, fillsWords: upkeep == CatalogueUpkeep.Fill),
            new SyntheticVehicleSource(new EfVehicleSource(contexts), targetCount),
            new EfPhotoManifestSource(contexts),
            upkeep);

    /// <summary>The document store's catalogue, over the catalogue container.</summary>
    public static InventoryService Cosmos(CosmosStore store, int targetCount, CatalogueUpkeep upkeep) =>
        new(
            new CosmosVehicleCatalogue(store, targetCount),
            new SyntheticVehicleSource(new CosmosVehicleSource(store), targetCount),
            new CosmosPhotoManifestSource(store),
            upkeep);
}
