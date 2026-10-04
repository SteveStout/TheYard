// The Admin tab's observability handlers: the SQL, store and log rings, the kept rings, the
// timing computed from them, the Store bar, Azure's view of this container, the peer and the
// machines. Its own file because these are the reads that report on the running process;
// the routes that point at them are mapped in AdminEndpoints.cs.

using Microsoft.AspNetCore.Http.HttpResults;
using TheYard.Application;
using TheYard.Infrastructure;

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
        await reader.ReadAsync(card, window, Clocks.UtcNow(), cancellation) is { } answer
            ? Results.Json(answer)
            : Results.Problem(detail: "card is one of errors, logs, sql or store, and window is one of 24h, 7d or 30d.", statusCode: 400, title: "The card or the window could not be read");

    /// <summary>
    /// Answers the timing, computed on read from the rings: request and SQL percentiles, counts by status, the
    /// document store's charges, and each store's cold start and share of the requests.
    /// </summary>
    private static IResult Metrics(HttpContext http, Backends backends, RequestRingBuffer requestLog, SqlRingBuffer sqlLog, StoreRingBuffer storeLog, HostStart start) =>
        Results.Json(MetricsReport.Build(backends, backends.For(http), requestLog.Snapshot(), sqlLog.Snapshot(), storeLog.Snapshot(), start.At));

    /// <summary>
    /// Answers the Store bar: which stores this container runs, which one this request is on, and the other site's
    /// address. Expires a stale yard-store cookie.
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
        var load = relational is null
            ? StoreLoad.Absent("this container has no relational store, or it did not come up")
            : await relational.ReadLoadAsync(RingSizes.MachineSamples, cancellation, loggers.CreateLogger(environment.ApplicationName));
        var now = Clocks.UtcNow();
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
                    loaded = backend.CatalogueLoaded,
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
