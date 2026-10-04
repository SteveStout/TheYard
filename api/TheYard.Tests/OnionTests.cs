using Mono.Cecil;
using NetArchTest.Rules;
using TheYard.Api;
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
public class OnionTests
{
    // #region rings
    /// <summary>The namespaces every ring may use: the .NET base class library and nothing else.</summary>
    private const string BaseClassLibrary = "System";

    /// <summary>
    /// The tracker the coverage collector writes into every assembly it measures. CI runs these
    /// tests with coverage on, so without this the rings would fail on code nobody wrote.
    /// </summary>
    private const string CoverageTracker = "Coverlet.Core.Instrumentation";

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
        var result = Types.InAssembly(typeof(BidEndpoints).Assembly)
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
        var result = Types.InAssembly(typeof(BidEndpoints).Assembly)
            .That().MeetCustomRule(IsPartOfAnEndpointClass)
            .ShouldNot().HaveDependencyOnAny(
                "TheYard.Application.BidService",
                "TheYard.Application.MarketService",
                "TheYard.Application.InventoryService")
            .GetResult();
        Assert.True(result.IsSuccessful, Explain(result));
    }

    [Fact]
    public void The_host_never_queries_a_database_itself()
    {
        // A statement against a store belongs to the adapter for that store; the host asks the
        // adapter. Entity Framework's design-time factory and the composition root may name the
        // context type, and neither runs a query.
        var result = Types.InAssembly(typeof(BidEndpoints).Assembly)
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
        // Registration classes in Composition/ and Program.cs are where the container is filled.
        // Anywhere else, a registration would be wiring hidden inside behaviour.
        var result = Types.InAssembly(typeof(BidEndpoints).Assembly)
            .That().MeetCustomRule(type => !IsCompositionRoot(type))
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.Extensions.DependencyInjection.IServiceCollection",
                "Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions",
                "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions")
            .GetResult();
        Assert.True(result.IsSuccessful, Explain(result));
    }
    // #endregion host

    /// <summary>
    /// Every type in one ring's assembly and namespace, including the types the compiler writes
    /// inside them for lambdas and async methods, which carry the method bodies.
    /// </summary>
    private static PredicateList InRing(Type marker, string ring) =>
        Types.InAssembly(marker.Assembly).That().MeetCustomRule(type => InNamespace(Outermost(type).Namespace, ring));

    /// <summary>True for the ring's namespace and every namespace under it.</summary>
    private static bool InNamespace(string name, string ring) =>
        name == ring || name.StartsWith(ring + ".", StringComparison.Ordinal);

    /// <summary>True for an endpoint class and for the types the compiler writes inside one (its async state machines and lambdas).</summary>
    private static bool IsPartOfAnEndpointClass(TypeDefinition type) => Outermost(type).Name.EndsWith("Endpoints", StringComparison.Ordinal);

    /// <summary>True for Program and for a registration class in Composition/, and for the types the compiler writes inside them.</summary>
    private static bool IsCompositionRoot(TypeDefinition type)
    {
        string name = Outermost(type).Name;
        return name is "Program" or "<Program>$" || name.EndsWith("Registration", StringComparison.Ordinal);
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
