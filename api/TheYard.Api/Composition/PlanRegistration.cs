using TheYard.Application;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Api;

/// <summary>
/// The App Service plan card (ADR: The App Service plan, read every minute): the reader that asks
/// Azure Monitor with the site's own identity, the keep in the document store, the recorder that
/// runs on the SQL site alone, and the cached reader the endpoint answers from.
/// </summary>
public static class PlanRegistration
{
    /// <summary>Registers the plan's reader, keep, recorder and cached reader.</summary>
    public static void AddTheYardPlan(this WebApplicationBuilder builder, YardComposition host)
    {
        // #region plan-wiring
        // The site's own path is read the way the Azure card reads it; the plan and its apps are
        // read off it, never configured. The keep is the document store when there is one, and
        // this process's memory otherwise, which the card says.
        string self = builder.Configuration["Azure:SelfResourceId"]
            ?? $"/subscriptions/{YardComposition.AzureSubscriptionId(builder.Configuration)}/resourceGroups/RG-THEYARD-SS/providers/Microsoft.Web/sites/APP-THEYARD-ONE-SS-ZMNETJ67BN5H2";
        var reader = new PlanReader(YardComposition.AzureClientId(builder.Configuration), self, configured: IdentityTokens.OnAppService);
        IPlanHistory history = host.Cosmos is not null
            ? new CosmosPlanHistory(host.Cosmos)
            : new MemoryPlanHistory("kept in this process's memory, so the readings end with it; the document store is not configured here");
        builder.Services.AddSingleton(history);

        // One reading of a shared plan is the whole truth, so only the site whose default store
        // is SQL records it; the Cosmos DB site reads the same keep.
        bool records = string.Equals(host.Backends.Default.Key, "sql", StringComparison.Ordinal);
        if (records)
        {
            builder.Services.AddSingleton(services => new PlanRecorder(reader, history, host.Build, TimeProvider.System, services.GetRequiredService<ILogger<PlanRecorder>>()));
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
