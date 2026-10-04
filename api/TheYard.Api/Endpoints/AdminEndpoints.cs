// The Admin tab's routes, and the place to start reading them. This file holds the class, the
// wire format two replies share, and the map of every route. The handlers live beside it, one
// file per card group:
//   AdminEndpoints.Observability.cs      the rings, the timing, the Store bar, Azure, the peer, the machines
//   AdminEndpoints.Activity.cs           site activity, the visitor rows and the kept log
//   AdminEndpoints.ProofAndPages.cs      the page sweep, the tests, the proof and the experiment
//   AdminEndpoints.CostsAndTelemetry.cs  what Azure charges, and the last hour from Application Insights

using System.Text.Json;

namespace TheYard.Api;

/// <summary>
/// The Admin tab's reads (the rings, the timing, the activity, the machines, the
/// kept logs, the tests, the page sweep, the proof) and the Store bar's answer.
/// Every one reads what the host already keeps; the per-visitor rows and the kept
/// log answer only to the operator's key.
/// </summary>
public static partial class AdminEndpoints
{
    /// <summary>
    /// The wire format for the two replies written by hand (the test summary and the
    /// proof's start), which keep the dataset's snake_case shape.
    /// </summary>
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
        // the other site's address. There is no switch endpoint: the bar's other
        // segment is a link to the other site, so the address bar changes and each
        // site stays one store's site. A stale yard-store cookie is expired here, on
        // the first page load that carries it.
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
        // screen. The whole results file is 160 KB and the strip needs six
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
        // Azure charges). Public like the rest of this tab, on the owner's rule that the
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
}
