// The listing with the vehicles somebody's bid has moved left out of the store's answer: the
// count and the page read as every visitor's cached, index-served ones, and the moved vehicles
// taken off in the process. Its own file because it is the one place the store's answer and the
// raised prices meet (ADR: The store does the work).
using TheYard.Domain.Shared.Auction;

namespace TheYard.Application.Shared.Auction;

/// <summary>The listing without the moved vehicles; the class is described in Stock.cs.</summary>
public sealed partial class Stock
{
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
}
