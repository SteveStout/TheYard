using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Application.Features.Admin.Costs;
using TheYard.Application.Features.Admin.Operations;
using TheYard.Application.Features.Docs;
using TheYard.Application.Features.Proof;
using TheYard.Application.Shared;

namespace TheYard.Tests;

// #region admin-query-tests
/// <summary>
/// The Admin tab's 24 reads as queries (ADR-115's fourth rung): every handler over fakes at its port and no host,
/// holding what each route used to decide in its body (a window, a card, a kind, nothing to read, a busy run),
/// with no host; the statuses the newly ruled routes declare are held through the real host in AdminQueryRouteTests.
/// </summary>
public sealed class AdminQueryTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task The_ring_reads_answer_what_the_rings_hold()
    {
        var fake = new Fake();
        Assert.Equal("sql", (await new ReadSqlRingHandler(fake).HandleAsync(new ReadSqlRingQuery(), None)).Body);
        Assert.Equal("store", (await new ReadStoreRingHandler(fake).HandleAsync(new ReadStoreRingQuery(), None)).Body);
        Assert.Equal("logs", (await new ReadLogRingHandler(fake).HandleAsync(new ReadLogRingQuery(), None)).Body);
        Assert.Equal("metrics", (await new ReadMetricsHandler(fake).HandleAsync(new ReadMetricsQuery(), None)).Body);
    }

    [Fact]
    public async Task A_kept_ring_the_reader_does_not_keep_is_refused()
    {
        var fake = new Fake();
        Assert.Equal(ReadingKind.Read, (await new ReadKeptRingHandler(fake).HandleAsync(new ReadKeptRingQuery("errors", "24h"), None)).Kind);
        var refused = await new ReadKeptRingHandler(fake).HandleAsync(new ReadKeptRingQuery("nonsense", "24h"), None);
        Assert.Equal(ReadingKind.Refused, refused.Kind);
        Assert.Equal("The card or the window could not be read", refused.Title);
    }

    [Fact]
    public async Task The_activity_reads_refuse_a_window_they_do_not_read_and_a_kind_the_log_does_not_keep()
    {
        var fake = new Fake();
        Assert.Equal("public 24h", (await new ReadActivityHandler(fake).HandleAsync(new ReadActivityQuery(null), None)).Body);
        Assert.Equal(ReadingKind.Refused, (await new ReadActivityHandler(fake).HandleAsync(new ReadActivityQuery("1y"), None)).Kind);
        Assert.Equal("visitors 7d", (await new ReadVisitorRowsHandler(fake).HandleAsync(new ReadVisitorRowsQuery("7d"), None)).Body);
        Assert.Equal(ReadingKind.Refused, (await new ReadVisitorRowsHandler(fake).HandleAsync(new ReadVisitorRowsQuery("2d"), None)).Kind);
        Assert.Equal("log 30d", (await new ReadKeptLogsHandler(fake).HandleAsync(new ReadKeptLogsQuery("30d", "error", null, null), None)).Body);
        var kind = await new ReadKeptLogsHandler(fake).HandleAsync(new ReadKeptLogsQuery("30d", "nonsense", null, null), None);
        Assert.Equal(("The kind could not be read", ReadingKind.Refused), (kind.Title, kind.Kind));
    }

    [Fact]
    public async Task The_machine_azure_and_request_reads_answer_their_port()
    {
        var fake = new Fake();
        Assert.Equal("machines 7d", (await new ReadMachinesHandler(fake).HandleAsync(new ReadMachinesQuery("7d"), None)).Body);
        Assert.Equal("follows 1h", (await new ReadFollowsHandler(fake).HandleAsync(new ReadFollowsQuery("1h"), None)).Body);
        Assert.Equal("self", (await new ReadAzureSelfHandler(fake).HandleAsync(new ReadAzureSelfQuery(), None)).Body);
        Assert.Equal("peer", (await new ReadPeerHandler(fake).HandleAsync(new ReadPeerQuery(), None)).Body);
        Assert.Equal("telemetry", (await new ReadTelemetryHandler(fake).HandleAsync(new ReadTelemetryQuery(), None)).Body);
        Assert.Equal("arrival", (await new ReadArrivalHandler(fake).HandleAsync(new ReadArrivalQuery(), None)).Body);
        Assert.Equal("stores", (await new ReadStoresHandler(fake).HandleAsync(new ReadStoresQuery(), None)).Body);
        Assert.Equal("costs 30d", (await new ReadCostsHandler(fake).HandleAsync(new ReadCostsQuery("30d"), None)).Body);
    }

    [Fact]
    public async Task The_evidence_reads_say_missing_when_no_results_shipped_and_the_runs_say_busy()
    {
        var fake = new Fake();
        Assert.Equal("pages", (await new ReadPageStatusHandler(fake).HandleAsync(new ReadPageStatusQuery(), None)).Body);
        Assert.Equal("summary", (await new ReadTestSummaryHandler(fake).HandleAsync(new ReadTestSummaryQuery(), None)).Body);
        Assert.Equal("{}", (await new ReadTestResultsHandler(fake).HandleAsync(new ReadTestResultsQuery(), None)).Body);
        Assert.Equal("experiment", (await new RunExperimentHandler(fake).HandleAsync(new RunExperimentCommand(), None)).Body);
        Assert.Equal("proof", (await new ReadProofStatusHandler(fake).HandleAsync(new ReadProofStatusQuery(), None)).Body);
        Assert.Equal(ReadingKind.Started, (await new RunPageSweepHandler(fake).HandleAsync(new RunPageSweepCommand(), None)).Kind);
        Assert.Equal(ReadingKind.Started, (await new StartProofHandler(fake).HandleAsync(new StartProofCommand(3), None)).Kind);

        var empty = new Fake { Shipped = false, Free = false };
        Assert.Equal(ReadingKind.Missing, (await new ReadTestSummaryHandler(empty).HandleAsync(new ReadTestSummaryQuery(), None)).Kind);
        Assert.Equal(ReadingKind.Missing, (await new ReadTestResultsHandler(empty).HandleAsync(new ReadTestResultsQuery(), None)).Kind);
        Assert.Equal(ReadingKind.Busy, (await new RunPageSweepHandler(empty).HandleAsync(new RunPageSweepCommand(), None)).Kind);
        Assert.Equal("The proof is busy", (await new StartProofHandler(empty).HandleAsync(new StartProofCommand(3), None)).Title);
    }

    [Fact]
    public async Task A_document_with_no_slug_in_the_catalogue_is_missing()
    {
        var fake = new Fake();
        Assert.Equal("# readme", (await new ReadDocumentHandler(fake).HandleAsync(new ReadDocumentQuery("readme"), None)).Body);
        Assert.Equal(ReadingKind.Missing, (await new ReadDocumentHandler(fake).HandleAsync(new ReadDocumentQuery("nobody-wrote-this"), None)).Kind);
    }

    /// <summary>Every port at once, answering a word for each reading, so a test reads what a handler did with it.</summary>
    private sealed class Fake : IRingReadings, IKeptRingReadings, IActivityReadings, IMachineReadings, IAzureReadings, IRequestReadings, IEvidenceReadings, IEvidenceRuns, IProofReadings, IProofRuns, ICostReadings, IDocumentReadings
    {
        public bool Shipped { get; init; } = true;

        public bool Free { get; init; } = true;

        public object Sql() => "sql";

        public object Store() => "store";

        public object Logs() => "logs";

        public object Metrics() => "metrics";

        public Task<object?> ReadAsync(string? card, string? window, CancellationToken cancellation) => Task.FromResult<object?>(card == "errors" ? "kept" : null);

        public Task<object> PublicAsync(string window, CancellationToken cancellation) => Task.FromResult<object>("public " + window);

        public Task<object> VisitorsAsync(string window, CancellationToken cancellation) => Task.FromResult<object>("visitors " + window);

        public Task<object?> KeptLogsAsync(string window, string? kind, int? status, string? path, CancellationToken cancellation) =>
            Task.FromResult<object?>(kind is null or "request" or "error" or "app" ? "log " + window : null);

        public Task<object> MachinesAsync(string? window, CancellationToken cancellation) => Task.FromResult<object>("machines " + window);

        public Task<object> FollowsAsync(string? window, CancellationToken cancellation) => Task.FromResult<object>("follows " + window);

        public Task<object> SelfAsync(CancellationToken cancellation) => Task.FromResult<object>("self");

        public Task<object> PeerAsync(CancellationToken cancellation) => Task.FromResult<object>("peer");

        public Task<object> TelemetryAsync(CancellationToken cancellation) => Task.FromResult<object>("telemetry");

        public object Arrival() => "arrival";

        public object Stores() => "stores";

        public object PageStatus() => "pages";

        public object? TestSummary() => Shipped ? "summary" : null;

        public string? TestResults() => Shipped ? "{}" : null;

        public bool StartPageSweep() => Free;

        public Task<object> RunExperimentAsync(CancellationToken cancellation) => Task.FromResult<object>("experiment");

        public object Status() => "proof";

        public bool Start(int rounds) => Free;

        Task<object> ICostReadings.ReadAsync(string? window, CancellationToken cancellation) => Task.FromResult<object>("costs " + window);

        public string? Render(string slug) => slug == "readme" ? "# readme" : null;
    }
}

