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
    [
        "System.IO.File", "System.IO.Directory", "System.IO.FileStream", "System.IO.StreamReader", "System.IO.StreamWriter",
        "System.IO.FileInfo", "System.IO.DirectoryInfo", "System.IO.FileSystemInfo", "System.IO.Compression", "System.IO.MemoryMappedFiles", "System.IO.Pipes",
        "System.Net", "System.Data",
    ];

    /// <summary>What a rule that takes everything it needs as a value never reads for itself: a clock, a process, a type found by its name.</summary>
    private static readonly string[] TheMachine = ["System.Random", "System.TimeProvider", "System.Environment", "System.Diagnostics.Stopwatch", "System.Diagnostics.Process"];

    [Fact]
    public void Data_depends_on_nothing_outside_the_BCL()
    {
        var result = InRing(typeof(Vehicle), "TheYard.Data")
            .ShouldNot().HaveDependencyOtherThan(BaseClassLibrary, CoverageTracker, "TheYard.Data")
            .GetResult();
        Assert.True(result.IsSuccessful, Explain(result));
        // System is allowed whole, so the disk, the network and the machine are held out by name.
        var reaches = InRing(typeof(Vehicle), "TheYard.Data").ShouldNot().HaveDependencyOnAny([.. TheDiskAndTheNetwork, .. TheMachine]).GetResult();
        Assert.True(reaches.IsSuccessful, Explain(reaches));
        Assert.Empty(TypeLookups(typeof(Vehicle).Assembly.Location, "TheYard.Data"));
    }

    [Fact]
    public void Data_has_no_behaviour()
    {
        // Pure records (CLAUDE.md): a property, a constructor, what the compiler writes for a record,
        // and nothing else. A computed rule in Data is a second Domain nobody tests as one.
        Assert.Empty(Behaviour(typeof(Vehicle).Assembly.Location));
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
            .ShouldNot().HaveDependencyOnAny([.. TheDiskAndTheNetwork, .. TheMachine])
            .GetResult();
        Assert.True(result.IsSuccessful, Explain(result));
        Assert.Empty(ClockReads(typeof(BidRules).Assembly.Location, "TheYard.Domain"));
        Assert.Empty(TypeLookups(typeof(BidRules).Assembly.Location, "TheYard.Domain"));
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
            .Append("Microsoft.AspNetCore.Identity.EntityFrameworkCore")
            .Append("Microsoft.Data")
            .ToArray();
        var result = Types.InAssembly(typeof(CosmosStore).Assembly)
            .ShouldNot().HaveDependencyOnAny(relational)
            .GetResult();
        Assert.True(result.IsSuccessful, Explain(result));
    }
    [Fact]
    public void Infrastructure_adapts_and_decides_nothing()
    {
        // An adapter turns a port's call into a store's and back. The web (beyond Identity's user
        // store) is the host's, and a rule about money is the Domain's: an adapter method that takes a
        // price or a bid by name and compares it, or reads a vehicle's price and compares it, is a
        // second copy of BidRules that no Domain test sees.
        var found = new List<string>();
        foreach (var adapter in new[] { typeof(YardDbContext).Assembly, typeof(CosmosStore).Assembly })
        {
            using var module = ModuleDefinition.ReadModule(adapter.Location);
            found.AddRange(module.GetTypeReferences()
                .Where(reference => reference.Namespace.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) && !reference.Namespace.StartsWith("Microsoft.AspNetCore.Identity", StringComparison.Ordinal))
                .Select(reference => $"{adapter.GetName().Name} uses {reference.FullName}"));
            found.AddRange(MoneyDecisions(module));
        }
        Assert.True(found.Count == 0, string.Join(Environment.NewLine, found.Distinct()));
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
                "Microsoft.Data.Sqlite",
                "System.Data.Common",
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
            .ShouldNot().HaveDependencyOnAny("TheYard.Api.Shared.Stores.Backend", "TheYard.Api.Shared.Stores.Backends", "TheYard.Infrastructure.Cosmos.CosmosStore")
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
                "Microsoft.Data.Sqlite",
                "System.Data.Common.DbCommand",
                "System.Data.Common.DbConnection",
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
    public void A_route_or_a_handler_is_handed_its_settings_never_the_configuration()
    {
        // Rule 6 by its other door: IConfiguration injected into a route delegate is the built app's
        // configuration read per request. A setting is bound once, at composition, into a type the
        // route takes (SiteHosts carries Site:Url), so a test hands the route its value.
        using var module = ModuleDefinition.ReadModule(typeof(Program).Assembly.Location);
        var taking = module.GetTypes()
            .Where(type => !IsCompositionRoot(type) && (IsPartOfAnEndpointClass(type) || Outermost(type).Name.EndsWith("Handler", StringComparison.Ordinal)))
            .SelectMany(type => type.Methods)
            .Where(method => method.Parameters.Any(parameter => parameter.ParameterType.FullName == "Microsoft.Extensions.Configuration.IConfiguration"))
            .Select(method => $"{method.DeclaringType.FullName}.{method.Name} takes IConfiguration")
            .ToList();
        Assert.True(taking.Count == 0, string.Join(Environment.NewLine, taking));
    }

    [Fact]
    public void The_application_ring_finds_nothing_at_runtime_at_all()
    {
        // The handlers and services take their ports by constructor; a handler that took an
        // IServiceProvider and asked it for a port would pass the ring rule (System is allowed)
        // and hide its dependency from every fake-port test (the review of 1.0.4.0, finding 5).
        Assert.Empty(RuntimeLookups(typeof(TheYard.Application.Auction).Assembly.Location));
    }

    /// <summary>
    /// Every call this site makes to Claude goes through IClaudeClient, whose one adapter writes a
    /// usage document from every answer (ADR: Every Claude lane reports its usage). So the Anthropic
    /// API's address and an Anthropic SDK namespace appear in that adapter's file and in no other
    /// source file of the six projects or the page; a feature that called the API itself would be a
    /// lane that reports no usage, which the record calls a defect.
    /// </summary>
    [Fact]
    public void Claude_is_asked_only_through_the_one_client_that_reports_its_usage()
    {
        string root = Repo.Root();
        string adapter = Path.Combine("api", "TheYard.Api", "Shared", "Usage", "AnthropicClaudeClient.cs");
        var sources = new[] { "TheYard.Data", "TheYard.Domain", "TheYard.Application", "TheYard.Infrastructure", "TheYard.Infrastructure.Cosmos", "TheYard.Api" }
            .SelectMany(project => Directory.EnumerateFiles(Path.Combine(root, "api", project), "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.EnumerateFiles(Path.Combine(root, "src"), "*.ts*", SearchOption.AllDirectories))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();
        Assert.Contains(sources, path => path.EndsWith(adapter, StringComparison.Ordinal));
        var calling = sources
            .Where(path => !path.EndsWith(adapter, StringComparison.Ordinal))
            .Where(path => File.ReadAllText(path) is var text
                && (text.Contains("api.anthropic.com", StringComparison.OrdinalIgnoreCase) || text.Contains("using Anthropic", StringComparison.Ordinal) || text.Contains("@anthropic-ai/", StringComparison.Ordinal)))
            .Select(path => Path.GetRelativePath(root, path))
            .ToList();
        Assert.True(calling.Count == 0, "these files reach the Anthropic API around IClaudeClient: " + string.Join(", ", calling));
        Assert.Contains(typeof(TheYard.Application.IUsageLog), typeof(TheYard.Api.Shared.Usage.AnthropicClaudeClient).GetConstructors().Single().GetParameters().Select(p => p.ParameterType));
        Assert.True(typeof(TheYard.Application.IClaudeClient).IsAssignableFrom(typeof(TheYard.Api.Shared.Usage.AnthropicClaudeClient)));
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

    /// <summary>
    /// Every place a ring finds a type or loads an assembly by its name (Type.GetType, Assembly.Load
    /// and its kin): a reference the compiler never sees, so no ring rule can.
    /// </summary>
    private static List<string> TypeLookups(string assemblyPath, string ring)
    {
        using var module = ModuleDefinition.ReadModule(assemblyPath);
        return [.. module.GetTypes()
            .Where(type => InNamespace(Outermost(type).Namespace, ring))
            .SelectMany(type => type.Methods.Where(method => method.HasBody))
            .SelectMany(method => method.Body.Instructions
                .Select(instruction => instruction.Operand)
                .OfType<MethodReference>()
                .Where(called => (called.DeclaringType.FullName == "System.Type" && called.Name == "GetType" && called.Parameters.Count > 0)
                    || (called.DeclaringType.FullName == "System.Reflection.Assembly" && called.Name.StartsWith("Load", StringComparison.Ordinal))
                    || called.DeclaringType.FullName == "System.Activator")
                .Select(called => $"{method.DeclaringType.FullName}.{method.Name} calls {called.DeclaringType.Name}.{called.Name}"))];
    }

    /// <summary>What the compiler writes for a record, and the only methods besides accessors and constructors a Data type may carry.</summary>
    private static readonly HashSet<string> RecordMembers = ["ToString", "PrintMembers", "Equals", "GetHashCode", "<Clone>$", "Deconstruct", "op_Equality", "op_Inequality", "get_EqualityContract"];

    /// <summary>Every Data method with a body that is not an accessor, a constructor or a member the compiler writes for a record.</summary>
    private static List<string> Behaviour(string assemblyPath)
    {
        using var module = ModuleDefinition.ReadModule(assemblyPath);
        return [.. module.GetTypes()
            .Where(type => InNamespace(Outermost(type).Namespace, "TheYard.Data") && !type.CustomAttributes.Any(IsCompilerGenerated))
            .SelectMany(type => type.Methods.Where(method => method.HasBody))
            .Where(method => !method.IsGetter && !method.IsSetter && !method.IsConstructor && !RecordMembers.Contains(method.Name) && !method.CustomAttributes.Any(IsCompilerGenerated))
            .Select(method => $"{method.DeclaringType.FullName}.{method.Name} is behaviour in Data")];
    }

    private static bool IsCompilerGenerated(CustomAttribute attribute) =>
        attribute.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute";

    /// <summary>A parameter named for money: a price, a bid, an amount, a reserve or an increment.</summary>
    private static readonly System.Text.RegularExpressions.Regex Money = new("^(amount|bid|buyNow|price|reserve|increment|minNextBid|currentBid|startingBid)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>The comparisons in IL: what turns a value into a decision.</summary>
    private static readonly HashSet<Mono.Cecil.Cil.Code> Comparisons =
    [
        Mono.Cecil.Cil.Code.Clt, Mono.Cecil.Cil.Code.Clt_Un, Mono.Cecil.Cil.Code.Cgt, Mono.Cecil.Cil.Code.Cgt_Un,
        Mono.Cecil.Cil.Code.Blt, Mono.Cecil.Cil.Code.Blt_S, Mono.Cecil.Cil.Code.Blt_Un, Mono.Cecil.Cil.Code.Blt_Un_S,
        Mono.Cecil.Cil.Code.Bgt, Mono.Cecil.Cil.Code.Bgt_S, Mono.Cecil.Cil.Code.Bgt_Un, Mono.Cecil.Cil.Code.Bgt_Un_S,
        Mono.Cecil.Cil.Code.Ble, Mono.Cecil.Cil.Code.Ble_S, Mono.Cecil.Cil.Code.Ble_Un, Mono.Cecil.Cil.Code.Ble_Un_S,
        Mono.Cecil.Cil.Code.Bge, Mono.Cecil.Cil.Code.Bge_S, Mono.Cecil.Cil.Code.Bge_Un, Mono.Cecil.Cil.Code.Bge_Un_S,
    ];

    /// <summary>Adapter methods that compare money: a money parameter by name, or a vehicle's price read, beside a comparison.</summary>
    private static IEnumerable<string> MoneyDecisions(ModuleDefinition module) =>
        module.GetTypes()
            .SelectMany(type => type.Methods.Where(method => method.HasBody))
            .Where(method => method.Body.Instructions.Any(instruction => Comparisons.Contains(instruction.OpCode.Code)))
            .Where(method => method.Parameters.Any(parameter => Money.IsMatch(parameter.Name))
                || method.Body.Instructions.Select(instruction => instruction.Operand).OfType<MethodReference>()
                    .Any(called => called.DeclaringType.FullName == "TheYard.Data.Vehicle" && called.Name is "get_CurrentBid" or "get_BuyNowPrice" or "get_ReservePrice" or "get_StartingBid"))
            .Select(method => $"{method.DeclaringType.FullName}.{method.Name} decides on money in an adapter");

    /// <summary>True for the ring's namespace and every namespace under it.</summary>
    private static bool InNamespace(string name, string ring) =>
        name == ring || name.StartsWith(ring + ".", StringComparison.Ordinal);

    /// <summary>
    /// True for an endpoint class and for the types the compiler writes inside one (its async state
    /// machines and lambdas): a type named *Endpoints, or any type outside the composition root whose
    /// code maps a route (calls a Map method of the route builder), whatever it is named.
    /// </summary>
    private static bool IsPartOfAnEndpointClass(TypeDefinition type)
    {
        var outer = Outermost(type);
        return !IsCompositionRoot(outer) && (outer.Name.EndsWith("Endpoints", StringComparison.Ordinal) || RouteMappers.Value.Contains(outer.FullName));
    }

    /// <summary>The host's types whose method bodies, or the bodies the compiler wrote inside them, map a route.</summary>
    private static readonly Lazy<HashSet<string>> RouteMappers = new(() =>
    {
        using var module = ModuleDefinition.ReadModule(typeof(Program).Assembly.Location);
        return module.GetTypes()
            .Where(type => type.Methods.Any(method => method.HasBody && method.Body.Instructions
                .Select(instruction => instruction.Operand).OfType<MethodReference>()
                .Any(called => called.DeclaringType.Name.EndsWith("EndpointRouteBuilderExtensions", StringComparison.Ordinal) && called.Name.StartsWith("Map", StringComparison.Ordinal))))
            .Select(type => Outermost(type).FullName)
            .ToHashSet(StringComparer.Ordinal);
    });

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
        "ObservabilityRegistration", "SpaRegistration", "StartupRegistration", "StoreRegistration", "TelemetryRegistration", "UsageRegistration",
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
