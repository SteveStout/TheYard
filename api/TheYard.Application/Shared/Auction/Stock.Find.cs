// The reads by id: one vehicle, several, and the first live ones, each with its photos. The filter
// resolved to the store's spelling is CatalogueFilterResolver.cs, and the cached reads CataloguePages.cs.
using TheYard.Data;
using TheYard.Domain.Shared.Auction;

namespace TheYard.Application.Shared.Auction;

/// <summary>The reads by id; the class is described in Stock.cs.</summary>
public sealed partial class Stock
{
    // #region find
    /// <summary>One vehicle by id, with its photos, or null when the catalogue has no such id.</summary>
    public async Task<Vehicle?> FindAsync(string id, CancellationToken cancellation)
    {
        var found = await FindManyAsync([id], cancellation);
        return found.Count == 0 ? null : found[0];
    }

    /// <summary>The vehicles with these ids, with their photos, in the catalogue's order; ids it lacks are left out.</summary>
    public async Task<IReadOnlyList<Vehicle>> FindManyAsync(IReadOnlyCollection<string> ids, CancellationToken cancellation)
    {
        var (warm, catalogue) = await ReadyAsync(cancellation);
        return [.. (await _listing.FindEntriesAsync(catalogue, ids, cancellation)).Select(entry => Finish(entry.Vehicle, warm, null))];
    }

    /// <summary>
    /// The first live auctions in the catalogue's order, which is what the room looks at in a round
    /// beside the vehicles somebody is bidding on. A store query kept to the live segment, ordered by
    /// the expansion's place and stopped at <paramref name="take"/>, cached for the clock's bucket.
    /// </summary>
    public async Task<IReadOnlyList<Vehicle>> FirstLiveAsync(AuctionClock clock, int take, CancellationToken cancellation)
    {
        var (warm, catalogue) = await ReadyAsync(cancellation);
        var filter = new CatalogueFilter { SinceAnchorMs = clock.NowMs - clock.AnchorMs, Segment = CatalogueSegment.Live };
        return [.. (await _listing.PageAsync(catalogue, filter, CatalogueOrder.Seq, 0, take, cancellation)).Select(entry => Finish(entry.Vehicle, warm, null))];
    }
    // #endregion find
}
