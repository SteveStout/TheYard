// The Admin tab's timing card, built from the rings on read: request and SQL percentiles, counts
// by status, the document store's charges, and each store's cold start and share of the requests.
// Its own file so the endpoint only reads the rings and answers, and the shape the card draws can
// be read in one place.
using TheYard.Application;

namespace TheYard.Api;

/// <summary>Builds the timing card's answer from the request, SQL and store rings.</summary>
public static class MetricsReport
{
    /// <summary>
    /// The card's answer. The store this request is on gets the top-level numbers, which the
    /// comparison card on a one-store container and the peer read take; every store this
    /// container runs is listed below them, each with its own cold start and its own share of the
    /// request ring.
    /// </summary>
    public static object Build(
        Backends backends,
        Backend mine,
        IReadOnlyList<RequestEntry> requests,
        IReadOnlyList<SqlStatement> statements,
        IReadOnlyList<StoreOperation> operations,
        DateTimeOffset startedAt)
    {
        long[] requestDurations = requests.Select(entry => entry.DurationMs).ToArray();
        long[] sqlDurations = statements.Select(statement => statement.DurationMs).ToArray();
        return new
        {
            requests = new
            {
                window = requests.Count,
                p50_ms = Percentiles.OfOrNull(requestDurations, 50),
                p95_ms = Percentiles.OfOrNull(requestDurations, 95),
                by_path = Percentiles.ByPath(requests),
                // The same window by route, for the comparison card: a bid on one vehicle and a
                // bid on another are one row.
                by_route = Routes.ByRoute(requests),
            },
            // Counts by status, which is what makes the timing above mean something: a p95 of
            // eight milliseconds reads very differently when a third of the window is 500s. It
            // names nobody, which a list of requests could not say.
            by_status = requests
                .GroupBy(entry => entry.Status)
                .OrderBy(group => group.Key)
                .Select(group => new { status = group.Key, count = group.Count() })
                .ToArray(),
            sql = SqlView(statements.Count, sqlDurations),
            // #region store-metrics
            // The same window over the document store, with what the window cost: total request
            // units, the median charge, the dearest single operation, and how many of them fanned
            // out across partitions. These are the numbers the comparison card puts beside the
            // milliseconds.
            store = StoreMetrics.Of(mine.Name, operations),
            store_by_route = Routes.ChargesByRoute(operations),
            // How this container came up, measured on this container at this start, which is the
            // only honest cold start there is.
            startup = StartupView(mine, startedAt),
            // #endregion store-metrics
            // #region runtime-metrics
            // How much of this process's code the runtime compiled itself since it started, and
            // how long that took, beside the garbage collector's mode: the reading that says
            // whether code arrives compiled ahead of time (ReadyToRun) or is compiled on the
            // container's one shared core as it is first called (ADR: Compiled before it ships).
            // Process-wide, so once, not per store.
            runtime = new
            {
                jit_methods = System.Runtime.JitInfo.GetCompiledMethodCount(currentThread: false),
                jit_ms = (long)System.Runtime.JitInfo.GetCompilationTime(currentThread: false).TotalMilliseconds,
                gc = System.Runtime.GCSettings.IsServerGC ? "server" : "workstation",
                gc_concurrent = System.Runtime.GCSettings.LatencyMode != System.Runtime.GCLatencyMode.Batch,
                processors = Environment.ProcessorCount,
            },
            // #endregion runtime-metrics
            // #region backends-metrics
            // Every store this container runs, on the same rows the peer answers with, so the
            // card compares two stores in one process the way it compares two containers: the cold
            // start each one had, the requests each one served, and what those cost the one that
            // can say. A store's charges show only on the store that logs operations, and its
            // statements only on the store that logs statements.
            backends = backends.All.Select(backend => new
            {
                key = backend.Key,
                store = backend.Name,
                ready = backend.Ready,
                @default = ReferenceEquals(backend, backends.Default),
                startup = StartupView(backend, startedAt),
                requests = RequestsView(requests.Where(entry => entry.Store == backend.Key).ToArray()),
                store_metrics = backend.LogsOperations ? StoreMetrics.Of(backend.Name, operations) : null,
                store_by_route = backend.LogsOperations ? Routes.ChargesByRoute(operations) : Array.Empty<RouteCharge>(),
                sql = backend.LogsStatements ? SqlView(statements.Count, sqlDurations) : null,
            }).ToArray(),
            // #endregion backends-metrics
            // No list of recent requests: a feed of what every other visitor to a public site is
            // doing (which vehicles they opened, which filters they typed) answers nothing the
            // aggregates above do not, and names people where they name nobody.
        };
    }

    /// <summary>The SQL window's percentiles and its slowest statement, or nulls when the window is empty.</summary>
    private static object SqlView(int window, long[] durations) => new
    {
        window,
        p50_ms = Percentiles.OfOrNull(durations, 50),
        p95_ms = Percentiles.OfOrNull(durations, 95),
        max_ms = durations.Length == 0 ? (long?)null : durations.Max(),
    };

    /// <summary>One store's share of the request ring: its percentiles and its routes.</summary>
    private static object RequestsView(IReadOnlyList<RequestEntry> served)
    {
        long[] durations = served.Select(entry => entry.DurationMs).ToArray();
        return new
        {
            window = served.Count,
            p50_ms = Percentiles.OfOrNull(durations, 50),
            p95_ms = Percentiles.OfOrNull(durations, 95),
            by_route = Routes.ByRoute(served),
        };
    }

    /// <summary>How one store came up: each step's time, what the seed cost, and when it was ready.</summary>
    private static object StartupView(Backend backend, DateTimeOffset startedAt) => new
    {
        store = backend.Name,
        prepare_ms = backend.Startup.Ms("prepare"),
        schema_ms = backend.Database.SchemaMs,
        seed_ms = backend.Database.SeedMs,
        seed_ru = backend.Database.SeedRequestUnits,
        catalogue_ms = backend.Startup.Ms("catalogue"),
        bids_ms = backend.Startup.Ms("bids"),
        ready_ms = backend.Startup.ReadyMs,
        started_at = startedAt,
    };
}
