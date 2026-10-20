using TheYard.Application;
using TheYard.Application.Features.Admin.Plan;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Api.Features.Admin.Plan;

/// <summary>
/// The App Service plan card (ADR: The App Service plan, read every minute): the reader that asks
/// Azure Monitor with the site's own identity, the keep in the document store, the recorder that
/// runs in the one process, the cached reader the endpoint answers from, and the endpoint.
/// </summary>
public sealed class Module : IModule
{
    /// <summary>Registers the plan's reader, keep, recorder and cached reader.</summary>
    /// <param name="services">The host's services.</param>
    /// <param name="configuration">The host's configuration: the site's own path, and whether this process records.</param>
    public void Register(IServiceCollection services, IConfiguration configuration)
    {
        // #region plan-wiring
        // The site's own path is read the way the Azure card reads it; the plan and its apps are
        // read off it, never configured. The keep is the document store when there is one, and
        // this process's memory otherwise, which the card says.
        string self = configuration["Azure:SelfResourceId"]
            ?? $"/subscriptions/{AzureIds.SubscriptionId(configuration)}/resourceGroups/RG-THEYARD-SS/providers/Microsoft.Web/sites/APP-THEYARD-ONE-SS-ZMNETJ67BN5H2";
        string clientId = AzureIds.ClientId(configuration);
        services.AddSingleton(MachineSize.OfThisProcess());
        services.AddSingleton(provider =>
        {
            var identity = provider.GetRequiredService<HostIdentity>();
            return new PlanReader(clientId, self, configured: identity.OnAppService, identity: identity, machine: provider.GetRequiredService<MachineSize>());
        });
        services.AddSingleton<IPlanHistory>(provider => provider.GetRequiredService<DocumentStore>().Store is { } cosmos
            ? new CosmosPlanHistory(cosmos)
            : new MemoryPlanHistory("kept in this process's memory, so the readings end with it; the document store is not configured here"));

        // One reading of a shared plan is the whole truth, so one process records it: the one
        // whose default store is SQL, which the one web app is, having no store default set. Were
        // the sites split again, the Cosmos DB site would read the same keep without recording.
        string recordingSite = SiteSettings.From(configuration).HostFor("sql") ?? "the SQL site";
        bool records = string.Equals(StoreChoice.DefaultKey(configuration), "sql", StringComparison.Ordinal);
        if (records)
        {
            services.AddSingleton(provider => new PlanRecorder(
                provider.GetRequiredService<PlanReader>(),
                provider.GetRequiredService<IPlanHistory>(),
                provider.GetRequiredService<BuildInfo>(),
                TimeProvider.System,
                provider.GetRequiredService<ILogger<PlanRecorder>>()));
            services.AddHostedService(provider => provider.GetRequiredService<PlanRecorder>());
        }

        services.AddSingleton(provider => new PlanHistoryReader(
            provider.GetRequiredService<IPlanHistory>(),
            records ? provider.GetRequiredService<PlanRecorder>() : null,
            cancellation => PlanCost.MonthToDateAsync(provider.GetRequiredService<ICostHistory>(), provider.GetRequiredService<PlanReader>(), TimeProvider.System, cancellation),
            TimeProvider.System,
            recordingSite));
        services.AddSingleton<IPlanCard>(provider => provider.GetRequiredService<PlanHistoryReader>());
        services.AddScoped<ReadPlanHandler>();
        // #endregion plan-wiring
    }

    /// <summary>Maps GET /api/admin/plan.</summary>
    /// <param name="app">The built host's routes.</param>
    public void Map(IEndpointRouteBuilder app) => app.MapPlanEndpoints();
}
