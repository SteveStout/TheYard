using TheYard.Rules;

namespace JobHunt.Server.Tests;

// #region server-modular-monolith-tests
/// <summary>
/// The machine lanes as the third process of the modular monolith (ADR-119, the addendum on the third process): one
/// deployable, cut into modules that are its slices, sharing rings and five named linked files with the Function and
/// no project with anything. The bodies are tests/rules/ModularMonolithRule.cs, the ones the other two suites run.
/// </summary>
public sealed class ModularMonolithTests
{
    private static readonly string[] Rings = ["JobHunt.Server.Domain", "JobHunt.Server.Application", "JobHunt.Server.Infrastructure"];

    [Fact]
    public void The_rule_names_three_processes()
    {
        Assert.Equal(["api", "functions", "server"], ModularMonolithRule.Processes);
    }

    [Fact]
    public void The_machine_lanes_are_one_deployable()
    {
        Assert.Equal(["server/JobHunt.Server/JobHunt.Server.csproj"], ModularMonolithRule.Deployables(Path.Combine(Repo.Root(), "server"), Repo.Root()));
    }

    [Fact]
    public void A_slice_s_files_name_no_other_slice_in_any_ring()
    {
        var wrong = Rings.SelectMany(ring => ModularMonolithRule.CrossSliceUsings(Path.Combine(Repo.Root(), "server", ring), Rings, Repo.Root())).ToList();
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void No_process_references_a_project_of_another()
    {
        Assert.Empty(ModularMonolithRule.AcrossProcesses(Repo.Root()));
    }

    [Fact]
    public void What_crosses_is_six_named_linked_files_and_nothing_from_the_site()
    {
        string root = Repo.Root();
        Assert.Equal(
            [
                "functions/JobHunt.Domain/Shared/Applications/ApplicationCap.cs",
                "functions/JobHunt.Domain/Shared/Companies/Blacklist.cs",
                "functions/JobHunt.Domain/Shared/Companies/CompanyKey.cs",
                "functions/JobHunt.Domain/Shared/Companies/FormerEmployers.cs",
                "functions/JobHunt.Domain/Shared/Tracker/TrackerCsvText.cs",
                "functions/JobHunt.Domain/Shared/Tracker/TrackerRows.cs",
            ],
            ModularMonolithRule.LinkedFrom(Path.Combine(root, "server"), Path.Combine(root, "functions"), root));
        Assert.Empty(ModularMonolithRule.LinkedFrom(Path.Combine(root, "server"), Path.Combine(root, "api"), root));
    }
}
// #endregion server-modular-monolith-tests

/// <summary>Finds the repository's root from the test's output folder.</summary>
internal static class Repo
{
    public static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "server", "JobHunt.Server.Domain")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("server/JobHunt.Server.Domain not found above " + AppContext.BaseDirectory);
    }

    public static IEnumerable<string> Sources(string ring) =>
        Directory.EnumerateFiles(Path.Combine(Root(), "server", ring), "*.cs", SearchOption.AllDirectories).Where(path => !TheYard.Rules.NamespaceRule.IsBuildOutput(path));
}
