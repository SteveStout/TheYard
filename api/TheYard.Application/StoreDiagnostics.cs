// What a store can say about itself for the Admin tab, asked through one port so both stores
// answer the same questions (ADR: The onion cut into vertical slices, rule 4): whether it logs its
// statements or its operations, how long one round trip takes, and the database's own reading of
// its load where it keeps one.
namespace TheYard.Application;

// #region store-diagnostics
/// <summary>
/// The diagnostics port. The relational adapter and the document adapter each implement it, and a
/// backend on the files until its store is attached answers with <see cref="NoStoreDiagnostics"/>,
/// so nothing above the port tells the stores apart by asking which one it has.
/// </summary>
public interface IStoreDiagnostics
{
    /// <summary>True on a store that logs each operation with its request charge (the document store).</summary>
    bool LogsOperations { get; }

    /// <summary>True on a store that logs each statement it runs (an attached relational store).</summary>
    bool LogsStatements { get; }

    /// <summary>One round trip to the store doing as little work as a round trip can, in milliseconds, or null where there is no store to reach.</summary>
    /// <param name="cancellation">The caller's cancellation.</param>
    Task<double?> RoundTripMillisecondsAsync(CancellationToken cancellation);

    /// <summary>The database's own reading of its load, newest first, or an absent reading with its reason.</summary>
    /// <param name="rows">How many intervals to read.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    Task<StoreLoad> ReadLoadAsync(int rows, CancellationToken cancellation);
}

/// <summary>A backend with no store attached: it logs nothing, reaches nothing, and keeps no reading.</summary>
public sealed class NoStoreDiagnostics : IStoreDiagnostics
{
    private NoStoreDiagnostics()
    {
    }

    /// <summary>The one instance.</summary>
    public static NoStoreDiagnostics Instance { get; } = new();

    /// <summary>False: nothing is logged.</summary>
    public bool LogsOperations => false;

    /// <summary>False: nothing is logged.</summary>
    public bool LogsStatements => false;

    /// <summary>Null: there is no store to reach.</summary>
    /// <param name="cancellation">The caller's cancellation.</param>
    public Task<double?> RoundTripMillisecondsAsync(CancellationToken cancellation) => Task.FromResult<double?>(null);

    /// <summary>An absent reading.</summary>
    /// <param name="rows">How many intervals were asked for.</param>
    /// <param name="cancellation">The caller's cancellation.</param>
    public Task<StoreLoad> ReadLoadAsync(int rows, CancellationToken cancellation) =>
        Task.FromResult(StoreLoad.Absent("this container has no relational store, or it did not come up"));
}

/// <summary>One fifteen-second interval as the database reports it, every figure a percentage of what the tier allows.</summary>
/// <param name="At">The end of the fifteen-second interval, UTC.</param>
/// <param name="CpuPercent">Processor use, as a percentage of the tier's limit.</param>
/// <param name="DataIoPercent">Data file reads and writes, as a percentage of the tier's limit.</param>
/// <param name="LogWritePercent">Transaction log writes, as a percentage of the tier's limit.</param>
/// <param name="MemoryPercent">Memory use, as a percentage of the tier's limit.</param>
/// <param name="WorkerPercent">Concurrent workers, as a percentage of the tier's limit.</param>
public sealed record ResourceStatRow(
    DateTime At,
    double CpuPercent,
    double DataIoPercent,
    double LogWritePercent,
    double MemoryPercent,
    double WorkerPercent);

/// <summary>The relational store's own reading, or the reason there is not one.</summary>
/// <param name="Available">True when the database answered with its reading.</param>
/// <param name="Note">Why there is no reading, or null when there is one.</param>
/// <param name="Rows">The intervals the database reported, empty when there is no reading.</param>
public sealed record StoreLoad(bool Available, string? Note, IReadOnlyList<ResourceStatRow> Rows)
{
    /// <summary>A reading that did not happen, with the sentence the card shows in its place.</summary>
    public static StoreLoad Absent(string note) => new(false, note, []);
}
// #endregion store-diagnostics
