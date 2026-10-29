// The keyset read on the document store: a page deep in an order is asked for from a known row's
// order key and place, never by a count of rows to skip, because the store's OFFSET reads every
// document it skips (1,658 request units and 9.4 seconds at an offset of 40,000). A file of its own
// because the places it keeps, and when it may use them, are one subject, and the catalogue that
// reads from them is a class of its own (ADR-088, the 300-line rule)
// (ADR: The store does the work, the addendum on a typed word as a seek).
using System.Collections.Concurrent;
using Microsoft.Azure.Cosmos;
using TheYard.Application.Shared.Auction;
using TheYard.Infrastructure.Cosmos.Shared.Stores;

namespace TheYard.Infrastructure.Cosmos.Shared.Auction;

/// <summary>
/// The kept places of every order over the whole catalogue, and the page read from them; the live
/// segment's places are <see cref="CosmosCatalogueLive"/>. The queries are the catalogue's own
/// (<see cref="CosmosVehicleCatalogue"/>), so a page read here is the page OFFSET would read.
/// </summary>
/// <param name="store">The document store.</param>
/// <param name="target">How many vehicles the catalogue is.</param>
public sealed class CosmosCatalogueKeysets(CosmosStore store, int target)
{
    /// <summary>How many rows apart the kept places are: the most a page skips with OFFSET once they are kept.</summary>
    public const int KeyStep = 250;

    /// <summary>How many places one read of the walk that keeps them covers, about forty request units a read.</summary>
    private const int WalkRead = 1_000;

    // One table of places per order, each the order key and place of every KeyStep-th row of the
    // whole catalogue. The slots, prices, grades and bid counts are written once by the fill and
    // never change, so a table walked once holds for as long as the catalogue does.
    private readonly ConcurrentDictionary<CatalogueOrder, Keyset> _keysets = new();
    private readonly ConcurrentDictionary<CatalogueOrder, Task> _walks = new();

    // The live segment's places, which move as the day goes on.
    private readonly CosmosCatalogueLive _live = new(store, target);

    private Container Catalogue => store.ContainerNamed(Containers.Catalogue);

    /// <summary>Every KeyStep-th row's order key and place, in the order's own order.</summary>
    private sealed record Keyset(double[] Keys, int[] Places);

    /// <summary>The walks that keep the places, while any runs. A test awaits them.</summary>
    public Task Walking => Task.WhenAll(_walks.Values);

    /// <summary>The walk of the live set, while one runs; finished or null otherwise.</summary>
    public Task? LiveWalking => _live.Walking;

    /// <summary>The document path an order sorts on first, and whether it descends; null for the place itself.</summary>
    private static (string Path, bool Descending)? KeyOf(CatalogueOrder order) => order switch
    {
        CatalogueOrder.EndAscending => ("end_offset", false),
        CatalogueOrder.StartAscending => ("start_offset", false),
        CatalogueOrder.EndDescending => ("end_offset", true),
        CatalogueOrder.PriceAscending => ("price", false),
        CatalogueOrder.PriceDescending => ("price", true),
        CatalogueOrder.ConditionDescending => ("condition_grade", true),
        CatalogueOrder.BidsDescending => ("bid_count", true),
        _ => null,
    };

    /// <summary>
    /// Whether a page may be read from the kept places: the whole catalogue in any order, or the
    /// default listing's upcoming segment by start or ended segment by end, each of which is the
    /// tail of its order over the whole catalogue. Any other filter is a set the places do not
    /// count, and is read with OFFSET as before.
    /// </summary>
    public static bool KeysetServes(CatalogueFilter filter, CatalogueOrder order) =>
        filter is { Make: null, BodyStyle: null, TitleStatus: null, Province: null, MinCondition: null, PriceMin: null, PriceMax: null, Tokens.Count: 0, Exclude.Count: 0 }
        && KeyOf(order) is not null
        && (filter.Segment == CatalogueSegment.Any
            || (filter.Segment == CatalogueSegment.Live && order == CatalogueOrder.EndAscending)
            || (filter.Segment == CatalogueSegment.Upcoming && order == CatalogueOrder.StartAscending)
            || (filter.Segment == CatalogueSegment.Ended && order == CatalogueOrder.EndDescending));

