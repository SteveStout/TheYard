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

    private Task<Warmed> WarmTask(CancellationToken cancellation)
    {
        Task<Warmed> warming;
        bool started = false;
        lock (_warmGate)
        {
            if (_warm is null || _warm.IsFaulted || _warm.IsCanceled)
            {
                _warm = BuildAsync(cancellation);
                started = true;
            }
            warming = _warm;
        }
        // The starter is already running under this token; everybody else stops waiting
        // without stopping the warm-up the rest of the process is queued behind.
        return started || warming.IsCompleted ? warming : warming.WaitAsync(cancellation);
    }

    /// <summary>
    /// Read what the catalogue needs before the first visitor does: the photo pools, the
    /// facets, and how full the store is.
    ///
    /// <para>One warm-up is shared, so the caller who starts it lends it their token and
    /// every later caller stops only their own wait (ADR: The ports learn to wait, the
    /// addendum on the token). The starter in a running container is the hosted service that
    /// warms beside the listening server, so what the store and the background fill see is
    /// the host's stopping token: a container being shut down stops filling, which until this
    /// rung it did not.</para>
    /// </summary>
    /// <param name="cancellation">Lent to the warm-up when this caller starts it; otherwise it ends this caller's wait alone.</param>
    public Task WarmAsync(CancellationToken cancellation) => WarmTask(cancellation);

    /// <summary>Whether the warm-up has finished, which the host asserts before serving.</summary>
    public bool IsWarm => _warm is { IsCompletedSuccessfully: true };

    /// <summary>The background fill, while one runs; finished or null otherwise. A test awaits it.</summary>
    public Task? Filling => _source.Filling;

    /// <summary>Where the catalogue is answered from right now.</summary>
    public CatalogueReport Report() => _source.Report();

    private async Task<Warmed> BuildAsync(CancellationToken cancellation)
    {
        // Pools keyed by lowercase style, because PhotoGallery looks them up lowercased, so a
        // capitalized style in the manifest must not silently miss.
        var pools = (await _manifestSource.LoadAsync(cancellation))
            .GroupBy(photo => photo.Style.ToLowerInvariant())
            .ToDictionary(group => group.Key, group => (IReadOnlyList<PhotoEntry>)[.. group]);

        var facets = await (await _source.ChooseAsync(cancellation)).FacetsAsync(cancellation);
        return new Warmed(pools, facets);
    }
    // #endregion warm
}
