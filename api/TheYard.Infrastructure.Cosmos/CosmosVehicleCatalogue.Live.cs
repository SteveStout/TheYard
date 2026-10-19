// The keyset read's live segment: the live set moves as the day goes on, so its kept places are
// walked at one moment and corrected by the rows that ended or started since. A file of its own
// because CosmosVehicleCatalogue.Keyset.cs holds the places that never move, and these do
// (ADR: The store does the work, the addendum of 19 October).
using Microsoft.Azure.Cosmos;
using TheYard.Application;

namespace TheYard.Infrastructure.Cosmos;

/// <summary>The live segment's kept places; the class is described in CosmosVehicleCatalogue.cs.</summary>
public sealed partial class CosmosVehicleCatalogue
{
    // The live segment's places, walked at one moment of the day: the live set moves as the day
    // goes on, so its places are its own and are corrected by the few rows that ended or started
    // since, and walked again every hour and after every midnight.
    private volatile LiveKeyset? _live;
    private Task? _liveWalk;

    /// <summary>The walk of the live set, while one runs; finished or null otherwise. A test awaits it.</summary>
    public Task? LiveWalking => _liveWalk;

    /// <summary>Every KeyStep-th live row's end and place, by end, as the live set stood at <paramref name="Since"/> past the anchor.</summary>
    private sealed record LiveKeyset(long Since, DateTimeOffset Walked, double[] Ends, int[] Places);

    /// <summary>How long the live segment's places are used before they are walked again.</summary>
    public static readonly TimeSpan LiveFor = TimeSpan.FromHours(1);

    /// <summary>
    /// A page deep in the live segment, by end. The live rows before kept place i at this moment are
    /// the i times KeyStep the walk counted, less the rows that ended since the walk (each of them
    /// ends before any kept place still live), plus the rows that started since and sort before
    /// that place; both are counts over the minutes since the walk, a few hundred rows an hour.
    /// Null while no walk of today's live set is kept, after starting one.
    /// </summary>
    private async Task<IReadOnlyList<CatalogueEntry>?> LivePageAsync(long d, int skip, int take)
    {
        var live = _live;
        if (live is null || d < live.Since || DateTimeOffset.UtcNow - live.Walked > LiveFor)
        {
            StartLiveWalk(d);
            if (live is null || d < live.Since)
            {
                return null;
            }
        }
        long ended = await CountWhereAsync(
            "c.start_offset <= @since AND c.end_offset > @since AND c.end_offset <= @d",
            ("@since", live.Since), ("@d", d));
        long index = Math.Min((skip + ended) / KeyStep, live.Ends.Length - 1);
        while (index >= 0 && index * KeyStep >= ended)
        {
            long started = await CountWhereAsync(
                "c.start_offset > @since AND c.start_offset <= @d AND c.end_offset > @d AND (c.end_offset < @key OR (c.end_offset = @key AND c.seq < @place))",
                ("@since", live.Since), ("@d", d), ("@key", live.Ends[index]), ("@place", live.Places[index]));
            long before = index * KeyStep - ended + started;
            if (before <= skip)
            {
                var documents = await store.QueryAsync<CatalogueDocument>(
                    Catalogue,
                    new QueryDefinition($"SELECT * FROM c WHERE {Written} AND (c.start_offset <= @d AND c.end_offset > @d) AND {AtOrAfter("end_offset", false)} ORDER BY {OrderBy(CatalogueOrder.EndAscending)} OFFSET {skip - before} LIMIT {take}")
                        .WithParameter("@target", target)
                        .WithParameter("@d", d)
                        .WithParameter("@key", live.Ends[index])
                        .WithParameter("@place", live.Places[index]),
                    partitionKey: null,
                    "cross-partition");
                return [.. documents.Select(document => document.ToEntry())];
            }
            index--;
        }
        return null;
    }

    /// <summary>One COUNT over the written catalogue with these extra conditions.</summary>
    private async Task<long> CountWhereAsync(string condition, params (string Name, object Value)[] parameters)
    {
        var query = new QueryDefinition($"SELECT VALUE COUNT(1) FROM c WHERE {Written} AND {condition}").WithParameter("@target", target);
        foreach (var (name, value) in parameters)
        {
            query = query.WithParameter(name, value);
        }
        var counts = await store.QueryAsync<long>(Catalogue, query, partitionKey: null, "cross-partition");
        return counts.Count == 0 ? 0 : counts[0];
    }

    /// <summary>Starts one walk of the live set as it stands now, unless one is running.</summary>
    private void StartLiveWalk(long d)
    {
        var running = _liveWalk;
        if (running is { IsCompleted: false })
        {
            return;
        }
        _liveWalk = Task.Run(async () =>
        {
            try
            {
                if (await FilledAsync() < target)
                {
                    return;
                }
                var walked = DateTimeOffset.UtcNow;
                var (ends, places) = await WalkPlacesAsync(
                    CatalogueOrder.EndAscending, "end_offset", false, "(c.start_offset <= @since AND c.end_offset > @since)", ("@since", d));
                _live = new LiveKeyset(d, walked, ends, places);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The walk is a saving, never the listing: deep live pages read with OFFSET until the next walk.
            }
        });
    }
}
