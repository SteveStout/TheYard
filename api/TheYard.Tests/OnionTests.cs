using Mono.Cecil;
using NetArchTest.Rules;
using TheYard.Application;
using TheYard.Data;
using TheYard.Domain;
using TheYard.Infrastructure;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Tests;

/// <summary>
/// The onion, held by the build. Dependencies point inward: Data at the centre, then Domain,
/// then Application, with the adapters (Infrastructure for the relational store,
/// Infrastructure.Cosmos for the document store) and the host (Api) outside them. Each rule reads
/// the compiled assemblies with NetArchTest, so a reference that only shows up in a method body,
/// a fully qualified name or a generic argument is caught as surely as a using line. An adapter
/// can never use the host because the host references the adapters and the compiler refuses a
/// cycle, so that line needs no test of its own.
/// (ADR: Onion and SOLID, how this codebase holds them)
/// </summary>
public sealed class OnionTests
{
    // #region rings
    /// <summary>The namespaces every ring may use: the .NET base class library and nothing else.</summary>
    private const string BaseClassLibrary = "System";

    /// <summary>
    /// The tracker the coverage collector writes into every assembly it measures. CI runs these
    /// tests with coverage on, so without this the rings would fail on code nobody wrote.
    /// </summary>
    private const string CoverageTracker = "Coverlet.Core.Instrumentation";

    /// <summary>
    /// The parts of the base class library that reach the disk, the network or a database. The
    /// rules above allow the whole library, so these are held out of the inner rings by name.
    /// </summary>
    private static readonly string[] TheDiskAndTheNetwork =
        ["System.IO.File", "System.IO.Directory", "System.IO.FileStream", "System.IO.StreamReader", "System.IO.StreamWriter", "System.Net", "System.Data"];

    [Fact]
    public void Data_depends_on_nothing_outside_the_BCL()
    {
        var result = InRing(typeof(Vehicle), "TheYard.Data")
            .ShouldNot().HaveDependencyOtherThan(BaseClassLibrary, CoverageTracker, "TheYard.Data")
            .GetResult();
        Assert.True(result.IsSuccessful, Explain(result));
    }

    [Fact]
    public void Domain_depends_on_nothing_outside_the_BCL_and_Data()
    {
        var result = InRing(typeof(BidRules), "TheYard.Domain")
            .ShouldNot().HaveDependencyOtherThan(BaseClassLibrary, CoverageTracker, "TheYard.Data", "TheYard.Domain")
            .GetResult();
        Assert.True(result.IsSuccessful, Explain(result));
    }

    [Fact]
    public void Application_depends_only_on_Domain_Data_and_the_BCL()
    {
        var result = InRing(typeof(Auction), "TheYard.Application")
            .ShouldNot().HaveDependencyOtherThan(BaseClassLibrary, CoverageTracker, "TheYard.Data", "TheYard.Domain", "TheYard.Application")
            .GetResult();
        Assert.True(result.IsSuccessful, Explain(result));
    }

    [Fact]
    public void Domain_never_reaches_the_disk_the_network_or_the_clock()
    {
        // A rule is a function of what it is handed: the vehicle, the bids and the clock. Reading
        // the time, a random number or a file itself would make it untestable with a fixed clock.
        var result = InRing(typeof(BidRules), "TheYard.Domain")
            .ShouldNot().HaveDependencyOnAny([.. TheDiskAndTheNetwork, "System.Random", "System.TimeProvider", "System.Environment"])
            .GetResult();
        Assert.True(result.IsSuccessful, Explain(result));
        Assert.Empty(ClockReads(typeof(BidRules).Assembly.Location, "TheYard.Domain"));
    }

    [Fact]
    public void Application_never_reaches_the_disk_or_the_network()
    {
        // A use case reaches a store or a service through a port; the disk and the network belong
        // to the adapters behind those ports.
        var result = InRing(typeof(Auction), "TheYard.Application")
            .ShouldNot().HaveDependencyOnAny(TheDiskAndTheNetwork)
            .GetResult();
        Assert.True(result.IsSuccessful, Explain(result));
    }

