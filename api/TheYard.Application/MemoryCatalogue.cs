// The catalogue answered out of a list in this process. Two jobs and no third: the tests' store,
// and the bridge a site stands on while its real store is being filled, or when the store is down
// and the site serves the files. It answers the same primitives the two store adapters answer, so
// a listing is planned one way whatever holds the vehicles (ADR: The store does the work).
using TheYard.Data;
using TheYard.Domain;

namespace TheYard.Application;

/// <summary>
/// <see cref="IVehicleCatalogue"/> over vehicles held in memory, in the order given. Each order a
/// page can be read in is sorted once, the first time it is asked for, and a page walks it and
/// stops at its last row, which is the rule of sorting only as far as the page, kept for the
/// list as the stores keep it with their indexes.
/// </summary>
public sealed class MemoryCatalogue : IVehicleCatalogue
{
    private readonly CatalogueEntry[] _entries;
    private readonly CatalogueSlot[] _slots;
    private readonly string[] _texts;
    private readonly Dictionary<string, int> _byId;
    private readonly int[]?[] _orders = new int[]?[Enum.GetValues<CatalogueOrder>().Length];

    /// <summary>The catalogue over these vehicles, each at its place in the list.</summary>
    public MemoryCatalogue(IReadOnlyList<Vehicle> vehicles)
    {
        ArgumentNullException.ThrowIfNull(vehicles);
        _entries = new CatalogueEntry[vehicles.Count];
        _slots = new CatalogueSlot[vehicles.Count];
        _texts = new string[vehicles.Count];
        _byId = new Dictionary<string, int>(vehicles.Count, StringComparer.Ordinal);
        for (int seq = 0; seq < vehicles.Count; seq++)
        {
            var vehicle = vehicles[seq];
            _entries[seq] = new CatalogueEntry(seq, vehicle);
            _slots[seq] = CatalogueSlot.For(vehicle.Id);
            _texts[seq] = VehicleSearchIndex.TextFor(vehicle);
            _byId[vehicle.Id] = seq;
        }
    }

    /// <summary>How many vehicles this list holds, which is what the host reports as held in memory.</summary>
    public int Count => _entries.Length;

    /// <inheritdoc />
    public int Target => _entries.Length;

    /// <inheritdoc />
    public CatalogueFillPace Pace { get; } = new(int.MaxValue, TimeSpan.Zero, int.MaxValue);

    /// <inheritdoc />
    public Task<int> FilledAsync() => Task.FromResult(_entries.Length);

    /// <summary>Nothing to write: the list is the catalogue.</summary>
    public Task FillAsync(IReadOnlyList<CatalogueEntry> batch) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<SegmentCounts> CountAsync(CatalogueFilter filter)
    {
        int live = 0, upcoming = 0, ended = 0;
        for (int seq = 0; seq < _entries.Length; seq++)
        {
            if (!filter.Matches(_entries[seq], _slots[seq], _texts[seq]))
            {
                continue;
            }
            switch (_slots[seq].SegmentAt(filter.SinceAnchorMs))
            {
                case CatalogueSegment.Live: live++; break;
                case CatalogueSegment.Upcoming: upcoming++; break;
                default: ended++; break;
            }
        }
        return Task.FromResult(new SegmentCounts(live, upcoming, ended));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CatalogueEntry>> PageAsync(CatalogueFilter filter, CatalogueOrder order, int skip, int take)
    {
        var page = new List<CatalogueEntry>(Math.Clamp(take, 0, 1_000));
        if (take <= 0)
        {
            return Task.FromResult<IReadOnlyList<CatalogueEntry>>(page);
        }
        int skipped = 0;
        foreach (int seq in OrderOf(order))
        {
            if (!filter.Matches(_entries[seq], _slots[seq], _texts[seq]))
            {
                continue;
            }
            if (skipped < skip)
            {
                skipped++;
                continue;
            }
            page.Add(_entries[seq]);
            if (page.Count == take)
            {
                break;
            }
        }
        return Task.FromResult<IReadOnlyList<CatalogueEntry>>(page);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<CatalogueEntry>> FindAsync(IReadOnlyCollection<string> ids) =>
        Task.FromResult<IReadOnlyList<CatalogueEntry>>(
            [.. ids.Where(_byId.ContainsKey).Distinct(StringComparer.Ordinal).Select(id => _entries[_byId[id]]).OrderBy(entry => entry.Seq)]);

    /// <inheritdoc />
    public Task<InventoryFacets> FacetsAsync() =>
        Task.FromResult(new InventoryFacets(
            Distinct(vehicle => vehicle.Make),
            Distinct(vehicle => vehicle.BodyStyle),
            Distinct(vehicle => vehicle.TitleStatus),
            Distinct(vehicle => vehicle.Province)));

    private List<string> Distinct(Func<Vehicle, string> field) =>
        [.. _entries.Select(entry => field(entry.Vehicle)).Distinct().Order(StringComparer.Ordinal)];

    // #region orders
    /// <summary>
    /// Every place in one order, sorted the first time it is asked for and kept: a stable sort
    /// over the list's own order, so a tie keeps the expansion's place as the stores' second
    /// sort key does. Two requests that arrive together may both sort, and either result is the
    /// same, so no lock.
    /// </summary>
    private int[] OrderOf(CatalogueOrder order)
    {
        if (Volatile.Read(ref _orders[(int)order]) is { } kept)
        {
            return kept;
        }
        IEnumerable<int> places = Enumerable.Range(0, _entries.Length);
        int[] sorted = order switch
        {
            CatalogueOrder.EndAscending => [.. places.OrderBy(seq => _slots[seq].EndOffsetMs)],
            CatalogueOrder.StartAscending => [.. places.OrderBy(seq => _slots[seq].StartOffsetMs)],
            CatalogueOrder.EndDescending => [.. places.OrderByDescending(seq => _slots[seq].EndOffsetMs)],
            CatalogueOrder.PriceAscending => [.. places.OrderBy(seq => CatalogueSlot.PriceOf(_entries[seq].Vehicle))],
            CatalogueOrder.PriceDescending => [.. places.OrderByDescending(seq => CatalogueSlot.PriceOf(_entries[seq].Vehicle))],
            CatalogueOrder.ConditionDescending => [.. places.OrderByDescending(seq => _entries[seq].Vehicle.ConditionGrade)],
            CatalogueOrder.BidsDescending => [.. places.OrderByDescending(seq => _entries[seq].Vehicle.BidCount)],
            _ => [.. places],
        };
        Volatile.Write(ref _orders[(int)order], sorted);
        return sorted;
    }
    // #endregion orders
}
