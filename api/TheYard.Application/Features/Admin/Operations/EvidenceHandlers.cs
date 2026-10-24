// The Admin tab's reads of the machines, Azure, the request and the gate's evidence, and the two runs it starts,
// one query or command and one handler each (ADR-115's fourth rung). The rings and the activity are in
// OperationsHandlers.cs; the two files are one slice cut by what the handlers read.
using TheYard.Application.Shared;

namespace TheYard.Application.Features.Admin.Operations;

// #region machine-handlers
/// <summary>The machines card.</summary>
/// <param name="machines">The machines.</param>
public sealed class ReadMachinesHandler(IMachineReadings machines) : IQueryHandler<ReadMachinesQuery, Reading>
{
    /// <summary>The card over the window it names, the last hour when it names none it keeps.</summary>
    /// <param name="query">The window.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<Reading> HandleAsync(ReadMachinesQuery query, CancellationToken cancellation) => Reading.Of(await machines.MachinesAsync(query.Window, cancellation));
}

/// <summary>Asks for the machines card.</summary>
/// <param name="Window">1h, 24h, 7d or 30d.</param>
public sealed record ReadMachinesQuery(string? Window);

/// <summary>The memory-against-traffic card.</summary>
/// <param name="machines">The machines.</param>
public sealed class ReadFollowsHandler(IMachineReadings machines) : IQueryHandler<ReadFollowsQuery, Reading>
{
    /// <summary>The card over the window it names, the last hour when it names none it keeps.</summary>
    /// <param name="query">The window.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<Reading> HandleAsync(ReadFollowsQuery query, CancellationToken cancellation) => Reading.Of(await machines.FollowsAsync(query.Window, cancellation));
}

/// <summary>Asks for the memory-against-traffic card.</summary>
/// <param name="Window">1h, 24h, 7d or 30d.</param>
public sealed record ReadFollowsQuery(string? Window);
// #endregion machine-handlers

// #region azure-handlers
/// <summary>This container as Azure has it.</summary>
/// <param name="azure">What Azure and the other container say.</param>
public sealed class ReadAzureSelfHandler(IAzureReadings azure) : IQueryHandler<ReadAzureSelfQuery, Reading>
{
    /// <summary>The identity, the state and the hourly readings.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<Reading> HandleAsync(ReadAzureSelfQuery query, CancellationToken cancellation) => Reading.Of(await azure.SelfAsync(cancellation));
}

/// <summary>Asks for this container as Azure has it.</summary>
public sealed record ReadAzureSelfQuery;

/// <summary>The other container's metrics.</summary>
/// <param name="azure">What Azure and the other container say.</param>
public sealed class ReadPeerHandler(IAzureReadings azure) : IQueryHandler<ReadPeerQuery, Reading>
{
    /// <summary>The peer as it answered.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<Reading> HandleAsync(ReadPeerQuery query, CancellationToken cancellation) => Reading.Of(await azure.PeerAsync(cancellation));
}

/// <summary>Asks for the other container.</summary>
public sealed record ReadPeerQuery;

/// <summary>The last hour as Application Insights has it.</summary>
/// <param name="azure">What Azure and the other container say.</param>
public sealed class ReadTelemetryHandler(IAzureReadings azure) : IQueryHandler<ReadTelemetryQuery, Reading>
{
    /// <summary>The telemetry as it answered.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<Reading> HandleAsync(ReadTelemetryQuery query, CancellationToken cancellation) => Reading.Of(await azure.TelemetryAsync(cancellation));
}

/// <summary>Asks for the telemetry.</summary>
public sealed record ReadTelemetryQuery;
// #endregion azure-handlers

// #region request-handlers
/// <summary>The request's own arrival, for the operator alone; the route checks the key before it asks.</summary>
/// <param name="request">The request itself.</param>
public sealed class ReadArrivalHandler(IRequestReadings request) : IQueryHandler<ReadArrivalQuery, Reading>
{
    /// <summary>The addresses and schemes each hop wrote.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<Reading> HandleAsync(ReadArrivalQuery query, CancellationToken cancellation) => Task.FromResult(Reading.Of(request.Arrival()));
}

