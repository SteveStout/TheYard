// What the running system is doing, as the Admin tab shows it. This file holds the SQL ring, the
// document store ring and its numbers, and the startup timings. The other parts live beside it:
//   LogRingBuffer.cs      - LogEntry, LogRingBuffer, RingBufferLoggerProvider: the log lines
//   RequestTimings.cs     - RequestEntry, RequestRingBuffer, EndpointTiming, Percentiles: request timings
//   HttpCurrentRequest.cs - HttpCurrentRequest: which request caused a statement
using TheYard.Application;

namespace TheYard.Api;

// #region admin-observability
// What the running system is doing, on the Admin tab: the SQL it sends, the
// log lines it writes, and how long both take (ADR: What the database is
// actually doing).
//
// All three are the same shape as the error buffer: a fixed-size ring in this
// process's memory, reset by every container roll, and the page says so. A demo
// does not need a log store, and a log store is exactly the kind of thing that
// turns a free tier into a bill.

/// <summary>
/// Fixed-size, thread-safe ring of recent SQL statements. Nothing here holds a
/// parameter value: <see cref="SqlStatement"/> has no field for one.
/// </summary>
public sealed class SqlRingBuffer(int capacity) : ISqlLog
{
    /// <summary>The most entries the ring keeps, never below one.</summary>
    private readonly int _capacity = Math.Max(1, capacity);

    /// <summary>The lock every read and write of the ring takes.</summary>
    private readonly object _gate = new();

    /// <summary>The entries, oldest first; the oldest leaves when a new one would pass the capacity.</summary>
    private readonly Queue<SqlStatement> _entries = new();

    /// <summary>
    /// A statement the Admin tab caused by being looked at.
    ///
    /// The health check runs two SELECTs to prove the catalogue is in the
    /// store, and an open Admin tab asks for it every thirty seconds. Kept,
    /// those four statements a minute fill a two hundred slot ring in under an
    /// hour and the section shows nothing but the act of reading it
    /// (ADR: Reviewing my own work).
    /// </summary>
    private static bool SelfObservation(string? request) =>
        request is not null
        && (request.EndsWith("/api/health", StringComparison.Ordinal)
            || request.EndsWith("/readyz", StringComparison.Ordinal)
            || request.EndsWith("/api/admin/sql", StringComparison.Ordinal)
            || request.EndsWith("/api/admin/logs", StringComparison.Ordinal)
            || request.EndsWith("/api/admin/metrics", StringComparison.Ordinal)
            // And the machines card, which reads the database's own view of
            // itself every half minute: left in, the newest statement on the
            // SQL card would always be this page looking at itself
            // (ADR: What the machines are doing).
            || request.EndsWith("/api/admin/machines", StringComparison.Ordinal));

    /// <summary>Where a statement also goes so a roll does not end it (ADR: Logs that outlive the container, the addendum on the cards); unset, nowhere.</summary>
    public Action<SqlStatement>? Kept { get; set; }

    /// <summary>
    /// Keeps a statement unless the Admin tab caused it by being looked at, then hands it to <see
    /// cref="Kept"/>.
    /// </summary>
    public void Record(SqlStatement statement)
    {
        if (SelfObservation(statement.Request))
        {
            return;
        }

        lock (_gate)
        {
            _entries.Enqueue(statement);
            while (_entries.Count > _capacity)
            {
                _entries.Dequeue();
            }
        }

        Kept?.Invoke(statement);
    }

    /// <summary>The ring's entries, newest first, copied so the caller can read them outside the lock.</summary>
    public IReadOnlyList<SqlStatement> Snapshot()
    {
        lock (_gate)
        {
            return _entries.Reverse().ToArray();
        }
    }
}
// #endregion admin-observability

// #region store-ring
/// <summary>
/// Fixed-size, thread-safe ring of recent document store operations, the
/// sibling of <see cref="SqlRingBuffer"/> for the other store (ADR: What the
/// store is actually doing). Same self-observation rule: the health check's two
/// point reads every thirty seconds would otherwise fill the ring with the act
/// of reading it.
/// </summary>
public sealed class StoreRingBuffer(int capacity) : IStoreLog
{
    /// <summary>The most entries the ring keeps, never below one.</summary>
    private readonly int _capacity = Math.Max(1, capacity);

    /// <summary>The lock every read and write of the ring takes.</summary>
    private readonly object _gate = new();

    /// <summary>The entries, oldest first; the oldest leaves when a new one would pass the capacity.</summary>
    private readonly Queue<StoreOperation> _entries = new();

    /// <summary>
    /// A store operation the Admin tab caused by being looked at, which the ring leaves out, as the
    /// SQL ring does.
    /// </summary>
    private static bool SelfObservation(string? request) =>
        request is not null
        && (request.EndsWith("/api/health", StringComparison.Ordinal)
            || request.EndsWith("/readyz", StringComparison.Ordinal)
            || request.EndsWith("/api/admin/store", StringComparison.Ordinal)
            || request.EndsWith("/api/admin/sql", StringComparison.Ordinal)
            || request.EndsWith("/api/admin/logs", StringComparison.Ordinal)
            || request.EndsWith("/api/admin/metrics", StringComparison.Ordinal)
            || request.EndsWith("/api/admin/peer", StringComparison.Ordinal)
            // The experiment's own queries are the point of the experiment card
            // and are shown there with their charge; kept out of this ring so an
            // open Admin tab does not push a visitor's bid out of it.
            || request.EndsWith("/api/admin/experiment", StringComparison.Ordinal));

