using System.Reflection;
using NetArchTest.Rules;
using TheYard.Api;

namespace TheYard.Tests;

// #region slice-tests
/// <summary>
/// The second axis of the onion (ADR: The onion cut into vertical slices): the rules that hold a
/// feature to one folder per ring and its dependencies to what it is handed. The ring rules are in
/// OnionTests; these are the rules the record added beside them, one rung of its ladder at a time.
/// </summary>
public sealed class SliceTests
{
    /// <summary>The host's assembly, read by name so a type that no longer exists is a null and not a compile error.</summary>
    private static readonly System.Reflection.Assembly Host = typeof(Program).Assembly;

    [Fact]
    public void Rule_1_a_slice_depends_only_on_its_own_folder_and_the_shared_ring()
    {
        // A slice is the namespace of its folder under Features/. It may use anything that is not a
        // slice (the shared part of the host, and the inner rings) and its own types; never another
        // slice's. The shared part of the host uses no slice at all: Program.cs finds the modules
        // by reflection, which is no dependency.
        var slices = Host.GetTypes()
            .Select(type => SliceOf(type.Namespace))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(slices.Count >= 8, $"only {slices.Count} slices were found: {string.Join(", ", slices)}");

        var wrong = new List<string>();
        foreach (string slice in slices)
        {
            string[] others = [.. slices.Where(other => other != slice && !other.StartsWith(slice + ".", StringComparison.Ordinal) && !slice.StartsWith(other + ".", StringComparison.Ordinal))];
            var result = Types.InAssembly(Host)
                .That().ResideInNamespace(slice)
                .ShouldNot().HaveDependencyOnAny(others)
                .GetResult();
            wrong.AddRange((result.FailingTypes ?? []).Select(type => $"{type.FullName} reaches another slice"));
        }
        var shared = Types.InAssembly(Host)
            .That().DoNotResideInNamespace(FeaturesNamespace)
            .And().DoNotHaveName("Program")
            .ShouldNot().HaveDependencyOnAny([FeaturesNamespace])
            .GetResult();
        wrong.AddRange((shared.FailingTypes ?? []).Select(type => $"{type.FullName} is shared and reaches a slice"));
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void Rule_1_in_the_inner_rings_too_a_slice_uses_no_other_slice()
    {
        // The same folder name in every ring: Application's and Infrastructure's slices are the
        // namespaces under their Features, and none reaches another's.
        foreach (var ring in new[] { typeof(TheYard.Application.Auction).Assembly, typeof(TheYard.Infrastructure.YardDbContext).Assembly })
        {
            string root = ring.GetName().Name + ".Features";
            string[] slices = [.. ring.GetTypes().Select(type => type.Namespace).OfType<string>()
                .Where(ns => ns.StartsWith(root + ".", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal)];
            foreach (string slice in slices)
            {
                string[] others = [.. slices.Where(other => other != slice)];
                if (others.Length == 0)
                {
                    continue;
                }
                var result = Types.InAssembly(ring).That().ResideInNamespace(slice).ShouldNot().HaveDependencyOnAny(others).GetResult();
                Assert.True(result.IsSuccessful, string.Join(", ", (result.FailingTypes ?? []).Select(type => $"{type.FullName} reaches another slice")));
            }
        }
    }

    /// <summary>The namespace every slice sits under.</summary>
    private const string FeaturesNamespace = "TheYard.Api.Features";

    /// <summary>The slice a namespace belongs to: the folder under Features/ that holds a Module, or null outside Features.</summary>
    private static string? SliceOf(string? ns) =>
        ns is not null && ns.StartsWith(FeaturesNamespace + ".", StringComparison.Ordinal)
            && Host.GetType(ns + ".Module") is not null
            ? ns
            : null;

    [Fact]
    public void Rule_3_the_sites_and_the_stores_are_lookups_with_no_store_or_host_written_in()
    {
        // A third site or a third store is a setting (Shared/SiteSettings.cs), so the two types
        // that decide which store and which site a request gets carry no store key and no name.
        string[] types = ["TheYard.Api.SiteHosts", "TheYard.Api.Site", "TheYard.Api.Backends", "TheYard.Api.CurrentBackend"];
        Assert.Empty(Literals(types, literal =>
            literal.Equals("sql", StringComparison.OrdinalIgnoreCase)
            || literal.Equals("cosmos", StringComparison.OrdinalIgnoreCase)
            || literal.Contains("stevenstout", StringComparison.OrdinalIgnoreCase)
            || literal.Contains("theyard", StringComparison.OrdinalIgnoreCase)
            || literal.Contains("theshed", StringComparison.OrdinalIgnoreCase)
            || literal.Contains(".example", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Rule_4_a_backend_names_no_adapter_and_both_stores_answer_one_diagnostics_port()
    {
        var adapterTypes = new[] { "TheYard.Infrastructure", "TheYard.Infrastructure.Cosmos", "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore.Identity" };
        // Every type a member names, however deep: a Func<IServiceProvider, IUserStore<YardUser>?> names
        // Infrastructure's user entity two generic levels down, and 1.0.4.0's one-level walk let it through
        // (the review of 1.0.4.0, finding 5).
        static IEnumerable<Type> Named(Type type) =>
            type.IsGenericType ? type.GetGenericArguments().SelectMany(Named).Prepend(type) : [type];
        bool Adapter(Type type) => Named(type).Any(named => adapterTypes.Any(ns => named.Namespace?.StartsWith(ns, StringComparison.Ordinal) == true));
        var members = new List<string>();
        foreach (var holder in new[] { typeof(Backend), typeof(StoreAttachment) })
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            members.AddRange(holder.GetProperties(all).Where(property => Adapter(property.PropertyType)).Select(property => $"{holder.Name}.{property.Name} : {property.PropertyType.Name}"));
            members.AddRange(holder.GetFields(all).Where(field => !field.Name.Contains('<', StringComparison.Ordinal) && Adapter(field.FieldType)).Select(field => $"{holder.Name}.{field.Name} : {field.FieldType.Name}"));
            members.AddRange(holder.GetConstructors(all).SelectMany(ctor => ctor.GetParameters()).Where(parameter => Adapter(parameter.ParameterType)).Select(parameter => $"{holder.Name}({parameter.Name} : {parameter.ParameterType.Name})"));
            members.AddRange(holder.GetMethods(all).Where(method => !method.IsSpecialName).SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType).Where(Adapter).Select(type => $"{holder.Name}.{method.Name} : {type.Name}")));
        }
        Assert.True(members.Count == 0, "Backend names an adapter: " + string.Join(", ", members));
        Assert.True(typeof(TheYard.Application.IStoreDiagnostics).IsAssignableFrom(typeof(TheYard.Infrastructure.RelationalDiagnostics)));
        Assert.True(typeof(TheYard.Application.IStoreDiagnostics).IsAssignableFrom(typeof(TheYard.Infrastructure.Cosmos.DocumentDiagnostics)));
    }

    [Fact]
    public void Rule_5_a_guest_site_stands_behind_one_interface_and_the_shed_is_one_implementation()
    {
        var sites = Host.GetTypes().Where(type => type.IsClass && typeof(IHostedSite).IsAssignableFrom(type)).ToList();
        Assert.Contains(sites, type => type.Name == "ShedSite");
        Assert.All(sites, type => Assert.True(type.IsSealed, $"{type.Name} should be sealed"));
        // Nothing else holds The Shed's application: the mount knows only the interface.
        Assert.Null(Host.GetType("TheYard.Api.ShedSlot"));
        Assert.Null(Host.GetType("TheYard.Api.ShedMount"));
    }

    /// <summary>Every string literal the named types' method bodies load that the test flags, with where it was found.</summary>
    private static List<string> Literals(string[] typeNames, Func<string, bool> flagged)
    {
        using var module = Mono.Cecil.ModuleDefinition.ReadModule(Host.Location);
        return [.. module.GetTypes()
            .Where(type => typeNames.Contains(Outermost(type).FullName))
            .SelectMany(type => type.Methods.Where(method => method.HasBody))
            .SelectMany(method => method.Body.Instructions
                .Where(instruction => instruction.OpCode == Mono.Cecil.Cil.OpCodes.Ldstr && instruction.Operand is string literal && flagged(literal))
                .Select(instruction => $"{method.DeclaringType.FullName}.{method.Name} writes \"{instruction.Operand}\""))];
    }

    /// <summary>The type a nested or compiler-written type sits inside, all the way out.</summary>
    private static Mono.Cecil.TypeDefinition Outermost(Mono.Cecil.TypeDefinition type)
    {
        while (type.DeclaringType is { } outer)
        {
            type = outer;
        }
        return type;
    }

    [Fact]
    public void Rule_2_the_composition_bag_is_retired_and_startup_is_one_hosted_service_per_concern()
    {
        // The record every registration took whole, and the one method that read eleven fields off
        // it, are gone: each registration is handed what it needs by name, and what ran at startup
        // is a hosted service per concern, each taking its dependencies by constructor.
        Assert.Null(Host.GetType("TheYard.Api.YardComposition"));
        Assert.Null(Host.GetType("TheYard.Api.Startup"));
        string[] concerns = ["StoreAnnouncement", "ScratchDatabaseCleanup", "ErrorRingKeeping", "CatalogueWarm", "PageSweepAtRoll", "KeepWarmAtRoll", "HostedSiteStopper"];
        foreach (string concern in concerns)
        {
            var type = Host.GetType("TheYard.Api." + concern);
            Assert.True(type is not null && typeof(Microsoft.Extensions.Hosting.IHostedService).IsAssignableFrom(type), $"{concern} is not a hosted service");
        }
    }
}
// #endregion slice-tests
