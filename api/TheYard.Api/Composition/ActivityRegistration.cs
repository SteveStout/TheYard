using TheYard.Application;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Api;

/// <summary>
/// Site activity, the machines card and the catalogue keeper: the collectors that
/// write off the request path, and the operator's key and switches that guard the
/// rows (ADR: Site activity, and the line an address does not cross).
/// </summary>
public static class ActivityRegistration
{
    /// <summary>Registers the activity collector, the machine sampler and recorder, the catalogue keeper and the operator's settings.</summary>
    public static void AddTheYardActivity(this WebApplicationBuilder builder, YardComposition host)
    {
        var backends = host.Backends;
        var cosmos = host.Cosmos;
        var sqlBackend = host.SqlBackend;
        var storeLog = host.StoreLog;
        var requestLog = host.RequestLog;
        string signingKey = host.SigningKey;
        // #region activity-wiring
        // Site activity (ADR: Site activity, and the line an address does not cross).
        // The visitor token is keyed with the signing key, so both containers turn one
        // address into one token within a day and nobody outside can turn it back.
        // The collector is a hosted service that writes off the request path, one
        // batch every five seconds, to one keeper: Azure Cosmos DB wherever it came
        // up, whichever store served the request, because it keeps the rows without
        // expiry and because writing a serverless relational database every five
        // seconds kept it from ever pausing and spent September's free amount on the
        // fourteenth (ADR: Site activity, and the line an address does not cross,
        // addendum). Without Cosmos DB the default store keeps its own rows. The
        // admin key guards the visitor rows; unset, that endpoint is a 404, which is
        // the default and the safe one.
        host.VisitorTokens = new VisitorTokens(signingKey);
        var activityStores = backends.All.ToDictionary(backend => backend.Key, backend => backend.Activity, StringComparer.Ordinal);
        var activityKeeper = backends.All.FirstOrDefault(backend => backend.Key == "cosmos" && backend.Activity is not NullActivityStore)?.Key ?? backends.Default.Key;
        builder.Services.AddSingleton(services => new ActivityCollector(activityStores, activityKeeper, services.GetRequiredService<ILogger<ActivityCollector>>()));
        builder.Services.AddHostedService(services => services.GetRequiredService<ActivityCollector>());

        // #region machine-sampler-wiring
        // Memory is not an event, so nothing on the Admin tab could show it until
        // something asked on a clock (ADR: What the machines are doing). Four an
        // hour for an hour, in about twenty kilobytes of this container's memory.
        builder.Services.AddSingleton(new MachineSampler(RingSizes.MachineSamples));
        builder.Services.AddHostedService(services => services.GetRequiredService<MachineSampler>());
        // #endregion machine-sampler-wiring
        // #region machine-history-wiring
        // And a minute at a time is kept where a roll cannot empty it, so the card
        // can draw a day, a week and a month beside the hour this process remembers
        // (ADR: What the machines are doing, the addendum on the windows). Both sites
        // write to the one document store, each under its own name; with no document
        // store the port is wired to nothing, the recorder idles, and the card says
        // so. The relational reading goes through the quiet context, so a read a
        // minute is not the newest line on the SQL card for ever.
        IMachineHistory machineHistory = cosmos is not null ? new CosmosMachineHistory(cosmos) : NullMachineHistory.Instance;
        builder.Services.AddSingleton(new MachineHistoryReader(machineHistory, backends.Default.Key));
        builder.Services.AddSingleton(services => new MachineRecorder(
            services.GetRequiredService<MachineSampler>(),
            machineHistory,
            backends.Default.Key,
            cancellation => ResourceStats.ReadAsync(sqlBackend.QuietContexts, "the relational store", 8, cancellation),
            () => storeLog.Snapshot(),
            () => requestLog.Snapshot(),
            services.GetRequiredService<ILogger<MachineRecorder>>()));
        builder.Services.AddHostedService(services => services.GetRequiredService<MachineRecorder>());
        // #endregion machine-history-wiring
        // The catalogue of the store this site does not serve is given back once
        // nobody has asked for it in a while. Zero minutes, the default, is never;
        // the plan both sites share sets ten (ADR: One plan, two sites).
        builder.Services.AddSingleton(services => new CatalogueKeeper(
            backends,
            TimeSpan.FromMinutes(builder.Configuration.GetValue("Store:ReleaseIdleMinutes", 0)),
            services.GetRequiredService<ILogger<CatalogueKeeper>>()));
        builder.Services.AddHostedService(services => services.GetRequiredService<CatalogueKeeper>());
        var adminKey = new AdminKey(builder.Configuration["Admin:Key"]);
        builder.Services.AddSingleton(adminKey);
        // Whether the per-visitor rows (the visitor table and the kept log) are
        // served at all. Off by default on the owner's rule ("disable
        // the per visitor data for now"): the rows keep being written, the two
        // endpoints answer 404 to everybody, key or no key, and the cards do not
        // show. Admin__VisitorRows=true turns it back on (ADR: Site activity, and
        // the line an address does not cross, sixth addendum).
        bool visitorRows = builder.Configuration.GetValue("Admin:VisitorRows", false);
        builder.Services.AddSingleton(new AdminSettings(visitorRows));
        // A report is kept thirty seconds and rebuilt behind the next read for ten
        // minutes after that, so a reader never waits on the visitor rows being counted
        // (ActivityReportCache, the addendum of 28 September).
        builder.Services.AddSingleton(new ActivityReportCache(TimeProvider.System));

        // #endregion activity-wiring
    }
}
