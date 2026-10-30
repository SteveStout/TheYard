using TheYard.Application.Features.Admin.Plan;

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
            ?? AzureResources.From(configuration).SitePath(AzureIds.SubscriptionId(configuration));
        string clientId = AzureIds.ClientId(configuration);
        services.AddSingleton(MachineSize.OfThisProcess());
        // The layout is a part of its own, with the real clock given to it here: it is the one part
        // of the reading that remembers an answer, and an hour is how long it keeps one.
        services.AddSingleton(new PlanLayout(self, TimeProvider.System));
        services.AddSingleton(provider =>
        {
            var identity = provider.GetRequiredService<HostIdentity>();
            return new PlanReader(
                clientId,
                provider.GetRequiredService<PlanLayout>(),
                configured: identity.OnAppService,
                identity: identity,
                machine: provider.GetRequiredService<MachineSize>());
        });
        services.AddSingleton<IPlanHistory>(provider => provider.GetRequiredService<DocumentStore>().Store is { } cosmos
            ? new CosmosPlanHistory(cosmos, TimeProvider.System)
            : new MemoryPlanHistory("kept in this process's memory, so the readings end with it; the document store is not configured here"));

        // One reading of a shared plan is the whole truth, so one process records it: the one
        // whose default store is SQL, which the one web app is, having no store default set. Were
        // the sites split again, the Cosmos DB site would read the same keep without recording.
        string recordingSite = SiteSettings.From(configuration).HostFor(StoreKeys.Relational) ?? "the SQL site";
        bool records = string.Equals(StoreChoice.DefaultKey(configuration), StoreKeys.Relational, StringComparison.Ordinal);
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
