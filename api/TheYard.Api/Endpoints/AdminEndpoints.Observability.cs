// The Admin tab's observability handlers: the SQL, store and log rings, the kept rings, the
// timing computed from them, the Store bar, Azure's view of this container, the peer and the
// machines. Its own file because these are the reads that report on the running process;
// the routes that point at them are mapped in AdminEndpoints.cs.

using Microsoft.AspNetCore.Http.HttpResults;
using TheYard.Application;

namespace TheYard.Api;

/// <summary>The observability handlers of AdminEndpoints; the type and what it is for are described in AdminEndpoints.cs.</summary>
public static partial class AdminEndpoints
{
    /// <summary>Answers the SQL ring: the statements the relational store ran, newest first.</summary>
    private static IResult Sql(SqlRingBuffer sqlLog) =>
        Results.Json(sqlLog.Snapshot());

    /// <summary>Answers the document store's operations ring, newest first, with the store's name.</summary>
    private static IResult Store(Backends backends, StoreRingBuffer storeLog) =>
        Results.Json(new
        {
            store = backends.Named("cosmos")?.Name ?? backends.Default.Name,
            operations = storeLog.Snapshot(),
        });

    /// <summary>Answers the raw log ring, newest first, exactly as the console got it.</summary>
    private static IResult Logs(LogRingBuffer logLog) =>
        Results.Json(logLog.Snapshot());

    /// <summary>
    /// Answers one card's entries over a kept window read back from the document store, or a 400 when the card or
    /// the window is not one of the fixed names.
    /// </summary>
    private static async Task<IResult> Kept(string? card, string? window, KeptRingReader reader, CancellationToken cancellation) =>
        await reader.ReadAsync(card, window, DateTimeOffset.UtcNow, cancellation) is { } answer
            ? Results.Json(answer)
            : Results.Problem(detail: "card is one of errors, logs, sql or store, and window is one of 24h, 7d or 30d.", statusCode: 400, title: "The card or the window could not be read");

