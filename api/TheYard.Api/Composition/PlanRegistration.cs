using TheYard.Application;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Api;

/// <summary>
/// The App Service plan card (ADR: The App Service plan, read every minute): the reader that asks
/// Azure Monitor with the site's own identity, the keep in the document store, the recorder that
/// runs in the one process, and the cached reader the endpoint answers from.
/// </summary>
public static class PlanRegistration
{
    /// <summary>Registers the plan's reader, keep, recorder and cached reader.</summary>
    /// <param name="builder">The host being built.</param>
    /// <param name="cosmos">The document store the readings are kept in, or null.</param>
    /// <param name="defaultStore">The key of this process's default store; the one whose default is SQL records.</param>
    /// <param name="identity">Where this process runs: the reader asks only on App Service, through its door.</param>
    public static void AddTheYardPlan(this WebApplicationBuilder builder, CosmosStore? cosmos, string defaultStore, HostIdentity identity)
    {
        // #region plan-wiring
        // The site's own path is read the way the Azure card reads it; the plan and its apps are
        // read off it, never configured. The keep is the document store when there is one, and
        // this process's memory otherwise, which the card says.
        string self = builder.Configuration["Azure:SelfResourceId"]
            ?? $"/subscriptions/{AzureIds.SubscriptionId(builder.Configuration)}/resourceGroups/RG-THEYARD-SS/providers/Microsoft.Web/sites/APP-THEYARD-ONE-SS-ZMNETJ67BN5H2";
        var reader = new PlanReader(AzureIds.ClientId(builder.Configuration), self, configured: identity.OnAppService, identity: identity);
        IPlanHistory history = cosmos is not null
            ? new CosmosPlanHistory(cosmos)
            : new MemoryPlanHistory("kept in this process's memory, so the readings end with it; the document store is not configured here");
        builder.Services.AddSingleton(history);

        // One reading of a shared plan is the whole truth, so one process records it: the one
        // whose default store is SQL, which the one web app is, having no store default set. Were
        // the sites split again, the Cosmos DB site would read the same keep without recording.
        bool records = string.Equals(defaultStore, "sql", StringComparison.Ordinal);
        if (records)
        {
            builder.Services.AddSingleton(services => new PlanRecorder(reader, history, services.GetRequiredService<BuildInfo>(), TimeProvider.System, services.GetRequiredService<ILogger<PlanRecorder>>()));
            builder.Services.AddHostedService(services => services.GetRequiredService<PlanRecorder>());
        }

        builder.Services.AddSingleton(services => new PlanHistoryReader(
            history,
            records ? services.GetRequiredService<PlanRecorder>() : null,
            cancellation => PlanCost.MonthToDateAsync(services.GetRequiredService<ICostHistory>(), reader, TimeProvider.System, cancellation),
            TimeProvider.System));
        // #endregion plan-wiring
    }
}
