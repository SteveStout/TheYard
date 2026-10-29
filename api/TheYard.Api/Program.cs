// The entry point. It builds the web host and starts it. The rules live in Domain, the use cases in
// Application, the stores in Infrastructure, and this project only wires them together (ADR:
// Program.cs, explained). What every slice shares is registered here by name, one call each with
// the file beside it; every slice is a folder under Features/ with a Module that registers and maps
// it, found by the loop below (ADR: The onion cut into vertical slices). What the platform says about
// this process is read here once, never inside behaviour.

#region composition
var builder = WebApplication.CreateBuilder(args);
var paths = HostPaths.Find(builder.Environment.ContentRootPath);                    // Shared/Wiring/HostPaths.cs
var build = new BuildInfo(builder.Configuration["APP_VERSION"] ?? "dev", builder.Configuration["APP_COMMIT"] ?? "local");
var identity = HostIdentity.From(builder.Configuration);                           // Shared/Instance/HostIdentity.cs
builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(build);
builder.Services.AddSingleton(identity);

// None, said here rather than deep inside the registration: the lifetime that owns
// ApplicationStopping is made by Build() below, so nothing stops the stores being asked, and this
// is the one file where that is a true statement instead of a token somebody forgot to thread.
var stores = await builder.AddTheYardStoresAsync(paths, CancellationToken.None);   // Shared/Wiring/StoreRegistration.cs
builder.AddTheYardObservability(stores.Backends, stores.Document, stores.SqlLog, stores.StoreLog, stores.Accessor, stores.CurrentRequest);
builder.AddTheYardSessions();                                                      // Shared/Wiring/AuthRegistration.cs
builder.AddTheYardActivity(stores.Backends, stores.Document, stores.Relational);   // Shared/Wiring/ActivityRegistration.cs
builder.AddTheYardApi(build);                                                      // Shared/Wiring/ApiRegistration.cs
builder.AddTheYardTelemetry(identity);                                             // Shared/Wiring/TelemetryRegistration.cs
builder.AddTheYardAdmin(stores.Relational, build, identity);                       // Shared/Wiring/AdminRegistration.cs
builder.AddTheYardCosts(identity);                                                 // Shared/Wiring/CostRegistration.cs
builder.AddTheYardUsage();                                                         // Shared/Wiring/UsageRegistration.cs

var modules = Modules.InThisHost();                                                // Shared/Wiring/Modules.cs, every Features/*/Module.cs
foreach (var module in modules)
{
    module.Register(builder.Services, builder.Configuration);
}
builder.AddTheYardStartup(identity);                                               // Shared/Wiring/StartupRegistration.cs
builder.AddTheYardHostedSites();                                                   // Shared/Wiring/HostedSiteRegistration.cs, last

var app = builder.Build();
await app.MountTheHostedSitesAsync(app.Lifetime.ApplicationStopping);  // Shared/Sites/HostedSiteMount.cs
app.UseTheYardRequestPipeline(paths, identity);                                    // Shared/Wiring/RequestPipeline.cs
#endregion composition

foreach (var module in modules)
{
    module.Map(app);
}
app.MapTheYardSpa();                                                               // Shared/Wiring/SpaRegistration.cs, the last word

app.Run();

#region records-and-test-hook
/// <summary>
/// The app's entry point, exposed so the integration tests can boot the real host.
/// </summary>
public sealed partial class Program;
#endregion records-and-test-hook
