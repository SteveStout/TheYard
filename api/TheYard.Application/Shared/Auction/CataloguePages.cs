// The store's answers as the listing reads them: the five-second cache, the page planned segment by
// segment, the same page and count with the moved vehicles left out, and the reads by id. A class of
// its own because Stock with it was over three hundred lines (ADR-088), and because this is the one
// place a store's answer is cached and the one place it meets the raised prices (ADR: The store does
// the work).
using TheYard.Application.Shared.Caching;
using TheYard.Domain.Shared.Auction;

namespace TheYard.Application.Shared.Auction;

/// <summary>A catalogue's counts, pages and vehicles as <see cref="Stock"/> reads them, through bounded caches.</summary>
public sealed class CataloguePages
{
    private const int ExpectedVehicleBytes = 1_500;
    private const long BucketMs = 5_000;

    // #region listing-cache
    // Bounded by count and bytes, expiring after a bucket of the clock: a store's answer is reused
    // for the five seconds a page of auctions cannot visibly change in, and never grows past the
    // numbers here (ADR: The store does the work, rule b).
    private readonly BoundedCache<string, SegmentCounts> _counts = new(512, 1L << 20, TimeSpan.FromSeconds(5), _ => 96);
    private readonly BoundedCache<string, IReadOnlyList<CatalogueEntry>> _pages = new(64, 16L << 20, TimeSpan.FromSeconds(5), page => 64 + (page.Count * ExpectedVehicleBytes));
    private readonly BoundedCache<string, CatalogueEntry> _vehicles = new(4_096, 8L << 20, TimeSpan.FromMinutes(10), _ => ExpectedVehicleBytes, comparer: StringComparer.Ordinal);
    // #endregion listing-cache

    /// <summary>Forgets every cached count and page, when the catalogue that answers changes.</summary>
    internal void Clear()
    {
        _counts.Clear();
        _pages.Clear();
    }

    // #region page
    /// <summary>
    /// One page from the store and the count beside it. The default listing is three segments,
    /// live by closest end, upcoming by soonest start, ended by most recent end, so it is read
    /// segment by segment, each off its own index, and stops at the page's last row.
    /// </summary>
    internal async Task<(int Total, IReadOnlyList<CatalogueEntry> Rows)> StorePageAsync(IVehicleCatalogue catalogue, CatalogueFilter filter, VehicleSort sort, int skip, int take, IReadOnlyList<CatalogueEntry>? excluded = null)
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
    internal async Task<IReadOnlyList<CatalogueEntry>> PageAsync(IVehicleCatalogue catalogue, CatalogueFilter filter, CatalogueOrder order, int skip, int take)
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

    /// <summary>
    /// The counts with the moved vehicles left out, as the store's count without them less the
    /// moved ones that match on the dataset's figures, worked out here. Leaving the ids in the
    /// count's query would make the store read every row's id rather than the counts index, and
    /// would key the cache on a list that changes with every bid; this way the count is the same
    /// cached, index-served count every listing with that filter shares.
    /// </summary>
    private async Task<SegmentCounts> CountsLessAsync(IVehicleCatalogue catalogue, CatalogueFilter filter, IReadOnlyList<CatalogueEntry> excluded)
    {
        var whole = filter with { Exclude = [] };
        var all = await CountsAsync(catalogue, whole);
        int live = 0, upcoming = 0, ended = 0;
        foreach (var entry in excluded)
        {
            var slot = CatalogueSlot.For(entry.Vehicle.Id);
            if (!whole.Matches(entry, slot, VehicleSearchIndex.TextFor(entry.Vehicle)))
            {
                continue;
            }
            switch (slot.SegmentAt(filter.SinceAnchorMs))
            {
                case CatalogueSegment.Live:
                    live++;
                    break;
                case CatalogueSegment.Upcoming:
                    upcoming++;
                    break;
                default:
                    ended++;
                    break;
            }
        }
        return new SegmentCounts(all.Live - live, all.Upcoming - upcoming, all.Ended - ended);
    }

