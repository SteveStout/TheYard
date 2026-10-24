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
        ListingOverlay? overlay = null)
    {
        var (warm, catalogue) = await ReadyAsync();
        offset = Math.Max(0, offset);
        limit = Math.Max(0, limit);
        if (Resolve(filter, clock, warm.Facets) is not { } resolved)
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
            var (total, rows) = await StorePageAsync(catalogue, resolved, sort, offset, limit);
            return new SearchResult(total, [.. rows.Select(entry => Finish(entry.Vehicle, warm, overlay))]);
        }

        var matches = filter.Compile(clock);
        var moved = await FindEntriesAsync(catalogue, overlay!.Ids);
        var raised = moved
            .Select(entry => entry with { Vehicle = overlay.Apply(entry.Vehicle) })
            .Where(entry => matches(entry.Vehicle))
            .OrderBy(entry => Key(entry, sort, resolved.SinceAnchorMs))
            .ToList();
        int skip = Math.Max(0, offset - raised.Count);
        int take = (int)Math.Min(int.MaxValue, (long)offset + limit - skip);
        var (storeTotal, stored) = await StorePageAsync(catalogue, resolved with { Exclude = overlay.Ids }, sort, skip, take, moved);
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

    // #region page
    /// <summary>
    /// One page from the store and the count beside it. The default listing is three segments,
    /// live by closest end, upcoming by soonest start, ended by most recent end, so it is read
    /// segment by segment, each off its own index, and stops at the page's last row.
    /// </summary>
    private async Task<(int Total, IReadOnlyList<CatalogueEntry> Rows)> StorePageAsync(IVehicleCatalogue catalogue, CatalogueFilter filter, VehicleSort sort, int skip, int take, IReadOnlyList<CatalogueEntry>? excluded = null)
    {
        // The counts are read only when a number needs them. Unfiltered, the total is the
        // catalogue's size, less the moved vehicles the store leaves out, and a page that starts
        // inside a segment needs no count at all: the default listing's first pages, which most
        // visitors read, cost the store one page and no count.
        SegmentCounts? counts = null;
        int total = Whole(filter) && catalogue is not MemoryCatalogue
            ? catalogue.Target - (excluded?.Count ?? 0)
            : (await Counts()).Total;
        var rows = new List<CatalogueEntry>(Math.Clamp(take, 0, 1_000));
        if (take <= 0 || skip >= total)
        {
            return (total, rows);
        }

        if (sort != VehicleSort.EndingSoonest)
        {
            rows.AddRange(await Page(filter, OrderFor(sort), skip, take));
            return (total, rows);
        }

        (CatalogueSegment Segment, CatalogueOrder Order)[] segments =
        [
            (CatalogueSegment.Live, CatalogueOrder.EndAscending),
            (CatalogueSegment.Upcoming, CatalogueOrder.StartAscending),
            (CatalogueSegment.Ended, CatalogueOrder.EndDescending),
        ];
        foreach (var (segment, order) in segments)
        {
            if (filter.Segment != CatalogueSegment.Any && filter.Segment != segment)
            {
                // A filter kept to one segment holds nothing in the other two.
                continue;
            }
            var page = await Page(filter.In(segment), order, skip, take);
            if (page.Count > 0)
            {
                // The segment ran from the skip to its end or to the page's: the next one starts at its top.
                rows.AddRange(page);
                take -= page.Count;
                skip = 0;
                if (take <= 0)
                {
                    break;
                }
                continue;
            }
            if (skip > 0)
            {
                // The skip runs past this segment: only now is its size needed.
                var known = await Counts();
                skip -= segment switch
                {
                    CatalogueSegment.Live => known.Live,
                    CatalogueSegment.Upcoming => known.Upcoming,
                    _ => known.Ended,
                };
                skip = Math.Max(0, skip);
            }
        }
        return (total, rows);

        async Task<SegmentCounts> Counts() =>
            counts ??= excluded is null ? await CountsAsync(catalogue, filter) : await CountsLessAsync(catalogue, filter, excluded);

        Task<IReadOnlyList<CatalogueEntry>> Page(CatalogueFilter f, CatalogueOrder order, int s, int t) =>
            excluded is null ? PageAsync(catalogue, f, order, s, t) : PageLessAsync(catalogue, f, order, s, t, excluded);
    }

    /// <summary>Whether a filter keeps every vehicle, so its total is the catalogue's size.</summary>
    private static bool Whole(CatalogueFilter filter) =>
        filter is { Make: null, BodyStyle: null, TitleStatus: null, Province: null, Segment: CatalogueSegment.Any, MinCondition: null, PriceMin: null, PriceMax: null, Tokens.Count: 0 };

    private static CatalogueOrder OrderFor(VehicleSort sort) => sort switch
    {
        VehicleSort.PriceAsc => CatalogueOrder.PriceAscending,
        VehicleSort.PriceDesc => CatalogueOrder.PriceDescending,
        VehicleSort.Condition => CatalogueOrder.ConditionDescending,
        VehicleSort.MostBids => CatalogueOrder.BidsDescending,
        _ => CatalogueOrder.Seq,
    };

    private static string Bucket(CatalogueFilter filter) => (filter.SinceAnchorMs / BucketMs).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private Task<SegmentCounts> CountsAsync(IVehicleCatalogue catalogue, CatalogueFilter filter) =>
        catalogue is MemoryCatalogue
            ? catalogue.CountAsync(filter)
            : _counts.GetOrAddAsync($"{Bucket(filter)}\u001e{filter.Key()}", () => catalogue.CountAsync(filter));

    /// <summary>
    /// One page, through the five-second cache. A segment's page cached earlier in the bucket can
    /// hold an auction that has since ended or started, so a cached page is checked against this
    /// instant by the schedule's own rule and read again when any row has moved on: the page a
    /// visitor gets never shows an ended auction among the live ones.
    /// </summary>
    private async Task<IReadOnlyList<CatalogueEntry>> PageAsync(IVehicleCatalogue catalogue, CatalogueFilter filter, CatalogueOrder order, int skip, int take)
    {
        if (catalogue is MemoryCatalogue)
        {
            return await catalogue.PageAsync(filter, order, skip, take);
        }
        string key = $"{Bucket(filter)}\u001e{filter.Key()}\u001e{order}\u001e{skip}\u001e{take}";
        var page = await _pages.GetOrAddAsync(key, () => catalogue.PageAsync(filter, order, skip, take));
        if (filter.Segment == CatalogueSegment.Any
            || page.All(entry => CatalogueSlot.For(entry.Vehicle.Id).SegmentAt(filter.SinceAnchorMs) == filter.Segment))
        {
            return page;
        }
        page = await catalogue.PageAsync(filter, order, skip, take);
        _pages.Set(key, page);
        return page;
    }
    // #endregion page
    // #endregion search
}
