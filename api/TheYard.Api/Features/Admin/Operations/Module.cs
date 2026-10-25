using TheYard.Application.Features.Admin.Costs;

namespace TheYard.Api.Features.Admin.Operations;

/// <summary>
/// The Admin tab's operator reads (ADR: What the database is actually doing): the rings, the kept log, the activity, the machines, the proof, the page sweep, the costs, the telemetry and the stores.
/// </summary>
public sealed class Module : IModule
{
    /// <summary>Registers the usage route's handler; what its other endpoints ask for is shared.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration.</param>
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped(provider => new KeepUsageHandler(provider.GetRequiredService<IUsageLog>(), TimeProvider.System));

        // #region operations-wiring
        // The Admin tab's reads as queries (ADR-115's fourth rung): each port is answered by the host part that holds
        // the data, one handler a route. Scoped, because the request's own readings (the store bar, the arrival,
        // the timings of the request's store) are read off the request.
        services.AddScoped<IRingReadings, RingReadings>();
        services.AddScoped<IKeptRingReadings, KeptRingReadings>();
        services.AddScoped<IActivityReadings, ActivityReadings>();
        services.AddScoped<IMachineReadings, MachineCardReadings>();
        services.AddScoped<IAzureReadings, AzureReadings>();
        services.AddScoped<IRequestReadings, RequestReadings>();
        services.AddScoped<IEvidenceReadings, EvidenceReadings>();
        services.AddScoped<IEvidenceRuns, EvidenceRuns>();
        services.AddScoped<ProofReadings>();
        services.AddScoped<IProofReadings>(provider => provider.GetRequiredService<ProofReadings>());
        services.AddScoped<IProofRuns>(provider => provider.GetRequiredService<ProofReadings>());
        services.AddScoped<ICostReadings, CostCardReadings>();
        foreach (var handler in new[]
        {
            typeof(ReadSqlRingHandler), typeof(ReadStoreRingHandler), typeof(ReadLogRingHandler), typeof(ReadMetricsHandler), typeof(ReadKeptRingHandler),
            typeof(ReadActivityHandler), typeof(ReadVisitorRowsHandler), typeof(ReadKeptLogsHandler), typeof(ReadMachinesHandler), typeof(ReadFollowsHandler),
            typeof(ReadAzureSelfHandler), typeof(ReadPeerHandler), typeof(ReadTelemetryHandler), typeof(ReadArrivalHandler), typeof(ReadStoresHandler),
            typeof(ReadPageStatusHandler), typeof(RunPageSweepHandler), typeof(ReadTestSummaryHandler), typeof(ReadTestResultsHandler), typeof(RunExperimentHandler),
            typeof(ReadProofStatusHandler), typeof(StartProofHandler), typeof(ReadCostsHandler),
        })
        {
            services.AddScoped(handler);
        }
        // #endregion operations-wiring
    }

    /// <summary>Maps /api/admin/* that is not the plan or a cloud card, /api/stores and /api/tests/summary.</summary>
    /// <param name="app">The built host's routes.</param>
    public void Map(IEndpointRouteBuilder app)
    {
        app.MapAdminEndpoints();
    }
}
