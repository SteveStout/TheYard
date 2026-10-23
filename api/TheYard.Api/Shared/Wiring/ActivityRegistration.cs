using TheYard.Application;
using TheYard.Application.Features.Visitors;
using TheYard.Infrastructure;
using TheYard.Infrastructure.Cosmos;
using TheYard.Infrastructure.Cosmos.Features.Visitors;

namespace TheYard.Api.Shared.Wiring;

/// <summary>
/// Site activity and the machines card: the collectors that
/// write off the request path, and the operator's key and switches that guard the
/// rows (ADR: Site activity, and the line an address does not cross).
/// </summary>
public static class ActivityRegistration
{
    /// <summary>Registers the activity collector, the machine sampler and recorder and the operator's settings.</summary>
    /// <param name="builder">The host being built.</param>
    /// <param name="backends">Every store: each keeps its own activity rows unless the document store keeps them all.</param>
    /// <param name="cosmos">The document store the machine minutes are kept in, or null.</param>
    /// <param name="relational">The relational store's contexts, whose quiet one the machine recorder reads.</param>
    public static void AddTheYardActivity(this WebApplicationBuilder builder, Backends backends, CosmosStore? cosmos, RelationalContexts relational)
    {
        // #region activity-wiring
        // Site activity (ADR: Site activity, and the line an address does not cross).
        // The visitor token is keyed with the signing key (AuthRegistration), so one
        // address is one token within a day and nobody outside can turn it back.
        // The collector is a hosted service that writes off the request path, one
        // batch every five seconds, to one keeper: Azure Cosmos DB wherever it came
        // up, whichever store served the request, because it keeps the rows without
        // expiry and because writing a serverless relational database every five
        // seconds keeps it from pausing and spends its free monthly allowance (ADR:
        // Site activity, and the line an address does not cross, addendum). Without Cosmos DB the default store keeps its own rows. The
        // admin key guards the visitor rows; unset, that endpoint is a 404, which is
        // the default and the safe one.
        var activityStores = backends.All.ToDictionary(backend => backend.Key, backend => backend.Activity, StringComparer.Ordinal);
        var activityKeeper = backends.All.FirstOrDefault(backend => backend.Key == StoreKeys.Document && backend.Activity is not NullActivityStore)?.Key ?? backends.Default.Key;
        // The people who have visited, all time (ADR-116): kept in the document store's activity container
        // under a kind of their own, or in this process's memory where there is no document store, which the
        // count says. The collector hands them on with the activity, one write a visitor a day at most.
        IVisitorStore visitors = cosmos is not null ? new CosmosVisitorStore(cosmos, TimeProvider.System) : new MemoryVisitorStore();
        builder.Services.AddSingleton(visitors);
        builder.Services.AddSingleton<IVisitorReader>(visitors);
        builder.Services.AddSingleton(services => new ActivityCollector(activityStores, activityKeeper, services.GetRequiredService<ILogger<ActivityCollector>>(), visitors));
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
        // The first screen's one reading, over the same kept minutes and the same hourly bill the
        // Admin tab reads, kept for fifteen seconds (ADR: One reading for the first screen).
        builder.Services.AddSingleton(services => new PulseReader(
            machineHistory,
            backends.Default.Key,
            services.GetRequiredService<CostHistoryReader>(),
            services.GetRequiredService<HostStart>(),
            services.GetRequiredService<BuildInfo>(),
            TimeProvider.System,
            cancellation => new ReadPeopleCountHandler(visitors, TimeProvider.System).HandleAsync(new ReadPeopleCountQuery(), cancellation)));
        builder.Services.AddSingleton(services => new MachineRecorder(
            services.GetRequiredService<MachineSampler>(),
            machineHistory,
            backends.Default.Key,
            cancellation => ResourceStats.ReadAsync(relational.Quiet, "the relational store", 8, cancellation),
            services.GetRequiredService<StoreRingBuffer>().Snapshot,
            services.GetRequiredService<RequestRingBuffer>().Snapshot,
            services.GetRequiredService<ILogger<MachineRecorder>>()));
        builder.Services.AddHostedService(services => services.GetRequiredService<MachineRecorder>());
        // #endregion machine-history-wiring
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
        // (ActivityReportCache; ADR: Site activity, and the line an address does not
        // cross, the addendum on the first read).
        builder.Services.AddSingleton(new ActivityReportCache(TimeProvider.System));

        // #endregion activity-wiring
    }
}
