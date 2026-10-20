// The entry point. It builds the web host and starts it. Each step below is one call, and the
// comment beside the call names the file that holds the details, so this file stays a short
// list of what the app is made of. The rules live in Domain, the use cases in Application, the
// stores in Infrastructure, and this project only wires them together (ADR: Program.cs, explained).
// Each step is handed what it needs by name, and what the platform says about this process is
// read here once, never inside behaviour (ADR: The onion cut into vertical slices, rule 6).
using TheYard.Api;

#region composition
var builder = WebApplication.CreateBuilder(args);
var paths = HostPaths.Find(builder.Environment.ContentRootPath);                    // Composition/HostPaths.cs
var build = new BuildInfo(builder.Configuration["APP_VERSION"] ?? "dev", builder.Configuration["APP_COMMIT"] ?? "local");
var identity = HostIdentity.From(builder.Configuration);                           // HostIdentity.cs
builder.Services.AddSingleton(paths);
builder.Services.AddSingleton(build);
builder.Services.AddSingleton(identity);

var stores = await builder.AddTheYardStoresAsync(paths);                           // Composition/StoreRegistration.cs
builder.AddTheYardObservability(stores.Backends, stores.Document, stores.SqlLog, stores.StoreLog, stores.Accessor, stores.CurrentRequest);
var tokens = builder.AddTheYardSessions();                                         // Composition/AuthRegistration.cs
builder.AddTheYardActivity(stores.Backends, stores.Document, stores.Relational);   // Composition/ActivityRegistration.cs
builder.AddTheYardEmail();                                                         // Composition/EmailRegistration.cs
builder.AddTheYardAccounts(tokens);                                                // Composition/AuthRegistration.cs
builder.AddTheYardApi(build);                                                      // Composition/ApiRegistration.cs
builder.AddTheYardTelemetry(identity);                                             // Composition/TelemetryRegistration.cs
builder.AddTheYardAdmin(stores.Relational, build, identity);                       // Composition/AdminRegistration.cs
builder.AddTheYardCosts(identity);                                                 // Composition/CostRegistration.cs
builder.AddTheYardPlan(stores.Document, stores.Backends.Default.Key, identity);     // Composition/PlanRegistration.cs
builder.AddTheYardCloud(stores.Document, stores.Backends.Default.Key, identity);    // Composition/CloudRegistration.cs
builder.AddTheYardStartup(identity);                                               // Composition/StartupRegistration.cs
builder.AddTheYardShed();                                                          // Composition/ShedRegistration.cs, last

var app = builder.Build();
await app.MountTheShedAsync();                                                     // Composition/ShedMount.cs
app.UseTheYardRequestPipeline(paths, identity);                                    // Composition/RequestPipeline.cs
#endregion composition

app.MapReferenceEndpoints();                  // Endpoints/ReferenceEndpoints.cs
app.MapVehicleEndpoints();                    // Endpoints/VehicleEndpoints.cs
app.MapBidEndpoints();                        // Endpoints/BidEndpoints.cs
app.MapDocumentationEndpoints();              // Endpoints/DocumentationEndpoints.cs
app.MapRecommendationEndpoints();             // Endpoints/RecommendationEndpoints.cs
app.MapHealthEndpoints();                     // Endpoints/HealthEndpoints.cs
app.MapPulseEndpoints();                      // Endpoints/PulseEndpoints.cs
app.MapErrorEndpoints();                      // Endpoints/ErrorEndpoints.cs
app.MapAdminEndpoints();                      // Endpoints/AdminEndpoints.cs
app.MapPlanEndpoints();                       // Endpoints/PlanEndpoints.cs
app.MapCloudEndpoints();                      // Endpoints/CloudEndpoints.cs
app.MapAccountEndpoints();                    // Endpoints/AccountEndpoints.cs
app.MapTheYardSpa();                          // Composition/SpaRegistration.cs

app.Run();

#region records-and-test-hook
/// <summary>
/// The app's entry point, exposed so the integration tests can boot the real host.
/// </summary>
public sealed partial class Program;
#endregion records-and-test-hook
