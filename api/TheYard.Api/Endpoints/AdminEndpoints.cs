using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using TheYard.Application;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Api;

/// <summary>
/// The Admin tab's reads (the rings, the timing, the activity, the machines, the
/// kept logs, the tests, the page sweep, the proof) and the Store bar's answer.
/// Every one reads what the host already keeps; the per-visitor rows and the kept
/// log answer only to the operator's key.
/// </summary>
public static class AdminEndpoints
{
    // The dataset is snake_case; the two replies written by hand keep the wire shape.
    private static readonly JsonSerializerOptions WireFormat = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>Maps the Admin tab's routes under /api/admin, /api/stores and /api/tests.</summary>
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        #region admin-observability-endpoints
        // The raw SQL, newest first. Statement text, parameter names and types, how
        // long the database took, and the request that caused it. No parameter values:
        // see the comment on SqlStatement for why there is nowhere to put one.
        app.MapGet("/api/admin/sql", Sql);

        // #region store-endpoint
        // The document store's operations, newest first: container, kind, the query
        // shape, whether it was pinned to one partition or fanned out, and the request
        // charge beside the milliseconds. Empty on a relational container, exactly as
        // the SQL list is empty on the document one; the page shows whichever the
        // container is (ADR: What the store is actually doing).
        app.MapGet("/api/admin/store", Store);
        // #endregion store-endpoint

        // The raw log lines, newest first, exactly as the console got them.
        app.MapGet("/api/admin/logs", Logs);

        // #region kept-rings-endpoint
        // The same four lists over a day, a week or a month, read back from the
        // document store (ADR: Logs that outlive the container, the addendum on the cards). card is one of errors,
        // logs, sql or store and window one of 24h, 7d or 30d; both pick from a fixed
        // list and neither reaches the store as anything but a parameter. Public,
        // because every entry is what the ring beside it already serves in public.
        app.MapGet("/api/admin/kept", Kept);
        // #endregion kept-rings-endpoint

        // Timing, computed on read from the two rings. The window is whatever the
        // rings currently hold, which the page states rather than implying.
        app.MapGet("/api/admin/metrics", Metrics);

        // #region activity-endpoints
        // Site activity (ADR: Site activity, and the line an address does not cross).
        // The public one: requests over time split by store, the totals, the top
        // paths, the bot and human counts, and what the feature has cost. It names
        // nobody, so it is as public as the rest of this tab. The window is a name
        // and not a number, so a caller cannot ask for a year.
        // A report is kept thirty seconds and rebuilt behind the next read for ten
        // minutes after that, so a reader never waits on the visitor rows being counted
        // (ActivityReportCache, the addendum of 28 September).
        app.MapGet("/api/admin/activity", Activity);

        // The visitor rows, behind the operator's key: a token that rotates daily,
        // the network to three octets, the store, the counts and the top paths. The
        // key is presented as a query parameter or a header and compared in constant
        // time; a wrong key, a missing key and an unconfigured key are all a 404, so
        // a stranger cannot tell the endpoint exists. This is the one Admin read that
        // is not public, because a network range beside a timestamp on a public page
        // can name an employer, and the tab is public by design (ADR-054).
        app.MapGet("/api/admin/activity/visitors", Visitors);
        // #endregion activity-endpoints

        // #region kept-logs-endpoints
        // The kept log (ADR: Logs that outlive the container), behind the same key
        // as the visitor rows and for the same reason: a request log names a network
        // beside a path and a time. A window, and optionally a kind, a status and a
        // fragment of the path, which travel to the store as parameters and never as
        // syntax. The answer says whether anything is kept at all, so a container
        // with no document store shows an honest card rather than an empty table.
        app.MapGet("/api/admin/logs/kept", KeptLogs);
        // #endregion kept-logs-endpoints