/// <summary>Asks how the request arrived.</summary>
public sealed record ReadArrivalQuery;

/// <summary>The store bar: the stores this container runs and the one this request is on.</summary>
/// <param name="request">The request itself.</param>
public sealed class ReadStoresHandler(IRequestReadings request) : IQueryHandler<ReadStoresQuery, Reading>
{
    /// <summary>The stores.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<Reading> HandleAsync(ReadStoresQuery query, CancellationToken cancellation) => Task.FromResult(Reading.Of(request.Stores()));
}

/// <summary>Asks for the store bar.</summary>
public sealed record ReadStoresQuery;
// #endregion request-handlers

// #region evidence-handlers
/// <summary>Every address as the last sweep found it.</summary>
/// <param name="evidence">The checks as they stand.</param>
public sealed class ReadPageStatusHandler(IEvidenceReadings evidence) : IQueryHandler<ReadPageStatusQuery, Reading>
{
    /// <summary>The sweep's result.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<Reading> HandleAsync(ReadPageStatusQuery query, CancellationToken cancellation) => Task.FromResult(Reading.Of(evidence.PageStatus()));
}

/// <summary>Asks for the page sweep's result.</summary>
public sealed record ReadPageStatusQuery;

/// <summary>Starts a sweep of every address, or says why not.</summary>
/// <param name="runs">The checks this process starts.</param>
public sealed class RunPageSweepHandler(IEvidenceRuns runs) : ICommandHandler<RunPageSweepCommand, Reading>
{
    /// <summary>Started, or busy.</summary>
    /// <param name="command">The command; it carries nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<Reading> HandleAsync(RunPageSweepCommand command, CancellationToken cancellation) =>
        Task.FromResult(runs.StartPageSweep()
            ? new Reading(ReadingKind.Started)
            : new Reading(ReadingKind.Busy, Detail: "a sweep is already running, was run in the last twenty seconds, or this container has no address of its own to dial"));
}

/// <summary>Starts a sweep of every address.</summary>
public sealed record RunPageSweepCommand;

/// <summary>The gate's counts for this build, or none when no results shipped with it.</summary>
/// <param name="evidence">The checks as they stand.</param>
public sealed class ReadTestSummaryHandler(IEvidenceReadings evidence) : IQueryHandler<ReadTestSummaryQuery, Reading>
{
    /// <summary>The counts, or missing.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<Reading> HandleAsync(ReadTestSummaryQuery query, CancellationToken cancellation) =>
        Task.FromResult(evidence.TestSummary() is { } summary ? Reading.Of(summary) : NoResults());

    /// <summary>What a build with no results says.</summary>
    public static Reading NoResults() => new(ReadingKind.Missing, Title: "No test results", Detail: "No test results shipped with this build. The ship's gate writes them.");
}

/// <summary>Asks for the gate's counts.</summary>
public sealed record ReadTestSummaryQuery;

/// <summary>The gate's results file, or none when no results shipped with this build.</summary>
/// <param name="evidence">The checks as they stand.</param>
public sealed class ReadTestResultsHandler(IEvidenceReadings evidence) : IQueryHandler<ReadTestResultsQuery, Reading>
{
    /// <summary>The file's text, or missing.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public Task<Reading> HandleAsync(ReadTestResultsQuery query, CancellationToken cancellation) =>
        Task.FromResult(evidence.TestResults() is { } text ? Reading.Of(text) : ReadTestSummaryHandler.NoResults());
}

/// <summary>Asks for the gate's results file.</summary>
public sealed record ReadTestResultsQuery;

/// <summary>Runs the partition key experiment and answers what it measured.</summary>
/// <param name="runs">The checks this process starts.</param>
public sealed class RunExperimentHandler(IEvidenceRuns runs) : ICommandHandler<RunExperimentCommand, Reading>
{
    /// <summary>What the experiment measured.</summary>
    /// <param name="command">The command; it carries nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<Reading> HandleAsync(RunExperimentCommand command, CancellationToken cancellation) => Reading.Of(await runs.RunExperimentAsync(cancellation));
}

/// <summary>Runs the partition key experiment.</summary>
public sealed record RunExperimentCommand;
// #endregion evidence-handlers
