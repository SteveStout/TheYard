// The host's answers to the Admin tab's queries (ADR-115's fourth rung). Each class implements one port the
// Application ring declares, over the part that holds the data: the rings, the collectors, the gate's file,
// Azure. The bodies are the routes' bodies as they were, moved, so every card reads what it read before; what
// a route decided (a window, a card, a kind) the handler decides now.
using System.Text.Json;
using TheYard.Application;
using TheYard.Application.Features.Admin.Costs;
using TheYard.Application.Features.Admin.Operations;
using TheYard.Application.Features.Proof;

namespace TheYard.Api.Features.Admin.Operations;

// #region operations-readings
/// <summary>The process's own rings.</summary>
public sealed class RingReadings(SqlRingBuffer sql, StoreRingBuffer store, LogRingBuffer logs, RequestRingBuffer requests, Backends backends, HostStart start, IHttpContextAccessor accessor) : IRingReadings
{
    /// <inheritdoc/>
    public object Sql() => sql.Snapshot();

    /// <inheritdoc/>
    public object Store() => new
    {
        store = backends.Named(StoreKeys.Document)?.Name ?? backends.Default.Name,
        operations = store.Snapshot(),
    };

    /// <inheritdoc/>
    public object Logs() => logs.Snapshot();

    /// <inheritdoc/>
    public object Metrics() =>
        MetricsReport.Build(backends, backends.For(accessor.HttpContext), requests.Snapshot(), sql.Snapshot(), store.Snapshot(), start.At);
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
        var answer = JsonSerializer.SerializeToNode(await self.GetStateAsync())!.AsObject();
        answer["hourly"] = hourly.View is { } view ? JsonSerializer.SerializeToNode(view) : null;
        return answer;
    }

    /// <inheritdoc/>
    public async Task<object> PeerAsync(CancellationToken cancellation) => await peer.ReadAsync();

    /// <inheritdoc/>
    public Task<object> TelemetryAsync(CancellationToken cancellation) => telemetry.GetRecentAsync();
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
