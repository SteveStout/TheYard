// The composition root. It lists what the app is made of, in the order it is
// made; each call leads to the file that shows how (ADR: Program.cs, explained).
// Inventory and bidding, composed onion-style: Domain (entities, photo
// selection, auction schedule, filter and bid rules) <- Application
// (InventoryService and BidService use cases) <- Infrastructure (the stores'
// adapters, the synthetic scale-up) <- this host. The React app consumes it
// through Vite's /api proxy, so no CORS is needed.
using TheYard.Api;

#region composition
var builder = WebApplication.CreateBuilder(args);
var host = new YardComposition
{
    Paths = HostPaths.Find(builder.Environment.ContentRootPath),
    Build = new BuildInfo(
        Environment.GetEnvironmentVariable("APP_VERSION") ?? "dev",
        Environment.GetEnvironmentVariable("APP_COMMIT") ?? "local"),
};
builder.Services.AddSingleton(host.Paths);
builder.Services.AddSingleton(host.Build);

await builder.AddTheYardStoresAsync(host);    // Composition/StoreRegistration.cs
builder.AddTheYardObservability(host);        // Composition/ObservabilityRegistration.cs
builder.AddTheYardSessions(host);             // Composition/AuthRegistration.cs
builder.AddTheYardActivity(host);             // Composition/ActivityRegistration.cs
builder.AddTheYardEmail();                    // Composition/EmailRegistration.cs
builder.AddTheYardAccounts(host);             // Composition/AuthRegistration.cs
builder.AddTheYardApi(host);                  // Composition/ApiRegistration.cs
builder.AddTheYardTelemetry();                // Composition/TelemetryRegistration.cs
builder.AddTheYardAdmin(host);                // Composition/AdminRegistration.cs
builder.AddTheYardCosts();                    // Composition/CostRegistration.cs

var app = builder.Build();
await app.StartTheYardAsync(host);            // Composition/Startup.cs
app.UseTheYardRequestPipeline(host);          // Composition/RequestPipeline.cs
#endregion composition

app.MapReferenceEndpoints();                  // Endpoints/ReferenceEndpoints.cs
app.MapVehicleEndpoints();                    // Endpoints/VehicleEndpoints.cs
app.MapBidEndpoints();                        // Endpoints/BidEndpoints.cs
app.MapDocumentationEndpoints();              // Endpoints/DocumentationEndpoints.cs
app.MapHealthEndpoints();                     // Endpoints/HealthEndpoints.cs
app.MapErrorEndpoints();                      // Endpoints/ErrorEndpoints.cs
app.MapAdminEndpoints();                      // Endpoints/AdminEndpoints.cs
app.MapAccountEndpoints();                    // Endpoints/AccountEndpoints.cs
app.MapTheYardSpa();                          // Composition/SpaRegistration.cs

app.Run();

#region records-and-test-hook
/// <summary>
/// The app's entry point, exposed so the integration tests can boot the real host.
/// </summary>
public sealed partial class Program;
#endregion records-and-test-hook