    /// <summary>Where an operation also goes so a roll does not end it (ADR: Logs that outlive the container, the addendum on the cards); unset, nowhere.</summary>
    public Action<StoreOperation>? Kept { get; set; }

    /// <summary>
    /// Keeps an operation unless the Admin tab caused it by being looked at, then hands it to <see
    /// cref="Kept"/>.
    /// </summary>
    public void Record(StoreOperation operation)
    {
        if (SelfObservation(operation.Request))
        {
            return;
        }

        lock (_gate)
        {
            _entries.Enqueue(operation);
            while (_entries.Count > _capacity)
            {
                _entries.Dequeue();
            }
        }

        Kept?.Invoke(operation);
    }

    /// <summary>The ring's entries, newest first, copied so the caller can read them outside the lock.</summary>
    public IReadOnlyList<StoreOperation> Snapshot()
    {
        lock (_gate)
        {
            return _entries.Reverse().ToArray();
        }
    }
}

/// <summary>The store window's numbers, computed on read like the request percentiles.</summary>
/// <param name="Store">The key of the store the numbers are for.</param>
/// <param name="Window">How many store operations the numbers cover.</param>
/// <param name="P50Ms">The median operation duration, in milliseconds; null when the window is empty.</param>
/// <param name="P95Ms">The 95th percentile operation duration, in milliseconds; null when the window is empty.</param>
/// <param name="MaxMs">The slowest operation, in milliseconds; null when the window is empty.</param>
/// <param name="RuTotal">The request units spent across the window, rounded to two places.</param>
/// <param name="RuP50">The median request units per operation.</param>
/// <param name="RuMax">The most request units any one operation spent.</param>
/// <param name="CrossPartition">How many operations crossed partitions.</param>
/// <param name="PointOperations">How many operations were point reads or writes.</param>
public sealed record StoreMetrics(
    string Store,
    int Window,
    long? P50Ms,
    long? P95Ms,
    long? MaxMs,
    double RuTotal,
    double RuP50,
    double RuMax,
    int CrossPartition,
    int PointOperations)
{
    /// <summary>
    /// The numbers for one store's operations: durations by percentile and the request units they
    /// spent.
    /// </summary>
    public static StoreMetrics Of(string store, IReadOnlyList<StoreOperation> operations)
    {
        long[] durations = operations.Select(o => o.DurationMs).ToArray();
        double[] charges = operations.Select(o => o.RequestCharge).OrderBy(c => c).ToArray();
        return new StoreMetrics(
            store,
            operations.Count,
            Percentiles.OfOrNull(durations, 50),
            Percentiles.OfOrNull(durations, 95),
            durations.Length == 0 ? null : durations.Max(),
            Math.Round(charges.Sum(), 2),
            charges.Length == 0 ? 0 : charges[(int)Math.Ceiling(charges.Length * 0.5) - 1],
            charges.Length == 0 ? 0 : charges[^1],
            operations.Count(o => o.Partition.StartsWith("cross", StringComparison.Ordinal)),
            operations.Count(o => o.Kind.StartsWith("point", StringComparison.Ordinal)));
    }
}
// #endregion store-ring

// #region startup-timings
/// <summary>
/// How long each part of coming up took, and when the container was ready to
/// serve, measured from the process's own start. The comparison card shows
/// these for both containers on the same rows (ADR: Backends, side by side).
/// </summary>
public sealed class StartupTimings
{
    /// <summary>When this process started, UTC, which every figure here is measured from.</summary>
    private readonly DateTime _processStart = System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime();

    /// <summary>How long each named step took, in milliseconds.</summary>
    private readonly Dictionary<string, long> _steps = new(StringComparer.Ordinal);

    /// <summary>Milliseconds from process start to the point the host finished warming, or null until then.</summary>
    public long? ReadyMs { get; private set; }

    /// <summary>
    /// Runs one step of coming up, records how long it took under <paramref name="step"/>, and
    /// passes its result on.
    /// </summary>
    public async Task<T> Time<T>(string step, Func<Task<T>> work)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var result = await work();
        _steps[step] = clock.ElapsedMilliseconds;
        return result;
    }

    /// <summary>
    /// Runs one step of coming up that returns nothing and records how long it took under <paramref
    /// name="step"/>.
    /// </summary>
    public async Task Time(string step, Func<Task> work)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await work();
        _steps[step] = clock.ElapsedMilliseconds;
    }

    /// <summary>Marks the host warm: <see cref="ReadyMs"/> becomes the time since the process started.</summary>
    public void Ready() => ReadyMs = (long)(DateTime.UtcNow - _processStart).TotalMilliseconds;

    /// <summary>How long a step took in milliseconds, or null when it has not run.</summary>
    public long? Ms(string step) => _steps.TryGetValue(step, out long ms) ? ms : null;
}
// #endregion startup-timings