    [Fact]
    public void The_document_store_adapter_borrows_only_the_shared_user_from_the_relational_one()
    {
        // Identity's user entity is the one type both stores keep accounts as. Anything else from
        // the relational adapter, and Entity Framework above all, stays on its own side.
        string[] relational = typeof(YardDbContext).Assembly.GetTypes()
            .Where(type => type.IsPublic && type != typeof(YardUser))
            .Select(type => type.FullName!)
            .Append("Microsoft.EntityFrameworkCore")
            .ToArray();
        var result = Types.InAssembly(typeof(CosmosStore).Assembly)
            .ShouldNot().HaveDependencyOnAny(relational)
            .GetResult();
        Assert.True(result.IsSuccessful, Explain(result));
    }
    // #endregion rings

    // #region host
    [Fact]
    public void Endpoints_never_reach_a_store_or_the_service_container()
    {
        // An endpoint reads the request, asks the Application ring, and writes the answer. A
        // database context, a Cosmos DB client or a service looked up by hand would make it a
        // second place where the rules and the wiring live.
        var result = Types.InAssembly(typeof(Program).Assembly)
            .That().MeetCustomRule(IsPartOfAnEndpointClass)
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.Azure.Cosmos",
                "Microsoft.Data.SqlClient",
                "TheYard.Infrastructure.Cosmos",
                "TheYard.Infrastructure.YardDbContext",
                "TheYard.Infrastructure.ContextFactory",
                "System.IServiceProvider",
                "Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions")
            .GetResult();
        Assert.True(result.IsSuccessful, Explain(result));
    }

    [Fact]
    public void Endpoints_reach_the_auction_only_through_the_Application_ring()
    {
        // The order bids are composed in (the buyer's first, the room's second) and the price a
        // bid is measured against are Application's, in Auction. An endpoint that held the
        // services itself could compose them in another order.
        var result = Types.InAssembly(typeof(Program).Assembly)
            .That().MeetCustomRule(IsPartOfAnEndpointClass)
            .ShouldNot().HaveDependencyOnAny(
                "TheYard.Application.BidService",
                "TheYard.Application.MarketService",
                "TheYard.Application.InventoryService")
            .GetResult();
        Assert.True(result.IsSuccessful, Explain(result));
    }

    [Fact]
    public void Endpoints_see_the_auction_and_the_request_s_store_never_a_backend()
    {
        // A backend is the shared wiring's: which adapter stands behind a store is said by its
        // diagnostics, and an endpoint reads the request's store through CurrentBackend's key,
        // name and auction (ADR: The onion cut into vertical slices, rule 4). The Admin tab's and
        // the health report's endpoints read every backend on purpose until their slices have
        // handlers of their own, which is rule 7's rung.
        var result = Types.InAssembly(typeof(Program).Assembly)
            .That().MeetCustomRule(type => IsPartOfAnEndpointClass(type)
                && !InNamespace(Outermost(type).Namespace, "TheYard.Api.Features.Admin")
                && !InNamespace(Outermost(type).Namespace, "TheYard.Api.Features.Health"))
            .ShouldNot().HaveDependencyOnAny("TheYard.Api.Backend", "TheYard.Api.Backends", "TheYard.Infrastructure.Cosmos.CosmosStore")
            .GetResult();
        Assert.True(result.IsSuccessful, Explain(result));
    }

    [Fact]
    public void The_host_uses_the_sheds_assembly_only_from_the_shed_s_own_site()
    {
        // The Shed is a whole application of its own, hosted in this process (ADR: The onion cut
        // into vertical slices, rule 5). ShedSite and ShedApplication may use its types; anything
        // else that did would fuse the two applications the merge kept apart by construction.
        var result = Types.InAssembly(typeof(Program).Assembly)
            .That().MeetCustomRule(type => Outermost(type).Name is not ("ShedSite" or "ShedApplication"))
            .ShouldNot().HaveDependencyOnAny(["TestProject"])
            .GetResult();
        Assert.True(result.IsSuccessful, Explain(result));
    }

    [Fact]
    public void The_host_never_queries_a_database_itself()
    {
        // A statement against a store belongs to the adapter for that store; the host asks the
        // adapter. Entity Framework's design-time factory and the composition root may name the
        // context type, and neither runs a query.
        var result = Types.InAssembly(typeof(Program).Assembly)
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions",
                "Microsoft.EntityFrameworkCore.RelationalQueryableExtensions",
                "Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions",
                "Microsoft.EntityFrameworkCore.DbSet",
                "Microsoft.Data.SqlClient",
                "Microsoft.Azure.Cosmos.QueryDefinition",
                "Microsoft.Azure.Cosmos.Container")
            .GetResult();
        Assert.True(result.IsSuccessful, Explain(result));
    }

    [Fact]
    public void Services_are_registered_only_in_the_composition_root()
    {
        // Registration classes in Shared/, the slices' modules and Program.cs are where the container is filled.
        // Anywhere else, a registration would be wiring hidden inside behaviour.
        var result = Types.InAssembly(typeof(Program).Assembly)
            .That().MeetCustomRule(type => !IsCompositionRoot(type))
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.Extensions.DependencyInjection.IServiceCollection",
                "Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions",
                "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions")
            .GetResult();
        Assert.True(result.IsSuccessful, Explain(result));
    }

    [Fact]
    public void The_host_finds_nothing_at_runtime_outside_its_composition_root()
    {
        // A dependency arrives by constructor (ADR: The onion cut into vertical slices, rule 6).
        // Reading the environment, a service container or the built app's configuration from
        // inside behaviour is wiring hidden where a test cannot hand it a value; the types in
        // CompositionRoot are where those are read, each for the reason beside it.
        Assert.Empty(RuntimeLookups(typeof(Program).Assembly.Location));
    }

    [Fact]
    public void The_application_ring_finds_nothing_at_runtime_at_all()
    {
        // The handlers and services take their ports by constructor; a handler that took an
        // IServiceProvider and asked it for a port would pass the ring rule (System is allowed)
        // and hide its dependency from every fake-port test (the review of 1.0.4.0, finding 5).
        Assert.Empty(RuntimeLookups(typeof(TheYard.Application.Auction).Assembly.Location));
    }
    // #endregion host

    /// <summary>
    /// Every place an assembly's method bodies, outside the composition root, read the environment
    /// (Environment.GetEnvironmentVariable), a service container (IServiceProvider.GetService and
    /// the GetService, GetRequiredService and GetServices extensions over it, ActivatorUtilities,
    /// HttpContext.RequestServices) or the built app's configuration (WebApplication.Configuration).
    /// </summary>
    private static List<string> RuntimeLookups(string assemblyPath)
    {
        using var module = ModuleDefinition.ReadModule(assemblyPath);
        // Coverlet's tracker, which a coverage run injects into the host's assembly and which reads
        // the environment itself, is left out the way the ring rules leave out CoverageTracker: it
        // is the measuring tool's code, not this codebase's (CI on main ran red on it from 1.0.3.188).
        return [.. module.GetTypes()
            .Where(type => !IsCompositionRoot(type) && !(type.Namespace ?? "").StartsWith(CoverageTracker, StringComparison.Ordinal))
            .SelectMany(type => type.Methods.Where(method => method.HasBody))
            .SelectMany(method => method.Body.Instructions
                .Select(instruction => instruction.Operand)
                .OfType<MethodReference>()
                .Where(called => (called.DeclaringType.FullName, called.Name) is
                    ("System.Environment", "GetEnvironmentVariable")
                    or ("System.IServiceProvider", "GetService")
                    or ("Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions", _)
                    or ("Microsoft.Extensions.DependencyInjection.ServiceProviderKeyedServiceExtensions", _)
                    or ("Microsoft.Extensions.DependencyInjection.ActivatorUtilities", _)
                    or ("Microsoft.AspNetCore.Http.HttpContext", "get_RequestServices")
                    or ("Microsoft.AspNetCore.Builder.WebApplication", "get_Configuration"))
                .Select(called => $"{method.DeclaringType.FullName}.{method.Name} calls {called.DeclaringType.Name}.{called.Name}"))];
    }

    /// <summary>
    /// Every type in one ring's assembly and namespace, including the types the compiler writes
    /// inside them for lambdas and async methods, which carry the method bodies.
    /// </summary>
    private static PredicateList InRing(Type marker, string ring) =>
        Types.InAssembly(marker.Assembly).That().MeetCustomRule(type => InNamespace(Outermost(type).Namespace, ring));

    /// <summary>
    /// Every place a ring's method bodies read the system clock (DateTime.Now, DateTime.UtcNow,
    /// DateTime.Today and the DateTimeOffset ones). The type is allowed, because a rule takes the
    /// time as a value; reading it is not, and only a scan of the calls can tell the two apart.
    /// </summary>
    private static List<string> ClockReads(string assemblyPath, string ring)
    {
        using var module = ModuleDefinition.ReadModule(assemblyPath);
        return [.. module.GetTypes()
            .Where(type => InNamespace(Outermost(type).Namespace, ring))
            .SelectMany(type => type.Methods.Where(method => method.HasBody))
            .SelectMany(method => method.Body.Instructions
                .Select(instruction => instruction.Operand)
                .OfType<MethodReference>()
                .Where(called => called.DeclaringType.FullName is "System.DateTime" or "System.DateTimeOffset"
                    && called.Name is "get_Now" or "get_UtcNow" or "get_Today")
                .Select(called => $"{method.DeclaringType.FullName}.{method.Name} reads {called.DeclaringType.Name}.{called.Name[4..]}"))];
    }

    /// <summary>True for the ring's namespace and every namespace under it.</summary>
    private static bool InNamespace(string name, string ring) =>
        name == ring || name.StartsWith(ring + ".", StringComparison.Ordinal);

    /// <summary>True for an endpoint class and for the types the compiler writes inside one (its async state machines and lambdas).</summary>
    private static bool IsPartOfAnEndpointClass(TypeDefinition type) => Outermost(type).Name.EndsWith("Endpoints", StringComparison.Ordinal);

    /// <summary>
    /// The types where the container is filled and read, by name, each with its reason: Program and
    /// the registration classes in Shared/ fill it; a slice's Module fills its part; the two pipeline
    /// steps read the built container once, when Program builds the pipeline; The Shed's application
    /// composes a guest site's own container; IModule is the shape every slice's Module has. A type is on this list or it is behaviour, so a new
    /// FooModule or BarRegistration elsewhere is not exempt by its name (the review of 1.0.4.0, finding 5).
    /// </summary>
    private static readonly HashSet<string> CompositionRoot =
    [
        "Program", "<Program>$",
        "ActivityRegistration", "AdminRegistration", "ApiRegistration", "AuthRegistration", "CostRegistration", "HostedSiteRegistration",
        "ObservabilityRegistration", "SpaRegistration", "StartupRegistration", "StoreRegistration", "TelemetryRegistration",
        "Modules", "IModule",
        "RequestPipeline", "HostedSiteMount", "ShedApplication",
    ];

    /// <summary>True for a type on the composition root's list, for a slice's Module (TheYard.Api.Features.*.Module), and for the types the compiler writes inside them.</summary>
    private static bool IsCompositionRoot(TypeDefinition type)
    {
        var outer = Outermost(type);
        return CompositionRoot.Contains(outer.Name)
            || (outer.Name == "Module" && (outer.Namespace ?? "").StartsWith("TheYard.Api.Features.", StringComparison.Ordinal));
    }

    /// <summary>The type a nested or compiler-written type sits inside, all the way out.</summary>
    private static TypeDefinition Outermost(TypeDefinition type)
    {
        while (type.DeclaringType is { } outer)
        {
            type = outer;
        }
        return type;
    }

    /// <summary>The failing types and why, so a red test names the line to fix rather than only saying no.</summary>
    private static string Explain(TestResult result) =>
        result.IsSuccessful
            ? ""
            : string.Join(Environment.NewLine, (result.FailingTypes ?? []).Select(type => $"{type.FullName}: {type.Explanation}"));
}