        // #region stores-endpoints
        // The Store bar at the top of the page (ADR: One container, both stores, and
        // its addendum on the toggle moving to the sites). What stores this container
        // runs, which one is this site's default, which one this request is on, and
        // the other site's address. There is no switch endpoint any more: the bar's
        // other segment is a link to the other site, so the address bar changes and
        // each site stays one store's site. A cookie the old toggle set is expired
        // here, on the first page load that carries it.
        app.MapGet("/api/stores", Stores)
            .WithName("GetStores")
            .WithTags("Stores")
            .WithSummary("Which stores this container runs, and which one this request is on")
            .WithDescription("A request names a store with the X-Yard-Store header (sql or cosmos) or gets the site's default. "
                + "The other site, if there is one, is the same code with the other default.");
        // #endregion stores-endpoints
        #endregion admin-observability-endpoints

        app.MapGet("/api/admin/azure", Azure);

        // #region peer-endpoint
        // The other container's metrics, read server side with a short patience, so
        // the comparison card can put both backends on the same rows whichever tab
        // is open. The peer's address is configuration on this container and never
        // reaches the browser (ADR: Backends, side by side).
        app.MapGet("/api/admin/peer", Peer);
        // #endregion peer-endpoint

        // #region proof-endpoints
        // The performance proof (ADR: Same performance, proven): read the last result
        // or the run in progress, or start one. Reading is public like the rest of
        // the Admin tab. Starting is a write, sixteen bids in the stores, so it takes
        // a signed-in visitor, the same rule every other write here follows (ADR: The
        // one write a stranger can make, addendum); it answers 409 while a run is on
        // or for a minute after one, so the card is never asked to prove the same
        // thing twice at once.

        // #region machines-endpoint
        // What the three machines under this site are doing, each reporting the way
        // that machine actually reports: the container from the runtime, the
        // relational store from its own resource view, the document store from what
        // its operations charged, because it has no memory reading to give
        // (ADR: What the machines are doing).
        app.MapGet("/api/admin/machines", Machines)
            .WithName("GetMachines")
            .WithTags("Admin")
            .WithSummary("What the container and the two stores are doing, each as that machine reports itself, over the last hour or a kept window of 24h, 7d or 30d");
        // #endregion machines-endpoint

        // #region page-status-endpoints
        // The last sweep, whoever asked for it. Public, like every other reading on
        // this tab: it names addresses this site already serves to anybody.
        app.MapGet("/api/admin/pages", PageStatus)
            .WithName("GetPageStatus")
            .WithTags("Admin")
            .WithSummary("Every address this container serves, as the last sweep found it");

        // And one on demand. 409 rather than an error when a sweep is already running
        // or the last one is inside the cooldown, which is what the proof's start
        // does and what the card's button expects.
        app.MapPost("/api/admin/pages", RunPageStatus)
            .WithName("RunPageStatus")
            .WithTags("Admin")
            .WithSummary("Check every address this container serves, now");
        // #endregion page-status-endpoints
        // #region test-results
        // Every test the ship's gate ran for this build, as the gate wrote it: the
        // suites, their counts and times, and each test with its outcome and its
        // milliseconds. The file ships in the image beside the dataset and is read on
        // each request, because it is small and never changes inside a container.
        // Public like every reading on this tab; the tests are in the public
        // repository already. A build with no file says so rather than inventing one.
        // #region test-summary
        // The same file, added up: what the landing page shows a reader in its first
        // screen (1.0.2.0). The whole results file is 160 KB and the strip needs six
        // numbers, so this reads the counts and nothing else, and the answer is cached
        // until the file changes, which inside a container it never does.
        app.MapGet("/api/tests/summary", TestSummaryRead)
            .WithName("GetTestSummary")
            .WithTags("Admin")
            .WithSummary("The gate's counts for this build, without the tests themselves")
            .WithDescription("The landing page's evidence strip: the suites and their totals, read from the same file the Admin tab's tests card reads.")
            .Produces<TestSummaryReport>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
        // #endregion test-summary

