// The ports the Admin tab's readings of the running process are asked through (ADR-115's fourth rung: the 24
// Admin reads that asked no handler). Each reading is already in the shape its card draws, built by the host part
// that holds the data (the rings, the collectors, the gate's file, Azure's answers); the Application ring declares
// what it may ask for, in ports split by what they read, so every query has a port a test can fake and a route
// asks one handler and nothing else: a read with no handler has no fake to test against.
namespace TheYard.Application.Features.Admin.Operations;


// #region operations-ports
/// <summary>The process's own rings: the SQL statements, the store operations, the log lines, and the timings built from them.</summary>
public interface IRingReadings
{
    /// <summary>The SQL ring.</summary>
    object Sql();

    /// <summary>The document store's operation ring, with the store's name.</summary>
    object Store();

    /// <summary>The log ring.</summary>
    object Logs();

    /// <summary>The timings of the request's store and every other, from the request and store rings.</summary>
    object Metrics();
}

/// <summary>The rings each site keeps where a roll cannot empty them.</summary>
public interface IKeptRingReadings
{
    /// <summary>One card's kept ring over a window, or null when the card or the window is not one it keeps.</summary>
    Task<object?> ReadAsync(string? card, string? window, CancellationToken cancellation);
}

/// <summary>Who came: the public report, the visitor rows and the kept log, the last two for the operator alone.</summary>
public interface IActivityReadings
{
    /// <summary>The public activity report over a window.</summary>
    Task<object> PublicAsync(string window, CancellationToken cancellation);

    /// <summary>The visitor rows over a window.</summary>
    Task<object> VisitorsAsync(string window, CancellationToken cancellation);

    /// <summary>The kept log over a window, filtered, or null when the kind is not one the log keeps.</summary>
    Task<object?> KeptLogsAsync(string window, string? kind, int? status, string? path, CancellationToken cancellation);
}

/// <summary>The machines: what the container and the stores are doing, and memory against traffic.</summary>
public interface IMachineReadings
{
    /// <summary>The machines card over the last hour or a kept window.</summary>
    Task<object> MachinesAsync(string? window, CancellationToken cancellation);

    /// <summary>The memory-against-traffic card over the last hour or a kept window.</summary>
    Task<object> FollowsAsync(string? window, CancellationToken cancellation);
}

/// <summary>What Azure and the other container say: this container's identity and hours, the peer, and the telemetry.</summary>
public interface IAzureReadings
{
    /// <summary>This container as Azure has it, with the hourly readings.</summary>
    Task<object> SelfAsync(CancellationToken cancellation);

    /// <summary>The other container's metrics.</summary>
    Task<object> PeerAsync(CancellationToken cancellation);

    /// <summary>The last hour as Application Insights has it.</summary>
    Task<object> TelemetryAsync(CancellationToken cancellation);
}

/// <summary>The request itself and the store bar: how the request arrived, and which stores this container runs.</summary>
public interface IRequestReadings
{
    /// <summary>The request's own arrival: the addresses and schemes each hop wrote.</summary>
    object Arrival();

    /// <summary>The stores this container runs and the one this request is on.</summary>
    object Stores();
}

/// <summary>The checks of the site as they stand: the page sweep and the gate's results.</summary>
public interface IEvidenceReadings
{
    /// <summary>Every address as the last sweep found it.</summary>
    object PageStatus();

    /// <summary>The gate's counts for this build, or null when no results shipped with it.</summary>
    object? TestSummary();

    /// <summary>The gate's results file as text, or null when none shipped.</summary>
    string? TestResults();
}

/// <summary>The checks this process starts: the page sweep and the partition key experiment.</summary>
public interface IEvidenceRuns
{
    /// <summary>Starts a sweep of every address; false when one is running, ran a moment ago, or there is no address to dial.</summary>
    bool StartPageSweep();

    /// <summary>Runs the partition key experiment and answers what it measured.</summary>
    Task<object> RunExperimentAsync(CancellationToken cancellation);
}
// #endregion operations-ports
