// The relational store's answers to the diagnostics port: it logs every statement, a round trip
// is one SELECT 1, and on Azure SQL Database the database reports its own load.
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TheYard.Application.Shared.Stores;

namespace TheYard.Infrastructure.Shared.Stores;

/// <summary>
/// The relational store's diagnostics, over the context factory that logs its statements (ADR: The
/// onion cut into vertical slices, rule 4).
/// </summary>
/// <param name="contexts">The relational store's contexts, the ones whose statements reach the Admin tab.</param>
/// <param name="name">The store as it describes itself, for the reading's sentences.</param>
/// <param name="logger">Where a reading that failed is said, once the host's loggers exist; null before.</param>
public sealed class RelationalDiagnostics(IDbContextFactory<YardDbContext> contexts, string name, Func<ILogger?> logger) : IStoreDiagnostics
{
    /// <summary>False: the relational store logs statements, not operations.</summary>
    public bool LogsOperations => false;

    /// <summary>True: every statement reaches the SQL ring.</summary>
    public bool LogsStatements => true;

    /// <summary>One SELECT 1, timed.</summary>
    /// <param name="cancellation">The caller's cancellation.</param>
    public async Task<double?> RoundTripMillisecondsAsync(CancellationToken cancellation)
    {
        long start = Stopwatch.GetTimestamp();
        await YardDatabase.PingAsync(contexts, cancellation);
        return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    /// <summary>The database's own reading of its load (ResourceStats.cs), or an absent reading with its reason.</summary>
    /// <param name="rows">How many intervals to read.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    public Task<StoreLoad> ReadLoadAsync(int rows, CancellationToken cancellation) =>
        ResourceStats.ReadAsync(contexts, name, rows, cancellation, logger());
}
