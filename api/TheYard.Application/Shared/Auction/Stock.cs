// The catalogue as a visitor's request meets it. This file is the class's fields, its two
// constructors and how a vehicle leaves; the work is in the files beside it:
//   Stock.Warm.cs     the warm-up (region warm)
//   Stock.Search.cs   a listing planned out of the port's primitives (region search)
//   Stock.Find.cs     the reads by id (region find)
// and in three classes of their own, cut out when Stock passed three hundred lines (ADR-088):
//   StockSource.cs              the choice of store or list, and the fill (regions choose, fill)
//   CataloguePages.cs           the store's answers through the cache (regions listing-cache, page)
//   CatalogueFilterResolver.cs  a filter resolved to the store's spelling (region resolve)
using TheYard.Data;
using TheYard.Domain.Shared.Auction;

namespace TheYard.Application.Shared.Auction;

/// <summary>One page of search results plus the total match count.</summary>
/// <param name="Total">How many vehicles match the search across all pages.</param>
/// <param name="Vehicles">The vehicles on the requested page.</param>
public sealed record SearchResult(int Total, IReadOnlyList<Vehicle> Vehicles);

/// <summary>Distinct dropdown values, one list per filterable field.</summary>
/// <param name="Makes">Every distinct make in the inventory.</param>
/// <param name="BodyStyles">Every distinct body style in the inventory.</param>
/// <param name="TitleStatuses">Every distinct title status in the inventory.</param>
/// <param name="Provinces">Every distinct province a vehicle sits in.</param>
public sealed record InventoryFacets(
    IReadOnlyList<string> Makes,
    IReadOnlyList<string> BodyStyles,
    IReadOnlyList<string> TitleStatuses,
    IReadOnlyList<string> Provinces);

/// <summary>The prices somebody's bid or the room has moved: which vehicles, and how to lay the moves over one.</summary>
/// <param name="Ids">Every vehicle with a bid or a room answer on it.</param>
/// <param name="Apply">The vehicle at the price it stands at.</param>
public sealed record ListingOverlay(IReadOnlyCollection<string> Ids, Func<Vehicle, Vehicle> Apply);

/// <summary>What a site does about a store whose catalogue holds less than it should, past what the warm-up fills itself.</summary>
public enum CatalogueUpkeep
{
    /// <summary>Nothing: the list in memory answers for the life of the process. Every test host.</summary>
    Off,

    /// <summary>Count the store once a minute and switch to it when another site has filled it.</summary>
    Watch,

    /// <summary>Fill the store beside the running site, then switch to it.</summary>
    Fill,
}

/// <summary>Where a site's catalogue is answered from right now, for the Admin tab and the memory test.</summary>
/// <param name="Mode">"store" when the store answers; "memory" when the list in this process does; "filling" while this site fills the store beside the list; "waiting for the store" while another site does.</param>
/// <param name="HeldInMemory">How many vehicles this process holds in a list: zero once the store answers.</param>
/// <param name="Target">How many vehicles the catalogue is.</param>
/// <param name="Filled">How many of them the store holds with their slot, at the last count.</param>
/// <param name="Note">Why the store is not answering, when it is not.</param>
public sealed record CatalogueReport(string Mode, int HeldInMemory, int Target, int Filled, string? Note);

/// <summary>
/// The read-only inventory use case. The store holds the hundred thousand vehicles with their slot,
/// and every listing, facet and vehicle read is a query against it, planned here out of the
/// catalogue port's primitives (ADR: The store does the work). The deterministic expansion is the
/// seeder: it fills an empty store once and is let go. A site whose store is down, or still being
/// filled, answers from the expansion in memory until the store can, and says so on the Admin tab.
/// Vehicles whose body style has no photo pool keep the images the dataset carries.
/// </summary>
public sealed partial class Stock : IStockReader
{
    private readonly IPhotoManifestSource _manifestSource;
    private readonly string _imagePathPrefix;
    private readonly StockSource _source;

    // The store's answers through the five-second cache (CataloguePages.cs).
    private readonly CataloguePages _listing = new();


    /// <summary>The catalogue out of a list in memory, filled from <paramref name="vehicleSource"/>: the tests' shape and the file fallback's.</summary>
    public Stock(IVehicleSource vehicleSource, IPhotoManifestSource manifestSource, string imagePathPrefix = "/api/images")
        : this(null, vehicleSource, manifestSource, CatalogueUpkeep.Off, imagePathPrefix)
    {
    }

    /// <summary>The catalogue out of <paramref name="store"/>, filled from <paramref name="seeder"/> when the store holds less than it should.</summary>
    /// <param name="store">The store's catalogue, or null to answer from memory.</param>
    /// <param name="seeder">The deterministic expansion, read only to fill the store or to stand in for it.</param>
    /// <param name="manifestSource">The photo manifest.</param>
    /// <param name="upkeep">What this site does about a store that holds less than it should, past what the warm-up fills.</param>
    /// <param name="imagePathPrefix">The path photos are served under.</param>
    public Stock(IVehicleCatalogue? store, IVehicleSource seeder, IPhotoManifestSource manifestSource, CatalogueUpkeep upkeep, string imagePathPrefix = "/api/images")
    {
        _manifestSource = manifestSource;
        _source = new StockSource(store, seeder, upkeep, _listing.Clear);
        _imagePathPrefix = imagePathPrefix;
    }

    private async Task<(Warmed Warm, IVehicleCatalogue Catalogue)> ReadyAsync(CancellationToken cancellation)
    {
        var warm = await WarmTask(cancellation);
        return (warm, _source.Active!);
    }

    // #region facets
    /// <summary>
    /// The values behind the catalogue's filters, read from the store once at warm-up and handed
    /// back by reference, because they cannot change after the fill and walking the catalogue for
    /// them on every request is the cost ADR: The search index took out (its addendum).
    /// </summary>
    public async Task<InventoryFacets> FacetsAsync(CancellationToken cancellation) => (await WarmTask(cancellation)).Facets;
    // #endregion facets

    /// <summary>A vehicle as it leaves: its gallery photos in place of the dataset's, then at the price it stands at.</summary>
    private Vehicle Finish(Vehicle vehicle, Warmed warm, ListingOverlay? overlay)
    {
        var photos = PhotoGallery.SelectPhotos(vehicle.Id, vehicle.Make, vehicle.BodyStyle, warm.Pools);
        var shown = photos.Count == 0 ? vehicle : vehicle with { Images = [.. photos.Select(file => $"{_imagePathPrefix}/{file}")] };
        return overlay is null ? shown : overlay.Apply(shown);
    }
}
