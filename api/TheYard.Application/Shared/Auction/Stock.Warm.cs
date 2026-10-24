// The warm-up: the photo pools and the facets, read once and shared, and the catalogue chosen. The
// choice between the store and a list in memory, and the fill, are StockSource.cs.
using TheYard.Data;

namespace TheYard.Application.Shared.Auction;

/// <summary>The warm-up; the class is described in Stock.cs.</summary>
public sealed partial class Stock
{
    // #region warm
    // One warm-up, shared by every caller, started by whoever asks first; a warm-up that failed is
    // not kept, so the next caller starts a fresh one (ADR: The ports learn to wait). What it reads
    // is small: the photo manifest, the facets, and one count of what the store holds.
    private readonly object _warmGate = new();
    private Task<Warmed>? _warm;

    private sealed record Warmed(IReadOnlyDictionary<string, IReadOnlyList<PhotoEntry>> Pools, InventoryFacets Facets);

    private Task<Warmed> WarmTask()
    {
        lock (_warmGate)
        {
            if (_warm is null || _warm.IsFaulted || _warm.IsCanceled)
            {
                _warm = BuildAsync();
            }
            return _warm;
        }
    }

    /// <summary>Read what the catalogue needs before the first visitor does: the photo pools, the facets, and how full the store is.</summary>
    public Task WarmAsync() => WarmTask();

    /// <summary>Whether the warm-up has finished, which the host asserts before serving.</summary>
    public bool IsWarm => _warm is { IsCompletedSuccessfully: true };

    /// <summary>The background fill, while one runs; finished or null otherwise. A test awaits it.</summary>
    public Task? Filling => _source.Filling;

    /// <summary>Where the catalogue is answered from right now.</summary>
    public CatalogueReport Report() => _source.Report();

    private async Task<Warmed> BuildAsync()
    {
        // Pools keyed by lowercase style, because PhotoGallery looks them up lowercased, so a
        // capitalized style in the manifest must not silently miss.
        var pools = (await _manifestSource.LoadAsync())
            .GroupBy(photo => photo.Style.ToLowerInvariant())
            .ToDictionary(group => group.Key, group => (IReadOnlyList<PhotoEntry>)[.. group]);

        var facets = await (await _source.ChooseAsync()).FacetsAsync();
        return new Warmed(pools, facets);
    }
    // #endregion warm
}