        app.MapGet("/api/admin/tests", TestResults)
            .WithName("GetTestResults")
            .WithTags("Admin")
            .WithSummary("Every test the ship's gate ran for this build, and its result");
        // #endregion test-results
        app.MapGet("/api/admin/proof", ProofStatus);
        app.MapPost("/api/admin/proof", ProofStart).RequireAuthorization();
        // #endregion proof-endpoints

        // #region experiment-endpoint
        // The partition key, live: seven queries against the 100,000-document
        // catalogue, with the request charge beside each, run with this container's
        // own identity and cached for a minute (ADR: The partition key). On a
        // relational container it says so and shows nothing, which is the card's
        // fourth empty state.
        app.MapGet("/api/admin/experiment", ExperimentRun);
        // #endregion experiment-endpoint

        // #region costs-endpoint
        // What Azure charges for the site, over a day, a week or a month (ADR: What
        // Azure charges). Public like the rest of this tab, on Steve's word that the
        // bill is nothing to hide: the dollar figure belongs beside the millisecond
        // one. Served from the kept days, never from Azure on the request: the recorder
        // asks Cost Management once an hour. The window is a name from a fixed list.
        app.MapGet("/api/admin/costs", Costs)
            .WithName("GetCosts")
            .WithTags("Admin")
            .WithSummary("What Azure charges for the site over 24h, 7d or 30d: the spend and forecast, each resource, and the resources by type");
        // #endregion costs-endpoint

        #region telemetry-endpoint
        // The last hour as Application Insights has it, for the Admin tab (ADR-024).
        // Answers a shape the card can render even when telemetry is off or the query
        // fails, because a panel that reports on the system must not be able to break
        // the page it reports from.
        app.MapGet("/api/admin/telemetry", Telemetry);
        #endregion telemetry-endpoint

