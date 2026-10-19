// A visitor's filter resolved to the store's own spelling, and the reads by id with their bounded
// cache. Its own file because both are the edge between a request's words and the store's.
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Application;

/// <summary>A visitor's filter resolved to the store's own spelling, and the reads by id with their bounded cache; the class is described in InventoryService.cs.</summary>
public sealed partial class InventoryService
{
    // #region resolve
    /// <summary>
    /// The visitor's filter in the store's terms: each field value resolved to the store's own
    /// spelling against the facets, ignoring case as the filter always has, so the stores compare
    /// with plain equality. A value no vehicle has resolves to nothing, and the answer is the
    /// empty page without asking the store.
    /// </summary>
    private static CatalogueFilter? Resolve(VehicleFilter filter, AuctionClock clock, InventoryFacets facets)
    {
        if (!TryCanon(filter.Make, facets.Makes, out var make) ||
            !TryCanon(filter.BodyStyle, facets.BodyStyles, out var bodyStyle) ||
            !TryCanon(filter.TitleStatus, facets.TitleStatuses, out var titleStatus) ||
            !TryCanon(filter.Province, facets.Provinces, out var province))
        {
            return null;
        }

        string[] words = string.IsNullOrWhiteSpace(filter.Query)
            ? []
            : filter.Query.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return new CatalogueFilter
        {
            SinceAnchorMs = clock.NowMs - clock.AnchorMs,
            Make = make,
            BodyStyle = bodyStyle,
            TitleStatus = titleStatus,
            Province = province,
            Segment = filter.Status switch
            {
                AuctionStatus.Live => CatalogueSegment.Live,
                AuctionStatus.Upcoming => CatalogueSegment.Upcoming,
                AuctionStatus.Ended => CatalogueSegment.Ended,
                _ => CatalogueSegment.Any,
            },
            MinCondition = filter.MinCondition,
            PriceMin = filter.PriceMin,
            PriceMax = filter.PriceMax,
            Tokens = [.. words.Select(word => new CatalogueToken(word, StatusWordsHolding(word)))],
        };
    }

    private static readonly (string Word, CatalogueSegment Segment)[] StatusWords =
    [
        (AuctionStatus.Live.ToString().ToLowerInvariant(), CatalogueSegment.Live),
        (AuctionStatus.Upcoming.ToString().ToLowerInvariant(), CatalogueSegment.Upcoming),
        (AuctionStatus.Ended.ToString().ToLowerInvariant(), CatalogueSegment.Ended),
    ];

    private static CatalogueSegment[] StatusWordsHolding(string word) =>
        [.. StatusWords.Where(status => VehicleSearchIndex.Holds(status.Word, word)).Select(status => status.Segment)];

    private static bool TryCanon(string? wanted, IReadOnlyList<string> known, out string? canon)
    {
        canon = null;
        if (wanted is null)
        {
            return true;
        }
        canon = known.FirstOrDefault(value => value.Equals(wanted, StringComparison.OrdinalIgnoreCase));
        return canon is not null;
    }
    // #endregion resolve

    // #region find
    /// <summary>One vehicle by id, with its photos, or null when the catalogue has no such id.</summary>
    public async Task<Vehicle?> FindAsync(string id)
    {
        var found = await FindManyAsync([id]);
        return found.Count == 0 ? null : found[0];
    }

    /// <summary>The vehicles with these ids, with their photos, in the catalogue's order; ids it lacks are left out.</summary>
    public async Task<IReadOnlyList<Vehicle>> FindManyAsync(IReadOnlyCollection<string> ids)
    {
        var (warm, catalogue) = await ReadyAsync();
        return [.. (await FindEntriesAsync(catalogue, ids)).Select(entry => Finish(entry.Vehicle, warm, null))];
    }

    /// <summary>
    /// The first live auctions in the catalogue's order, which is what the room looks at in a round
    /// beside the vehicles somebody is bidding on. A store query kept to the live segment, ordered by
    /// the expansion's place and stopped at <paramref name="take"/>, cached for the clock's bucket.
    /// </summary>
    public async Task<IReadOnlyList<Vehicle>> FirstLiveAsync(AuctionClock clock, int take)
    {
        var (warm, catalogue) = await ReadyAsync();
        var filter = new CatalogueFilter { SinceAnchorMs = clock.NowMs - clock.AnchorMs, Segment = CatalogueSegment.Live };
        return [.. (await PageAsync(catalogue, filter, CatalogueOrder.Seq, 0, take)).Select(entry => Finish(entry.Vehicle, warm, null))];
    }

    private async Task<IReadOnlyList<CatalogueEntry>> FindEntriesAsync(IVehicleCatalogue catalogue, IReadOnlyCollection<string> ids)
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
    // #endregion find
}
