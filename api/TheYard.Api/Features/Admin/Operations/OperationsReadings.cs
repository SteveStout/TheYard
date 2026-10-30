// The host's answers to the Admin tab's queries (ADR-115's fourth rung). Each class implements one port the
// Application ring declares, over the part that holds the data: the rings, the collectors, the gate's file,
// Azure. The bodies are the routes' bodies as they were, moved, so every card reads what it read before; what
// a route decided (a window, a card, a kind) the handler decides now.
using System.Text.Json;
using TheYard.Application.Features.Admin.Costs;

namespace TheYard.Api.Features.Admin.Operations;

// #region operations-readings
/// <summary>
/// The process's own rings, handed out as the three ring sections and the metrics report read
/// them. It is handed <see cref="HostRings"/> rather than a buffer per ring: every one of its
/// four answers reads the rings and nothing else about them, so the rings are one collaborator
/// here and not four (ADR: One host per process, the rings under them).
/// </summary>
/// <param name="rings">The four rings this process keeps.</param>
/// <param name="backends">The stores, for the document store's name and the request's own store.</param>
/// <param name="start">When this process started, which the metrics report dates its window from.</param>
/// <param name="accessor">The request in flight, whose store the metrics report is built for.</param>
public sealed class RingReadings(HostRings rings, Backends backends, HostStart start, IHttpContextAccessor accessor) : IRingReadings
{
    /// <inheritdoc/>
    public object Sql() => rings.Sql.Snapshot();

    /// <inheritdoc/>
    public object Store() => new
    {
        store = backends.Named(StoreKeys.Document)?.Name ?? backends.Default.Name,
        operations = rings.Store.Snapshot(),
    };

    /// <inheritdoc/>
    public object Logs() => rings.Logs.Snapshot();

    /// <inheritdoc/>
    public object Metrics() =>
        MetricsReport.Build(backends, backends.For(accessor.HttpContext), rings.Requests.Snapshot(), rings.Sql.Snapshot(), rings.Store.Snapshot(), start.At);
}

/// <summary>The kept rings.</summary>
public sealed class KeptRingReadings(KeptRingReader reader) : IKeptRingReadings
{
    /// <inheritdoc/>
    public Task<object?> ReadAsync(string? card, string? window, CancellationToken cancellation) => reader.ReadAsync(card, window, Clocks.UtcNow(), cancellation);
}

/// <summary>Who came.</summary>
public sealed class ActivityReadings(ActivityCollector collector, ActivityReportCache reports, Backends backends, AdminSettings admin, LogCollector logs) : IActivityReadings
{
    /// <inheritdoc/>
    public Task<object> PublicAsync(string window, CancellationToken cancellation) =>
        reports.GetAsync(window, (now, building) => ActivityReport.PublicAsync(collector, backends, window, now, admin.VisitorRows, building), cancellation);

    /// <inheritdoc/>
    public Task<object> VisitorsAsync(string window, CancellationToken cancellation) =>
        ActivityReport.VisitorsAsync(collector, backends, window, Clocks.UtcNow(), cancellation);

    /// <inheritdoc/>
    public async Task<object?> KeptLogsAsync(string window, string? kind, int? status, string? path, CancellationToken cancellation) =>
        !string.IsNullOrEmpty(kind) && !LogEvent.Kinds.Contains(kind, StringComparer.Ordinal)
            ? null
            : await LogReport.QueryAsync(logs, window, kind, status, path, Clocks.UtcNow(), cancellation);
}

/// <summary>What Azure and the other container say.</summary>
public sealed class AzureReadings(AzureSelf self, HourlyReadings hourly, PeerReader peer, TelemetryReader telemetry) : IAzureReadings
{
    /// <inheritdoc/>
    public async Task<object> SelfAsync(CancellationToken cancellation)
    {
        var answer = JsonSerializer.SerializeToNode(await self.GetStateAsync(cancellation))!.AsObject();
        answer["hourly"] = hourly.View is { } view ? JsonSerializer.SerializeToNode(view) : null;
        return answer;
    }

    /// <inheritdoc/>
    public async Task<object> PeerAsync(CancellationToken cancellation) => await peer.ReadAsync(cancellation);

    /// <inheritdoc/>
    public Task<object> TelemetryAsync(CancellationToken cancellation) => telemetry.GetRecentAsync(cancellation);
}

/// <summary>The request itself and the store bar.</summary>
public sealed class RequestReadings(IHttpContextAccessor accessor, Backends backends) : IRequestReadings
{
    /// <inheritdoc/>
    public object Arrival()
    {
        var http = accessor.HttpContext!;
        var headers = http.Request.Headers;
        var raw = http.Items[TheYard.Api.Shared.Activity.Arrival.RawKey] as TheYard.Api.Shared.Activity.Arrival.Raw;
        return new ArrivalView(
            raw?.ForwardedFor ?? "",
            raw?.ForwardedProto ?? "",
            http.Connection.RemoteIpAddress?.ToString() ?? "",
            http.Request.Scheme,
            headers["X-Original-For"].ToString(),
            headers["X-Original-Proto"].ToString(),
            headers["X-Forwarded-For"].ToString(),
            headers["X-Forwarded-Proto"].ToString(),
            headers["X-Forwarded-Host"].ToString(),
            headers["X-Nf-Client-Connection-Ip"].ToString());
    }

    /// <inheritdoc/>
    public object Stores() => backends.Describe(accessor.HttpContext);
}

/// <summary>The checks as they stand: the page sweep and the gate's results file.</summary>
public sealed class EvidenceReadings(PageStatusRunner pages, HostPaths paths) : IEvidenceReadings
{
    /// <inheritdoc/>
    public object PageStatus() => pages.Status;

    /// <inheritdoc/>
    public object? TestSummary() => File.Exists(paths.TestResultsPath) ? TheYard.Api.Features.Admin.Operations.TestSummary.Of(paths.TestResultsPath) : null;

    /// <inheritdoc/>
    public string? TestResults() => File.Exists(paths.TestResultsPath) ? File.ReadAllText(paths.TestResultsPath) : null;
}

/// <summary>The checks this process starts.</summary>
public sealed class EvidenceRuns(PageStatusRunner pages, IStoreExperiment experiment) : IEvidenceRuns
{
    /// <inheritdoc/>
    public bool StartPageSweep() => pages.TryStart("asked");

    /// <inheritdoc/>
    public async Task<object> RunExperimentAsync(CancellationToken cancellation) => await experiment.RunAsync();
}

/// <summary>The performance proof.</summary>
public sealed class ProofReadings(ProofRunner proof) : IProofReadings, IProofRuns
{
    /// <inheritdoc/>
    public object Status() => proof.Status;

    /// <inheritdoc/>
    public bool Start(int rounds) => proof.TryStart(rounds);
}

/// <summary>The kept cost days.</summary>
public sealed class CostCardReadings(CostHistoryReader costs) : ICostReadings
{
    /// <inheritdoc/>
    public async Task<object> ReadAsync(string? window, CancellationToken cancellation) => await costs.ReadAsync(window, cancellation);
}
// #endregion operations-readings
