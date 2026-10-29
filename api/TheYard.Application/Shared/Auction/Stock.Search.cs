// A listing planned out of the catalogue port's primitives: the three schedule segments, the
// overlay of bids and the room, and the page. Its own file because it is the one copy of that plan
// every store is answered through.
using TheYard.Domain.Shared.Auction;

namespace TheYard.Application.Shared.Auction;

/// <summary>A listing planned out of the catalogue port's primitives: the three schedule segments, the overlay of bids and the room, and the page; the class is described in Stock.cs.</summary>
public sealed partial class Stock
{
    // #region search
    /// <summary>
    /// Vehicles matching <paramref name="filter"/> (statuses derived from <paramref name="clock"/>),
    /// ordered by <paramref name="sort"/>, paged by <paramref name="limit"/>/<paramref name="offset"/>,
    /// with the total counting every match. <paramref name="overlay"/> (everybody's bids and the
    /// room's) is laid on before filtering, so price bounds and the price sorts see the figures
    /// the page displays.
    /// </summary>
    public async Task<SearchResult> SearchAsync(
        VehicleFilter filter,
        AuctionClock clock,
        VehicleSort sort = VehicleSort.EndingSoonest,
        int limit = int.MaxValue,
        int offset = 0,
        ListingOverlay? overlay = null,
        CancellationToken cancellation = default)
    {
        var (warm, catalogue) = await ReadyAsync(cancellation);
        offset = Math.Max(0, offset);
        limit = Math.Max(0, limit);
        if (CatalogueFilterResolver.Resolve(filter, clock, warm.Facets) is not { } resolved)
        {
            return new SearchResult(0, []);
        }

        // #region overlay
        // A bid or the room moves a price, and the store holds the dataset's figure. When the move
        // can change which vehicles match or where they sort, the store leaves those vehicles out,
        // they are worked out here from their raised figures, and the two ordered lists are merged
        // at the page. Otherwise the store's page is the page, and the moves are laid on its rows.
        bool overlayMatters = overlay is { Ids.Count: > 0 }
            && (sort is VehicleSort.PriceAsc or VehicleSort.PriceDesc or VehicleSort.MostBids
                || filter.PriceMin is not null || filter.PriceMax is not null);
        if (!overlayMatters)
        {
            var (total, rows) = await _listing.StorePageAsync(catalogue, resolved, sort, offset, limit, cancellation);
            return new SearchResult(total, [.. rows.Select(entry => Finish(entry.Vehicle, warm, overlay))]);
        }

        var matches = filter.Compile(clock);
        var moved = await _listing.FindEntriesAsync(catalogue, overlay!.Ids, cancellation);
        var raised = moved
            .Select(entry => entry with { Vehicle = overlay.Apply(entry.Vehicle) })
            .Where(entry => matches(entry.Vehicle))
            .OrderBy(entry => Key(entry, sort, resolved.SinceAnchorMs))
            .ToList();
        int skip = Math.Max(0, offset - raised.Count);
        int take = (int)Math.Min(int.MaxValue, (long)offset + limit - skip);
        var (storeTotal, stored) = await _listing.StorePageAsync(catalogue, resolved with { Exclude = overlay.Ids }, sort, skip, take, cancellation, moved);
        return new SearchResult(
            storeTotal + raised.Count,
            // The raised vehicles already stand at their price and the store's rows are none of
            // the moved ones, so nothing is laid on twice.
            [.. Merge(stored, raised, sort, resolved.SinceAnchorMs, skip, offset, limit).Select(entry => Finish(entry.Vehicle, warm, null))]);
        // #endregion overlay
    }

    /// <summary>
    /// The page of the merged order: the store's rows from <paramref name="skip"/> on and every
    /// raised vehicle, walked together. A row's place is how many store rows and raised vehicles
    /// came before it; a raised vehicle that sorts before the first fetched row is counted but
    /// never on the page, because at most <paramref name="raised"/>'s count of them can precede it.
    /// </summary>
    private static IEnumerable<CatalogueEntry> Merge(IReadOnlyList<CatalogueEntry> stored, IReadOnlyList<CatalogueEntry> raised, VehicleSort sort, long since, int skip, int offset, int limit)
    {
        int s = 0, r = 0;
        long place = skip;
        while ((s < stored.Count || r < raised.Count) && place < (long)offset + limit)
        {
            bool takeRaised = r < raised.Count
                && (s >= stored.Count || Key(raised[r], sort, since).CompareTo(Key(stored[s], sort, since)) < 0);
            var next = takeRaised ? raised[r++] : stored[s++];
            if (place >= offset)
            {
                yield return next;
            }
            place++;
        }
    }

    /// <summary>The order every store sorts by, as one comparable key: the segment band, the order's figure, and the expansion's place.</summary>
    private static (int Band, long Figure, int Seq) Key(CatalogueEntry entry, VehicleSort sort, long since)
    {
        var vehicle = entry.Vehicle;
        if (sort == VehicleSort.EndingSoonest)
        {
            var slot = CatalogueSlot.For(vehicle.Id);
            return slot.SegmentAt(since) switch
            {
                CatalogueSegment.Live => (0, slot.EndOffsetMs, entry.Seq),
                CatalogueSegment.Upcoming => (1, slot.StartOffsetMs, entry.Seq),
                _ => (2, -slot.EndOffsetMs, entry.Seq),
            };
        }
        return sort switch
        {
            VehicleSort.PriceAsc => (0, CatalogueSlot.PriceOf(vehicle), entry.Seq),
            VehicleSort.PriceDesc => (0, -CatalogueSlot.PriceOf(vehicle), entry.Seq),
            VehicleSort.Condition => (0, -(long)Math.Round(vehicle.ConditionGrade * 1_000), entry.Seq),
            VehicleSort.MostBids => (0, -vehicle.BidCount, entry.Seq),
            _ => (0, 0, entry.Seq),
        };
    }

    // #endregion search
}
