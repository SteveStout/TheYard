using System.Text.RegularExpressions;

namespace TheYard.Tests;

// #region docs-say-what-runs
/// <summary>
/// The pages that say how a version ships say what runs (ADR: The gate runs where the memory is):
/// the CI/CD page names every workflow in .github/workflows, a workflow either page links at main
/// exists there, and neither says the gate runs on the machine that ships, which stopped being true
/// when the gate moved to a GitHub runner.
/// </summary>
public sealed class DocsSayWhatRunsTests
{
    [Fact]
    public void The_readme_and_the_ci_page_name_the_workflows_that_exist_and_where_the_gate_runs()
    {
        string root = Repo.Root();
        var workflows = Directory.EnumerateFiles(Path.Combine(root, ".github", "workflows"), "*.yml").Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        string cicd = File.ReadAllText(Path.Combine(root, "docs", "ci-cd", "CICD.md"));
        string readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var wrong = new List<string>();
        wrong.AddRange(workflows.Where(name => !cicd.Contains($".github/workflows/{name}", StringComparison.Ordinal)).Select(name => $"CICD.md does not name {name}"));
        foreach (var (page, text) in new[] { ("README.md", readme), ("CICD.md", cicd) })
        {
            wrong.AddRange(Regex.Matches(text, @"blob/main/\.github/workflows/([\w.-]+\.yml)").Select(match => match.Groups[1].Value)
                .Where(name => !workflows.Contains(name)).Distinct().Select(name => $"{page} links {name}, which .github/workflows does not have"));
            if (Regex.IsMatch(text, @"on the machine that ships", RegexOptions.IgnoreCase))
            {
                wrong.Add($"{page} says the gate runs on the machine that ships");
            }
        }
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
    [Fact]
    public void The_projects_page_names_only_host_folders_the_tree_has()
    {
        // The host's folders as the page names them (Features/..., Shared/...) exist; Composition/ and
        // Endpoints/, the host before the slices, are named nowhere on it.
        string root = Repo.Root();
        string page = File.ReadAllText(Path.Combine(root, "docs", "app-architecture", "PROJECTS.md"));
        string host = Path.Combine(root, "api", "TheYard.Api");
        var wrong = Regex.Matches(page, @"`?((?:Features|Shared)/[\w./]*\w)")
            .Select(match => match.Groups[1].Value.Replace("<Slice>", "", StringComparison.Ordinal).TrimEnd('/'))
            .Where(path => !path.Contains('<', StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .Where(path => !Directory.Exists(Path.Combine(host, path)) && !File.Exists(Path.Combine(host, path)) && !File.Exists(Path.Combine(host, path + ".cs")))
            .Select(path => $"PROJECTS.md names {path}, which api/TheYard.Api does not have")
            .ToList();
        if (Regex.IsMatch(page, @"\b(Composition|Endpoints)/"))
        {
            wrong.Add("PROJECTS.md still names the Composition/ or Endpoints/ folder");
        }
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }
}
// #endregion docs-say-what-runs
