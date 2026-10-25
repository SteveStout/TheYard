namespace TheYard.Tests;

// #region modular-monolith-tests
/// <summary>
/// The site as a modular monolith (ADR-119): one deployable, cut into modules that are the vertical slices of
/// ADR-107, a slice naming no other slice in any ring, and the job hunt's Function a second process that shares
/// rings and linked source files with the site, never a project. The bodies are tests/rules/ModularMonolithRule.cs,
/// the same ones functions/jobhunt.Tests runs over the Function.
/// </summary>
public sealed class ModularMonolithTests
{
    /// <summary>The rings of the site, by folder and root namespace.</summary>
    private static readonly string[] Rings = ["TheYard.Domain", "TheYard.Application", "TheYard.Infrastructure", "TheYard.Infrastructure.Cosmos", "TheYard.Api"];

    [Fact]
    public void The_site_is_one_deployable()
    {
        // TheYard.Api is the one process the site runs as; TheYard.Experiment is the partition key experiment's
        // console tool, run by hand and never deployed, so it is named rather than counted.
        var deployables = TheYard.Rules.ModularMonolithRule.Deployables(Path.Combine(Repo.Root(), "api"), Repo.Root());
        Assert.Equal(["api/TheYard.Api/TheYard.Api.csproj", "api/TheYard.Experiment/TheYard.Experiment.csproj"], deployables);
    }

    [Fact]
    public void A_slice_s_files_name_no_other_slice_in_any_ring()
    {
        // The reflection rule (SliceTests, rule 1 across the rings) reads what a slice compiles to; this reads what
        // its files say, so a using or an alias that names another slice is red before anything uses it.
        var wrong = Rings
            .SelectMany(ring => TheYard.Rules.ModularMonolithRule.CrossSliceUsings(Path.Combine(Repo.Root(), "api", ring), Rings, Repo.Root()))
            .ToList();
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void Every_module_of_the_host_registers_through_one_Module()
    {
        // A module is reached through its Module.cs and the Application ring's ports: each top folder under the
        // host's Features is a module, holding one Module.cs or one in each of its sub-slices.
        string features = Path.Combine(Repo.Root(), "api", "TheYard.Api", "Features");
        static bool Registered(string folder) =>
            File.Exists(Path.Combine(folder, "Module.cs"))
            || (Directory.EnumerateDirectories(folder).Any() && Directory.EnumerateDirectories(folder).All(sub => File.Exists(Path.Combine(sub, "Module.cs"))));
        var wrong = Directory.EnumerateDirectories(features)
            .Where(folder => !Registered(folder))
            .Select(folder => Path.GetRelativePath(Repo.Root(), folder).Replace('\\', '/') + " has no Module.cs for every slice it holds")
            .ToList();
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void The_two_processes_share_rings_and_linked_files_never_a_project()
    {
        string root = Repo.Root();
        Assert.Empty(TheYard.Rules.ModularMonolithRule.ProjectReferencesAcross(Path.Combine(root, "api"), Path.Combine(root, "functions"), root));
        Assert.Empty(TheYard.Rules.ModularMonolithRule.ProjectReferencesAcross(Path.Combine(root, "functions"), Path.Combine(root, "api"), root));
        // What crosses is named: the nightly slots the Function's clock asks (ADR-109) and the Central clock the site's
        // tests read, each one file compiled into the other process by a link.
        Assert.Equal(["functions/JobHunt.Domain/Shared/Clock/CentralClock.cs"], TheYard.Rules.ModularMonolithRule.LinkedFrom(Path.Combine(root, "api"), Path.Combine(root, "functions"), root));
        Assert.Equal(["api/TheYard.Domain/Features/Admin/NightlySlots.cs"], TheYard.Rules.ModularMonolithRule.LinkedFrom(Path.Combine(root, "functions"), Path.Combine(root, "api"), root));
    }
}
// #endregion modular-monolith-tests