/// <summary>The statuses the Admin tab's newly ruled routes declare, through the real host (ADR-115's fourth rung).</summary>
public sealed class AdminQueryRouteTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    [Covers("GET", "/api/stores", 200)]
    [Covers("GET", "/api/tests/summary", 200)]
    public async Task The_store_bar_and_the_gate_s_counts_answer_200()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/stores")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/tests/summary")).StatusCode);
    }

    [Fact]
    [Covers("GET", "/api/tests/summary", 404)]
    public async Task A_build_with_no_test_results_answers_404_for_the_counts()
    {
        var paths = HostPaths.Find(Path.Combine(Repo.Root(), "api", "TheYard.Api")) with { TestResultsPath = Path.Combine(Path.GetTempPath(), "no-test-results-here.json") };
        var client = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => services.AddSingleton(paths))).CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/tests/summary")).StatusCode);
    }

    [Fact]
    [Covers("GET", "/api/docs/{slug}", 200)]
    [Covers("GET", "/api/docs/{slug}", 404)]
    public async Task A_document_in_the_catalogue_answers_200_and_a_slug_it_does_not_hold_404()
    {
        var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/docs/adr-a-request-is-a-query-or-a-command")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/docs/no-such-document")).StatusCode);
    }
}
// #endregion admin-query-tests
