using NetArchTest.Rules;

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

    /// <summary>The namespace every slice sits under.</summary>
    private const string FeaturesNamespace = "TheYard.Api.Features";

    /// <summary>The slice a namespace belongs to: the folder under Features/ that holds a Module, or null outside Features.</summary>
    private static string? SliceOf(string? ns) =>
        ns is not null && ns.StartsWith(FeaturesNamespace + ".", StringComparison.Ordinal)
            && Host.GetType(ns + ".Module") is not null
            ? ns
            : null;

    [Fact]
    public void Rule_2_the_composition_bag_is_retired_and_startup_is_one_hosted_service_per_concern()
    {
        // The record every registration took whole, and the one method that read eleven fields off
        // it, are gone: each registration is handed what it needs by name, and what ran at startup
        // is a hosted service per concern, each taking its dependencies by constructor.
        Assert.Null(Host.GetType("TheYard.Api.YardComposition"));
        Assert.Null(Host.GetType("TheYard.Api.Startup"));
        string[] concerns = ["StoreAnnouncement", "ScratchDatabaseCleanup", "ErrorRingKeeping", "CatalogueWarm", "PageSweepAtRoll", "KeepWarmAtRoll", "ShedStopper"];
        foreach (string concern in concerns)
        {
            var type = Host.GetType("TheYard.Api." + concern);
            Assert.True(type is not null && typeof(Microsoft.Extensions.Hosting.IHostedService).IsAssignableFrom(type), $"{concern} is not a hosted service");
        }
    }
}
// #endregion slice-tests
