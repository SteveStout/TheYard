namespace TheYard.Api.Shared.Wiring;

/// <summary>
/// What Azure charges for the site (ADR: What Azure charges): the reader that asks
/// Cost Management with the site's own identity, the recorder that holds its answer
/// once an hour, and the reader the Admin tab's cost cards are served from.
/// </summary>
public static class CostRegistration
{
    /// <summary>Registers the cost reader, the hourly recorder, the port it holds the last read in and the cached reader the endpoint answers from.</summary>
    /// <param name="builder">The host being built.</param>
    /// <param name="identity">Where this process runs: the reader asks only on App Service, through its door.</param>
    /// <param name="relational">The relational store's context factories, which the last read is kept through once the store is attached.</param>
    public static void AddTheYardCosts(this WebApplicationBuilder builder, HostIdentity identity, RelationalContexts relational)
    {
        // #region cost-wiring
        // The subscription and the identity are identifiers, not secrets: the id is
        // already in the Azure card's resource path, and the identity is the
        // credential (ADR-072). The reader asks only where the platform has named an
        // identity endpoint, which is App Service; a local run and every test read
        // nothing and say so. The last read is kept in the relational store as well as
        // held in memory, so a roll starts with the last bill and does not ask again
        // while it is under an hour old (ADR: What Azure charges, the addendum on
        // keeping the read).
        var reader = new CostReader(
            AzureIds.SubscriptionId(builder.Configuration),
            AzureIds.ClientId(builder.Configuration),
            configured: identity.OnAppService,
            identity: identity);
        ICostHistory history = KeptHistory(relational, TimeProvider.System);
        // The kept days are also what the plan card reads its own line of the bill from, so the
        // two cards cannot disagree about what the plan costs.
        builder.Services.AddSingleton(history);
        var status = new CostStatus();
        builder.Services.AddSingleton(reader);
        builder.Services.AddSingleton(status);
        // The rules on the meters are read with the same identity, beside the bill, and held the same way,
        // from the two groups configuration names: the site's own and the job hunt's.
        var rules = new AlertRules(
            AzureIds.SubscriptionId(builder.Configuration),
            AzureIds.ClientId(builder.Configuration),
            configured: identity.OnAppService,
            groups: [.. new[] { builder.Configuration["Azure:Resources:Group"], builder.Configuration["Nightly:Group"] }.OfType<string>().Where(group => group.Length > 0)],
            identity: identity);
        builder.Services.AddSingleton(rules);
        builder.Services.AddSingleton(new CostHistoryReader(history, reader, status, TimeProvider.System, rules));
        // One read and what it leaves behind, then the schedule that runs it: the recorder holds
        // only the waits, so it is handed the keeping rather than everything the keeping needs.
        builder.Services.AddSingleton(services => new CostKeeping(
            reader,
            history,
            status,
            TimeProvider.System,
            services.GetRequiredService<ILogger<CostKeeping>>()));
        builder.Services.AddSingleton(services => new CostRecorder(
            services.GetRequiredService<CostKeeping>(),
            CostRecorder.FirstReadAfter(builder.Configuration["Store:Default"]),
            rules,
            TimeProvider.System));
        builder.Services.AddHostedService(services => services.GetRequiredService<CostRecorder>());
        // #endregion cost-wiring
    }

    /// <summary>The history the cost cards and the recorder share: held here, and kept through the relational store's quiet contexts once it is attached.</summary>
    /// <param name="relational">The relational store's context factories.</param>
    /// <param name="clock">The clock the held copy's age is read on.</param>
    public static ICostHistory KeptHistory(RelationalContexts relational, TimeProvider clock) => new KeptCosts(() => relational.Quiet, clock);
}