        return app;
    }

    private static IResult Sql(SqlRingBuffer sqlLog) =>
        Results.Json(sqlLog.Snapshot());

    private static IResult Store(Backends backends, StoreRingBuffer storeLog) =>
        Results.Json(new
        {
            store = backends.Named("cosmos")?.Name ?? backends.Default.Name,
            operations = storeLog.Snapshot(),
        });

    private static IResult Logs(LogRingBuffer logLog) =>
        Results.Json(logLog.Snapshot());

    private static async Task<IResult> Kept(string? card, string? window, KeptRingReader reader, CancellationToken cancellation) =>
        await reader.ReadAsync(card, window, DateTimeOffset.UtcNow, cancellation) is { } answer
            ? Results.Json(answer)
            : Results.Problem(detail: "card is one of errors, logs, sql or store, and window is one of 24h, 7d or 30d.", statusCode: 400, title: "The card or the window could not be read");

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

    private static async Task<IResult> Activity(string? window, ActivityCollector collector, ActivityReportCache activityReports, Backends backends, AdminSettings admin, CancellationToken cancellation) =>
        ActivityWindows.Parse(window) is null
            ? Results.Problem(detail: "window is one of 24h, 7d or 30d.", statusCode: 400, title: "The window could not be read")
            : Results.Json(await activityReports.GetAsync(
                window ?? "24h",
                (now, building) => ActivityReport.PublicAsync(collector, backends, window ?? "24h", now, admin.VisitorRows, building),
                cancellation));

    private static async Task<IResult> Visitors(string? window, string? key, HttpContext http, ActivityCollector collector, Backends backends, AdminKey adminKey, AdminSettings admin, CancellationToken cancellation)
    {
        string? presented = key ?? http.Request.Headers["X-Admin-Key"].FirstOrDefault();
        if (!admin.VisitorRows || !adminKey.Admits(presented))
        {
            return Results.NotFound();
        }

        return ActivityWindows.Parse(window) is null
            ? Results.Problem(detail: "window is one of 24h, 7d or 30d.", statusCode: 400, title: "The window could not be read")
            : Results.Json(await ActivityReport.VisitorsAsync(collector, backends, window ?? "24h", DateTimeOffset.UtcNow, cancellation));
    }

    private static async Task<IResult> KeptLogs(string? window, string? kind, int? status, string? path, string? key, HttpContext http, LogCollector collector, AdminKey adminKey, AdminSettings admin, CancellationToken cancellation)
    {
        string? presented = key ?? http.Request.Headers["X-Admin-Key"].FirstOrDefault();
        if (!admin.VisitorRows || !adminKey.Admits(presented))
        {
            return Results.NotFound();
        }

        if (ActivityWindows.Parse(window) is null)
        {
            return Results.Problem(detail: "window is one of 24h, 7d or 30d.", statusCode: 400, title: "The window could not be read");
        }

        if (!string.IsNullOrEmpty(kind) && !LogEvent.Kinds.Contains(kind, StringComparer.Ordinal))
        {
            return Results.Problem(detail: "kind is one of request, error or app.", statusCode: 400, title: "The kind could not be read");
        }

        return Results.Json(await LogReport.QueryAsync(collector, window ?? "24h", kind, status, path, DateTimeOffset.UtcNow, cancellation));
    }

    private static Ok<StoresView> Stores(HttpContext http, Backends backends)
    {
        Backends.ExpireLegacyCookie(http);
        return TypedResults.Ok(backends.Describe(http));
    }

    private static async Task<IResult> Azure(AzureSelf azureSelf) =>
        Results.Json(await azureSelf.GetStateAsync());

    private static async Task<IResult> Peer(PeerReader peer) =>
        Results.Json(await peer.ReadAsync());

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

    private static IResult PageStatus(PageStatusRunner pageStatus) =>
        Results.Json(pageStatus.Status);

    private static IResult RunPageStatus(PageStatusRunner pageStatus) =>
        pageStatus.TryStart("asked")
        ? Results.Accepted("/api/admin/pages")
        : Results.Conflict(new { status = "a sweep is already running, was run in the last twenty seconds, or this container has no address of its own to dial" });

    private static IResult TestSummaryRead(HostPaths paths)
    {
        if (!File.Exists(paths.TestResultsPath))
        {
            return Results.Problem(
                detail: "No test results shipped with this build. The ship's gate writes them.",
                statusCode: StatusCodes.Status404NotFound,
                title: "No test results");
        }

        return Results.Json(TestSummary.Of(paths.TestResultsPath), WireFormat);
    }

    private static IResult TestResults(HostPaths paths) =>
        File.Exists(paths.TestResultsPath)
        ? Results.Text(File.ReadAllText(paths.TestResultsPath), "application/json")
        : Results.Problem(
            detail: "No test results shipped with this build. The ship's gate writes them.",
            statusCode: StatusCodes.Status404NotFound,
            title: "No test results");

    private static IResult ProofStatus(ProofRunner proof) =>
        Results.Json(proof.Status);

    private static IResult ProofStart(int? rounds, ProofRunner proof) =>
        proof.TryStart(rounds ?? ProofRunner.DefaultRounds)
        ? Results.Json(new { status = "running" }, WireFormat, statusCode: StatusCodes.Status202Accepted)
        : Results.Problem(
            detail: "A run is in progress, or the last one finished less than a minute ago. The result is on the card.",
            statusCode: StatusCodes.Status409Conflict,
            title: "The proof is busy");

    private static async Task<IResult> ExperimentRun(IServiceProvider services)
    {
        var cosmos = services.GetService<CosmosStore>();
        return cosmos is null
            ? Results.Json(new { available = false, reason = "this container is not on Azure Cosmos DB", rows = Array.Empty<object>() })
            : Results.Json(await Experiment.RunAsync(cosmos));
    }

    private static async Task<IResult> Costs(string? window, CostHistoryReader costs, CancellationToken cancellation) =>
        Results.Json(await costs.ReadAsync(window, cancellation));

    private static async Task<IResult> Telemetry(TelemetryReader telemetry) =>
        Results.Json(await telemetry.GetRecentAsync());
}
