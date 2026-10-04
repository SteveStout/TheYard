using TheYard.Application;

namespace TheYard.Api;

/// <summary>
/// What Azure charges for the site (ADR: What Azure charges): the reader that asks
/// Cost Management with the site's own identity, the recorder that holds its answer
/// once an hour, and the reader the Admin tab's cost cards are served from.
/// </summary>
public static class CostRegistration
{
    /// <summary>Registers the cost reader, the hourly recorder, the port it holds the last read in and the cached reader the endpoint answers from.</summary>
    public static void AddTheYardCosts(this WebApplicationBuilder builder)
    {
        // #region cost-wiring
        // The subscription and the identity are identifiers, not secrets: the id is
        // already in the Azure card's resource path, and the identity is the
        // credential (ADR-072). The reader asks only where the platform has named an
        // identity endpoint, which is App Service; a local run and every test read
        // nothing and say so. The last read is held in memory: Azure keeps the bill,
        // so a copy in a database here would be a second truth that could drift.
        var reader = new CostReader(
            YardComposition.AzureSubscriptionId(builder.Configuration),
            YardComposition.AzureClientId(builder.Configuration),
            configured: IdentityTokens.OnAppService);
        ICostHistory history = new CostsInMemory();
        var status = new CostStatus();
        builder.Services.AddSingleton(reader);
        builder.Services.AddSingleton(status);
        builder.Services.AddSingleton(new CostHistoryReader(history, reader, status, TimeProvider.System));
        builder.Services.AddSingleton(services => new CostRecorder(
            reader,
            history,
            status,
            TimeProvider.System,
            services.GetRequiredService<ILogger<CostRecorder>>()));
        builder.Services.AddHostedService(services => services.GetRequiredService<CostRecorder>());
        // #endregion cost-wiring
    }
}