    /// <summary>
    /// Answers the timing, computed on read from the rings: request and SQL percentiles, counts by status, the
    /// document store's charges, and each store's cold start and share of the requests.
    /// </summary>
    private static IResult Metrics(HttpContext http, Backends backends, RequestRingBuffer requestLog, SqlRingBuffer sqlLog, StoreRingBuffer storeLog, HostStart start)
    {
        var startedAt = start.At;
        var requests = requestLog.Snapshot();
        var statements = sqlLog.Snapshot();
        long[] requestDurations = requests.Select(entry => entry.DurationMs).ToArray();
        long[] sqlDurations = statements.Select(statement => statement.DurationMs).ToArray();
        // The store this request is on gets the top-level numbers, which is what
        // the comparison card on a single-store container and the peer read have
        // always taken. Every store this container runs is listed below them, each
        // with its own cold start and its own share of the request ring
        // (ADR: One container, both stores).
        var mine = backends.For(http);
        return Results.Json(new
        {
            requests = new
            {
                window = requests.Count,
                p50_ms = Percentiles.OfOrNull(requestDurations, 50),
                p95_ms = Percentiles.OfOrNull(requestDurations, 95),
                by_path = Percentiles.ByPath(requests),
                // The same window by route, for the comparison card: a bid on one
                // vehicle and a bid on another are one row (ADR: Backends, side by side).
                by_route = Routes.ByRoute(requests),
            },
            // Counts by status, which is the aggregate that makes the timing above
            // mean something: a p95 of eight milliseconds reads very differently
            // when a third of the window is 500s. It also names nobody, which the
            // per-request list it replaced could not say.
            by_status = requests
                .GroupBy(entry => entry.Status)
                .OrderBy(group => group.Key)
                .Select(group => new { status = group.Key, count = group.Count() })
                .ToArray(),
            sql = new
            {
                window = statements.Count,
                p50_ms = Percentiles.OfOrNull(sqlDurations, 50),
                p95_ms = Percentiles.OfOrNull(sqlDurations, 95),
                max_ms = sqlDurations.Length == 0 ? (long?)null : sqlDurations.Max(),
            },
            // #region store-metrics
            // The same window over the document store, with what the window cost:
            // total request units, the median charge, the dearest single operation,
            // and how many of them fanned out across partitions. These are the
            // numbers the comparison card puts beside the milliseconds
            // (ADR: Backends, side by side).
            store = StoreMetrics.Of(mine.Name, storeLog.Snapshot()),
            store_by_route = Routes.ChargesByRoute(storeLog.Snapshot()),
            // How this container came up: how long the store took to answer, how
            // long the catalogue and the bids took to load, when it was ready to
            // serve, and what the seed cost. Measured on this container at this
            // start, which is the only honest cold start there is.
            startup = StartupView(mine),
            // #endregion store-metrics
            // #region backends-metrics
            // Every store this container runs, on the same rows the peer answers
            // with, so the card compares two stores in one process the way it
            // compared two containers: the cold start each one had, the requests
            // each one served, and what those cost the one that can say.
            backends = backends.All.Select(backend => new
            {
                key = backend.Key,
                store = backend.Name,
                ready = backend.Ready,
                @default = ReferenceEquals(backend, backends.Default),
                startup = StartupView(backend),
                requests = RequestsView(requests.Where(entry => entry.Store == backend.Key).ToArray()),
                store_metrics = backend.Cosmos is null
                    ? null
                    : StoreMetrics.Of(backend.Name, storeLog.Snapshot()),
                store_by_route = backend.Cosmos is null
                    ? Array.Empty<RouteCharge>()
                    : Routes.ChargesByRoute(storeLog.Snapshot()),
                sql = backend.Contexts is null
                    ? null
                    : new
                    {
                        window = statements.Count,
                        p50_ms = Percentiles.OfOrNull(sqlDurations, 50),
                        p95_ms = Percentiles.OfOrNull(sqlDurations, 95),
                        max_ms = sqlDurations.Length == 0 ? (long?)null : sqlDurations.Max(),
                    },
            }).ToArray(),
            // #endregion backends-metrics
            // No recent_requests list. The first version returned the whole ring,
            // five hundred entries of method, path, status and timing, which is a
            // near-real-time feed of what every other visitor to a public site is
            // doing: which vehicles they opened, which filters they typed. The page
            // never rendered it. Aggregates answer the question the section is for
            // and name nobody (the staff review, 2026-09-03).
        });

        static object RequestsView(IReadOnlyList<RequestEntry> served)
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

        object StartupView(Backend backend) => new
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

    /// <summary>
    /// Answers the Store bar: which stores this container runs, which one this request is on, and the other site's
    /// address. Expires a cookie the old toggle set.
    /// </summary>
    private static Ok<StoresView> Stores(HttpContext http, Backends backends)
    {
        Backends.ExpireLegacyCookie(http);
        return TypedResults.Ok(backends.Describe(http));
    }

    /// <summary>Answers what this container reads about its own Azure resource (AzureSelf).</summary>
    private static async Task<IResult> Azure(AzureSelf azureSelf) =>
        Results.Json(await azureSelf.GetStateAsync());

    /// <summary>
    /// Answers the other container's metrics, read server side, so the comparison card can show both backends.
    /// </summary>
    private static async Task<IResult> Peer(PeerReader peer) =>
        Results.Json(await peer.ReadAsync());

    /// <summary>
    /// Answers what the container and the two stores are doing, each as that machine reports itself, over the last
    /// hour or a kept window of 24h, 7d or 30d.
    /// </summary>
    private static async Task<IResult> Machines(string? window, MachineSampler sampler, MachineHistoryReader kept, Backends backends, StoreRingBuffer storeLog, RequestRingBuffer requestLog, HostStart start, ILoggerFactory loggers, IWebHostEnvironment environment, CancellationToken cancellation)
    {
        var startedAt = start.At;
        var relational = backends.Named("sql");
        var load = await ResourceStats.ReadAsync(relational, RingSizes.MachineSamples, cancellation, loggers.CreateLogger(environment.ApplicationName));
        var now = DateTimeOffset.UtcNow;
        var document = DocumentLoad.From(storeLog.Snapshot(), backends.Named("cosmos")?.Name ?? "Azure Cosmos DB", now);
        return Results.Json(new
        {
            // The hour below is this process's own memory and is always here. A
            // wider window is read from the store, in buckets sized to it.
            windows = MachineWindows.Names,
            history = await kept.ReadAsync(window, now, cancellation),
            // The request ring a minute at a time, in the unit a kept minute is
            // written in, so the hour and the month are one chart. The ring is
            // a number of requests and not an hour: when it is full its oldest
            // minute is marked, and the page says how many minutes it reaches.
            traffic = new { ring = RingSizes.RequestRing, minutes = TrafficMinutes.OfRing(requestLog.Snapshot(), RingSizes.RequestRing) },
            container = new
            {
                memory_limit_mb = MachineSampler.MemoryLimitMb,
                processors = MachineSampler.Processors,
                uptime_seconds = (long)(now - startedAt).TotalSeconds,
                every_seconds = (int)MachineSampler.Every.TotalSeconds,
                samples = sampler.Snapshot(),
                // Which catalogues this process is holding right now, because a
                // hundred thousand vehicles is most of what the memory above is.
                catalogues = backends.All.Select(backend => new
                {
                    store = backend.Name,
                    serves = ReferenceEquals(backend, backends.Default),
                    loaded = backend.Inventory.IsWarm,
                }),
            },
            relational = new
            {
                store = relational?.Name ?? "no relational store on this container",
                available = load.Available,
                note = load.Note,
                rows = load.Rows,
            },
            document = new
            {
                store = document.Store,
                available = document.Available,
                note = document.Note,
                request_units = document.RequestUnits,
                operations = document.Operations,
                p50_ms = document.P50Ms,
                p95_ms = document.P95Ms,
                free_request_units_per_second = DocumentLoad.FreeRequestUnitsPerSecond,
                // How many minutes back the operations ring reaches, so the
                // request units above are a total over a stated stretch.
                span_minutes = document.SpanMinutes,
                minutes = document.Minutes,
            },
        });
    }
}
