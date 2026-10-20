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
    /// <param name="builder">The host being built.</param>
    /// <param name="cosmos">The document store the readings are kept in, or null.</param>
    /// <param name="defaultStore">The key of this process's default store; the one whose default is SQL records.</param>
    /// <param name="identity">Where this process runs: its web app's name, and the door Azure is asked through.</param>
    public static void AddTheYardCloud(this WebApplicationBuilder builder, CosmosStore? cosmos, string defaultStore, HostIdentity identity)
    {
        // #region resource-wiring
        var azure = new AzureMetrics(AzureIds.ClientId(builder.Configuration), configured: identity.OnAppService, identity: identity);
        var directory = new ResourceDirectory(azure, $"/subscriptions/{AzureIds.SubscriptionId(builder.Configuration)}/resourceGroups/RG-THEYARD-SS", TimeProvider.System);
        var edge = new NetlifyEdge(builder.Configuration["Netlify:Token"], "theyard-edge", new Uri(builder.Configuration["Netlify:PublicSite"] ?? "https://theyard.stevenstout.biz/"));
        // Which public name gets the SQL database and which web app answers it, from this process's own settings.
        string? sqlName = SiteHosts.From(builder.Configuration).Sql;
        string? siteName = identity.SiteName;
        string? sqlSite = sqlName is null ? null : siteName is { Length: > 0 } ? $"{sqlName}, on {siteName}" : sqlName;
        var cards = ResourceCatalog.Cards(azure, directory, edge, YardModelFacts.Read(), sqlSite);
        IResourceHistory KeepOf(ResourceCard card) => cosmos is not null
            ? new CosmosResourceHistory(cosmos, card.Container)
            : new MemoryResourceHistory("kept in this process's memory, so the readings end with it; the document store is not configured here");
        var kept = cards.Select(card => (card, KeepOf(card))).ToList();

        // One reading of a shared resource is the whole truth, so one process records it, as with
        // the plan: the one whose default store is SQL, which the one web app is.
        bool records = string.Equals(defaultStore, "sql", StringComparison.Ordinal) && identity.OnAppService;
        if (records)
        {
            builder.Services.AddSingleton(services => new CloudRecorder(kept, services.GetRequiredService<BuildInfo>(), TimeProvider.System, services.GetRequiredService<ILogger<CloudRecorder>>()));
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