    /// <summary>
    /// A page with the moved vehicles left out, read as the store's own page without them, longer by
    /// as many of them as match, and the moved ones dropped here. A store row's place without the
    /// moved vehicles is its place with them less the moved ones that sort before it, so the rows
    /// kept are exactly the page the store would have answered with the ids in its query; the
    /// query itself stays the cached, index-served one every visitor shares, where the ids in it
    /// made the store test each row it read against the list, 58 seconds once under the browser suite.
    /// </summary>
    private async Task<IReadOnlyList<CatalogueEntry>> PageLessAsync(IVehicleCatalogue catalogue, CatalogueFilter filter, CatalogueOrder order, int skip, int take, IReadOnlyList<CatalogueEntry> excluded)
    {
        var whole = filter with { Exclude = [] };
        var before = excluded
            .Where(entry => whole.Matches(entry, CatalogueSlot.For(entry.Vehicle.Id), VehicleSearchIndex.TextFor(entry.Vehicle)))
            .Select(entry => OrderKey(entry, order))
            .Order()
            .ToList();
        var ids = filter.Exclude.ToHashSet(StringComparer.Ordinal);
        var rows = await PageAsync(catalogue, whole, order, skip, (int)Math.Min(int.MaxValue, (long)take + before.Count));
        var kept = new List<CatalogueEntry>(Math.Min(take, rows.Count));
        int place = skip;
        foreach (var row in rows)
        {
            if (!ids.Contains(row.Vehicle.Id))
            {
                int index = before.BinarySearch(OrderKey(row, order));
                int without = place - (index < 0 ? ~index : index);
                if (without >= skip && without < (long)skip + take)
                {
                    kept.Add(row);
                }
            }
            place++;
        }
        return kept;
    }

    /// <summary>A vehicle's place in a store order on the dataset's figures, the expansion's place breaking every tie, as the adapters sort.</summary>
    private static (long Figure, int Seq) OrderKey(CatalogueEntry entry, CatalogueOrder order)
    {
        var vehicle = entry.Vehicle;
        var slot = CatalogueSlot.For(vehicle.Id);
        long figure = order switch
        {
            CatalogueOrder.EndAscending => slot.EndOffsetMs,
            CatalogueOrder.StartAscending => slot.StartOffsetMs,
            CatalogueOrder.EndDescending => -slot.EndOffsetMs,
            CatalogueOrder.PriceAscending => CatalogueSlot.PriceOf(vehicle),
            CatalogueOrder.PriceDescending => -CatalogueSlot.PriceOf(vehicle),
            CatalogueOrder.ConditionDescending => -(long)Math.Round(vehicle.ConditionGrade * 1_000),
            CatalogueOrder.BidsDescending => -vehicle.BidCount,
            _ => 0,
        };
        return (figure, entry.Seq);
    }

    /// <summary>The vehicles with these ids, each read once from the store and kept for ten minutes, in the expansion's order.</summary>
    internal async Task<IReadOnlyList<CatalogueEntry>> FindEntriesAsync(IVehicleCatalogue catalogue, IReadOnlyCollection<string> ids)
    {
        if (ids.Count == 0)
        {
            return [];
        }
        if (catalogue is MemoryCatalogue)
        {
            return await catalogue.FindAsync(ids);
        }

        var found = new List<CatalogueEntry>(ids.Count);
        var missing = new List<string>();
        foreach (string id in ids.Distinct(StringComparer.Ordinal))
        {
            if (_vehicles.TryGet(id, out var cached))
            {
                found.Add(cached);
            }
            else
            {
                missing.Add(id);
            }
        }
        if (missing.Count > 0)
        {
            foreach (var entry in await catalogue.FindAsync(missing))
            {
                _vehicles.Set(entry.Vehicle.Id, entry);
                found.Add(entry);
            }
        }
        found.Sort((left, right) => left.Seq.CompareTo(right.Seq));
        return found;
    }
}
