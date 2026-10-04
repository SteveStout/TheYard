using Mono.Cecil;
using NetArchTest.Rules;
using TestProject.Domain;

namespace TestProject.Tests;

/// <summary>
/// Checks the onion by reading the compiled app. The folders are rings, innermost first: Data,
/// Domain, Application, then Infrastructure and Controllers on the outside, and Composition, which
/// wires them together. A ring may use the rings inside it and never one outside it. NetArchTest
/// reads every type, including the ones the compiler writes for lambdas and async methods, so a
/// reference in a method body or a fully written name is caught as surely as a using line.
/// (more in docs/ADR-013-onion-and-solid.md)
/// </summary>
public sealed class OnionTests
{
    /// <summary>The tracker a coverage run writes into the app, which is nobody's dependency.</summary>
    private const string Coverage = "Coverlet.Core.Instrumentation";

    private static readonly string[] DataMayUse = ["System", Coverage, "TestProject.Data"];
    private static readonly string[] DomainMayUse = ["System", Coverage, "TestProject.Data", "TestProject.Domain"];
    private static readonly string[] ApplicationMayUse = ["System", Coverage, "TestProject.Data", "TestProject.Domain", "TestProject.Application"];
    private static readonly string[] InfrastructureMustNotUse = ["TestProject.Controllers", "TestProject.Composition", "TestProject.Documentation", "Microsoft.AspNetCore"];
    private static readonly string[] ControllersMustNotUse = ["TestProject.Infrastructure", "TestProject.Composition", "System.IO.File", "System.IO.Directory", "System.IO.FileInfo", "System.IO.DirectoryInfo", "System.IO.FileStream"];
    private static readonly string[] DocumentationMustNotUse = ["TestProject.Controllers", "TestProject.Composition", "TestProject.Infrastructure", "Microsoft.AspNetCore"];
    private static readonly string[] Registration = ["Microsoft.Extensions.DependencyInjection.IServiceCollection", "Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions"];

    // #region rings
    [Fact]
    public void Data_depends_on_nothing_outside_the_BCL() =>
        AssertHolds(InRing("TestProject.Data").ShouldNot().HaveDependencyOtherThan(DataMayUse).GetResult());

    [Fact]
    public void Domain_depends_on_nothing_outside_the_BCL_and_Data() =>
        AssertHolds(InRing("TestProject.Domain").ShouldNot().HaveDependencyOtherThan(DomainMayUse).GetResult());

    [Fact]
    public void Application_depends_only_on_Domain_Data_and_the_BCL() =>
        AssertHolds(InRing("TestProject.Application").ShouldNot().HaveDependencyOtherThan(ApplicationMayUse).GetResult());

    [Fact]
    public void Infrastructure_depends_on_nothing_outside_it() =>
        AssertHolds(InRing("TestProject.Infrastructure").ShouldNot().HaveDependencyOnAny(InfrastructureMustNotUse).GetResult());

    [Fact]
    public void Controllers_never_touch_the_disk_or_the_file_store() =>
        AssertHolds(InRing("TestProject.Controllers").ShouldNot().HaveDependencyOnAny(ControllersMustNotUse).GetResult());

    [Fact]
    public void Documentation_reads_its_own_files_and_reaches_nothing_outside_it() =>
        AssertHolds(InRing("TestProject.Documentation").ShouldNot().HaveDependencyOnAny(DocumentationMustNotUse).GetResult());

    [Fact]
    public void Services_are_registered_only_in_Composition() =>
        AssertHolds(Types.InAssembly(typeof(HomePath).Assembly)
            .That().MeetCustomRule(type => Outermost(type).Namespace != "TestProject.Composition")
            .ShouldNot().HaveDependencyOnAny(Registration)
            .GetResult());
    // #endregion rings

    /// <summary>
    /// Every type in one ring, including the ones the compiler writes inside them. A ring is a
    /// folder, and each folder is a namespace.
    /// </summary>
    private static PredicateList InRing(string ring) =>
        Types.InAssembly(typeof(HomePath).Assembly).That().MeetCustomRule(type => Outermost(type).Namespace == ring);

    /// <summary>The type a nested or compiler-written type sits inside, all the way out.</summary>
    private static TypeDefinition Outermost(TypeDefinition type)
    {
        while (type.DeclaringType is { } outer)
        {
            type = outer;
        }
        return type;
    }

    /// <summary>Passes when the rule held, and names each failing type and why when it did not.</summary>
    private static void AssertHolds(TestResult result) =>
        Assert.True(
            result.IsSuccessful,
            string.Join(Environment.NewLine, (result.FailingTypes ?? []).Select(type => $"{type.FullName}: {type.Explanation}")));
}
