// The document store's answers to the diagnostics port: it logs every operation with its charge,
// a round trip is half of the two point reads its probe makes, and it keeps no load reading of its
// own that this card shows.
using System.Diagnostics;
using TheYard.Application.Shared.Stores;

namespace TheYard.Infrastructure.Cosmos.Shared.Stores;

/// <summary>The document store's diagnostics (ADR: The onion cut into vertical slices, rule 4).</summary>
/// <param name="store">The document store.</param>
public sealed class DocumentDiagnostics(CosmosStore store) : IStoreDiagnostics
{
    /// <summary>True: every operation reaches the store ring with its request charge.</summary>
    public bool LogsOperations => true;

    /// <summary>False: the document store runs no statements.</summary>
    public bool LogsStatements => false;

    /// <summary>Half of the probe's two point reads, so the figure is one read.</summary>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task<double?> RoundTripMillisecondsAsync(CancellationToken cancellation)
    {
        long start = Stopwatch.GetTimestamp();
        await store.Starting.ProbeAsync();
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds / 2;
    }

    /// <summary>Absent: the document store's load is on the Cosmos DB card, read from Azure Monitor.</summary>
    /// <param name="rows">How many intervals were asked for.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    public Task<StoreLoad> ReadLoadAsync(int rows, CancellationToken cancellation) =>
        Task.FromResult(StoreLoad.Absent("the document store keeps no such reading; its load is on the Cosmos DB card"));
}