    /// <summary>The row at or after the kept place, in the order: a range on the order's first key that its composite index serves.</summary>
    internal static string AtOrAfter(string path, bool descending) =>
        $"(c.{path} {(descending ? "<" : ">")} @key OR (c.{path} = @key AND c.seq >= @place))";

    /// <summary>
    /// A page deep in an order from the kept places, or null when they are not kept yet, in which
    /// case the walk that keeps them is started and the page is read with OFFSET this once.
    /// </summary>
    public async Task<IReadOnlyList<CatalogueEntry>?> PageAsync(CatalogueFilter filter, CatalogueOrder order, int skip, int take, CancellationToken cancellation)
    {
        if (skip < KeyStep || !KeysetServes(filter, order) || KeyOf(order) is not { } key)
        {
            return null;
        }
        if (filter.Segment == CatalogueSegment.Live)
        {
            return await _live.PageAsync(filter.SinceAnchorMs, skip, take, cancellation);
        }
        if (!_keysets.TryGetValue(order, out var keyset))
        {
            StartWalks(order);
            return null;
        }
        long at = skip + await LeadAsync(filter, order, keyset, key, cancellation);
        long index = at / KeyStep;
        if (index >= keyset.Keys.Length)
        {
            return [];
        }
        var documents = await store.QueryAsync<CatalogueDocument>(
            Catalogue,
            new QueryDefinition($"SELECT * FROM c WHERE {CosmosVehicleCatalogue.Written} AND {AtOrAfter(key.Path, key.Descending)} ORDER BY {CosmosVehicleCatalogue.OrderBy(order)} OFFSET {at % KeyStep} LIMIT {take}")
                .WithParameter("@target", target)
                .WithParameter("@key", keyset.Keys[index])
                .WithParameter("@place", keyset.Places[index]),
            partitionKey: null,
            "cross-partition", cancellation: cancellation);
        return [.. documents.Select(document => document.ToEntry())];
    }

    /// <summary>
    /// How many rows of the whole order come before the segment: none for the whole catalogue; for
    /// the upcoming segment, every row that started by now; for the ended, every row that ends
    /// after now. Read as the kept place at the boundary and one count of at most KeyStep rows after it.
    /// </summary>
    private async Task<long> LeadAsync(CatalogueFilter filter, CatalogueOrder order, Keyset keyset, (string Path, bool Descending) key, CancellationToken cancellation)
    {
        if (filter.Segment == CatalogueSegment.Any)
        {
            return 0;
        }
        double d = filter.SinceAnchorMs;
        // Upcoming by start: the lead is the rows with start <= now. Ended by end, descending: the rows with end > now.
        Func<double, bool> leads = order == CatalogueOrder.StartAscending ? figure => figure <= d : figure => figure > d;
        int last = -1;
        for (int i = 0; i < keyset.Keys.Length && leads(keyset.Keys[i]); i++)
        {
            last = i;
        }
        if (last < 0)
        {
            return 0;
        }
        string test = order == CatalogueOrder.StartAscending ? "c.start_offset <= @d" : "c.end_offset > @d";
        var counts = await store.QueryAsync<int>(
            Catalogue,
            new QueryDefinition($"SELECT VALUE COUNT(1) FROM c WHERE {CosmosVehicleCatalogue.Written} AND {AtOrAfter(key.Path, key.Descending)} AND {test}")
                .WithParameter("@target", target)
                .WithParameter("@key", keyset.Keys[last])
                .WithParameter("@place", keyset.Places[last])
                .WithParameter("@d", filter.SinceAnchorMs),
            partitionKey: null,
            "cross-partition", cancellation: cancellation);
        return (long)last * KeyStep + (counts.Count == 0 ? 0 : counts[0]);
    }

