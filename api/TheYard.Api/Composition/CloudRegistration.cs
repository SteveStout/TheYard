using TheYard.Application;
using TheYard.Infrastructure;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Api;

/// <summary>
/// The six resource cards under Cloud infrastructure (ADR: One frame for every resource): one
/// reader of Azure Monitor and one of Netlify, the resource group's list each card finds its
/// resource in, a keep per card, the one recorder and the one cached reader the
/// endpoint answers from. Each card's own file says what it reads and how it draws.
/// </summary>
public static class CloudRegistration
{
    /// <summary>Registers the cards, their keeps, the recorder and the reader.</summary>
    public static void AddTheYardCloud(this WebApplicationBuilder builder, YardComposition host)
    {
        // #region resource-wiring
        var azure = new AzureMetrics(YardComposition.AzureClientId(builder.Configuration), configured: IdentityTokens.OnAppService);
        var directory = new ResourceDirectory(azure, $"/subscriptions/{YardComposition.AzureSubscriptionId(builder.Configuration)}/resourceGroups/RG-THEYARD-SS", TimeProvider.System);
        var edge = new NetlifyEdge(builder.Configuration["Netlify:Token"], "theyard-edge", new Uri(builder.Configuration["Netlify:PublicSite"] ?? "https://theyard.stevenstout.biz/"));
        var cards = ResourceCatalog.Cards(azure, directory, edge, YardModelFacts.Read());
        IResourceHistory KeepOf(ResourceCard card) => host.Cosmos is not null
            ? new CosmosResourceHistory(host.Cosmos, card.Container)
            : new MemoryResourceHistory("kept in this process's memory, so the readings end with it; the document store is not configured here");
        var kept = cards.Select(card => (card, KeepOf(card))).ToList();

        // One reading of a shared resource is the whole truth, so one process records it, as with
        // the plan: the one whose default store is SQL, which the one web app is.
        bool records = string.Equals(host.Backends.Default.Key, "sql", StringComparison.Ordinal) && IdentityTokens.OnAppService;
        if (records)
        {
            builder.Services.AddSingleton(services => new CloudRecorder(kept, host.Build, TimeProvider.System, services.GetRequiredService<ILogger<CloudRecorder>>()));
            builder.Services.AddHostedService(services => services.GetRequiredService<CloudRecorder>());
        }
        builder.Services.AddSingleton(services => new CloudReader(
            kept,
            records ? services.GetRequiredService<CloudRecorder>() : null,
            (name, cancellation) => PlanCost.ForResourceAsync(services.GetRequiredService<ICostHistory>(), name, TimeProvider.System, cancellation),
            TimeProvider.System));
        // #endregion resource-wiring
    }
}
