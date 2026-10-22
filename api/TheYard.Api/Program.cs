// The entry point. It builds the web host and starts it. The rules live in Domain, the use cases in
// Application, the stores in Infrastructure, and this project only wires them together (ADR:
// Program.cs, explained). What every slice shares is registered here by name, one call each with
// the file beside it; every slice is a folder under Features/ with a Module that registers and maps
// it, found by the loop below (ADR: The onion cut into vertical slices). What the platform says about
// this process is read here once, never inside behaviour.
using TheYard.Api;

#region composition
var builder = WebApplication.CreateBuilder(args);
var paths = HostPaths.Find(builder.Environment.ContentRootPath);                    // Shared/HostPaths.cs
var build = new BuildInfo(builder.Configuration["APP_VERSION"] ?? "dev", builder.Configuration["APP_COMMIT"] ?? "local");
var identity = HostIdentity.From(builder.Configuration);                           // HostIdentity.cs
builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(build);
builder.Services.AddSingleton(identity);

var stores = await builder.AddTheYardStoresAsync(paths);                           // Shared/StoreRegistration.cs
builder.AddTheYardObservability(stores.Backends, stores.Document, stores.SqlLog, stores.StoreLog, stores.Accessor, stores.CurrentRequest);
builder.AddTheYardSessions();                                                      // Shared/AuthRegistration.cs
builder.AddTheYardActivity(stores.Backends, stores.Document, stores.Relational);   // Shared/ActivityRegistration.cs
builder.AddTheYardApi(build);                                                      // Shared/ApiRegistration.cs
builder.AddTheYardTelemetry(identity);                                             // Shared/TelemetryRegistration.cs
builder.AddTheYardAdmin(stores.Relational, build, identity);                       // Shared/AdminRegistration.cs
builder.AddTheYardCosts(identity);                                                 // Shared/CostRegistration.cs
builder.AddTheYardUsage();                                                         // Shared/UsageRegistration.cs

var modules = Modules.InThisHost();                                                // Shared/Modules.cs, every Features/*/Module.cs
foreach (var module in modules)
{
    module.Register(builder.Services, builder.Configuration);
}
builder.AddTheYardStartup(identity);                                               // Shared/StartupRegistration.cs
builder.AddTheYardHostedSites();                                                   // Shared/HostedSiteRegistration.cs, last

var app = builder.Build();
await app.MountTheHostedSitesAsync();                                              // Shared/Sites/HostedSiteMount.cs
app.UseTheYardRequestPipeline(paths, identity);                                    // Shared/RequestPipeline.cs
#endregion composition

foreach (var module in modules)
{
    module.Map(app);
}
app.MapTheYardSpa();                                                               // Shared/SpaRegistration.cs, the last word

app.Run();

#region records-and-test-hook
/// <summary>
/// The app's entry point, exposed so the integration tests can boot the real host.
/// </summary>
public sealed partial class Program;
#endregion records-and-test-hook