    /// <summary>
    /// Starts the walk for this order and for the default listing's two, once each: the first deep
    /// page of the default listing is read past the live segment into one of them.
    /// </summary>
    private void StartWalks(CatalogueOrder order)
    {
        foreach (var wanted in new[] { order, CatalogueOrder.StartAscending, CatalogueOrder.EndDescending }.Distinct())
        {
            // No token: this walk is kept under its order and handed to every later
            // request that wants the same one, so the request that happened to start it
            // does not get to take it away from the rest of them.
            _walks.GetOrAdd(wanted, chosen => Task.Run(() => WalkAsync(chosen, CancellationToken.None)));
        }
    }

    /// <summary>
    /// Reads the whole order once and keeps every KeyStep-th place. Only a whole catalogue is
    /// walked; a walk that fails is let go, and the next deep page starts another.
    /// </summary>
    private async Task WalkAsync(CatalogueOrder order, CancellationToken cancellation)
    {
        try
        {
            if (KeyOf(order) is not { } key || await CosmosVehicleCatalogue.FilledAsync(store, target, cancellation) < target)
            {
                return;
            }
            var (keys, places) = await WalkPlacesAsync(store, target, order, key.Path, key.Descending, null, cancellation);
            if ((long)(keys.Length - 1) * KeyStep < target && (long)keys.Length * KeyStep >= target)
            {
                _keysets[order] = new Keyset(keys, places);
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The walk is a saving, never the listing: the page reads with OFFSET until the next walk keeps the places.
        }
        finally
        {
            if (!_keysets.ContainsKey(order))
            {
                _walks.TryRemove(order, out _);
            }
        }
    }

    /// <summary>
    /// Reads an order a thousand places a read with a fifth of a second between, each read starting
    /// after the last place the one before ended on, so no read skips anything, and keeps the key
    /// and place of every KeyStep-th row: about forty request units a read.
    /// </summary>
    internal static async Task<(double[] Keys, int[] Places)> WalkPlacesAsync(CosmosStore store, int target, CatalogueOrder order, string path, bool descending, string? condition, CancellationToken cancellation, params (string Name, object Value)[] parameters)
    {
        var keys = new List<double>();
        var places = new List<int>();
        string after = descending
            ? $"(c.{path} < @key OR (c.{path} = @key AND c.seq > @place))"
            : $"(c.{path} > @key OR (c.{path} = @key AND c.seq > @place))";
        string within = condition is null ? "" : $" AND {condition}";
        long position = 0;
        double? lastKey = null;
        int lastPlace = 0;
        while (true)
        {
            var query = new QueryDefinition(lastKey is null
                    ? $"SELECT VALUE [c.{path}, c.seq] FROM c WHERE {CosmosVehicleCatalogue.Written}{within} ORDER BY {CosmosVehicleCatalogue.OrderBy(order)} OFFSET 0 LIMIT {WalkRead}"
                    : $"SELECT VALUE [c.{path}, c.seq] FROM c WHERE {CosmosVehicleCatalogue.Written}{within} AND {after} ORDER BY {CosmosVehicleCatalogue.OrderBy(order)} OFFSET 0 LIMIT {WalkRead}")
                .WithParameter("@target", target);
            if (lastKey is { } known)
            {
                query = query.WithParameter("@key", known).WithParameter("@place", lastPlace);
            }
            foreach (var (name, value) in parameters)
            {
                query = query.WithParameter(name, value);
            }
            var read = await store.QueryAsync<double[]>(store.ContainerNamed(Containers.Catalogue), query, partitionKey: null, "cross-partition", cancellation: cancellation);
            foreach (var pair in read)
            {
                if (position % KeyStep == 0)
                {
                    keys.Add(pair[0]);
                    places.Add((int)pair[1]);
                }
                position++;
                lastKey = pair[0];
                lastPlace = (int)pair[1];
            }
            if (read.Count < WalkRead)
            {
                return ([.. keys], [.. places]);
            }
            await Task.Delay(TimeSpan.FromMilliseconds(200));
        }
    }
}
