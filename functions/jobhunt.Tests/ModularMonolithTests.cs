using Xunit;

namespace JobHunt.Functions.Tests;

// #region function-modular-monolith-tests
/// <summary>
/// The job hunt's Function as a modular monolith (ADR-119): one deployable, the Functions app, cut into modules that
/// are its slices; a slice's files name no other slice; and the site is a second process it shares rings and linked
/// files with, never a project. The bodies are tests/rules/ModularMonolithRule.cs, the same ones the site's suite runs.
/// </summary>
public sealed class ModularMonolithTests
{
    private static readonly string[] Rings = ["JobHunt.Domain", "JobHunt.Application", "JobHunt.Infrastructure"];

    [Fact]
    public void The_Function_is_one_deployable()
    {
        var deployables = TheYard.Rules.ModularMonolithRule.Deployables(Path.Combine(Root(), "functions"), Root());
        Assert.Equal(["functions/jobhunt/JobHunt.Functions.csproj"], deployables);
    }

    [Fact]
    public void A_slice_s_files_name_no_other_slice_in_any_ring()
    {
        var wrong = Rings
            .SelectMany(ring => TheYard.Rules.ModularMonolithRule.CrossSliceUsings(Path.Combine(Root(), "functions", ring), Rings, Root()))
            .ToList();
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void The_two_processes_share_rings_and_linked_files_never_a_project()
    {
        string root = Root();
        Assert.Empty(TheYard.Rules.ModularMonolithRule.ProjectReferencesAcross(Path.Combine(root, "functions"), Path.Combine(root, "api"), root));
        Assert.Equal(["api/TheYard.Domain/Features/Admin/NightlySlots.cs"], TheYard.Rules.ModularMonolithRule.LinkedFrom(Path.Combine(root, "functions"), Path.Combine(root, "api"), root));
    }

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "functions", "JobHunt.Domain")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("functions/JobHunt.Domain not found above " + AppContext.BaseDirectory);
    }
}
// #endregion function-modular-monolith-tests
