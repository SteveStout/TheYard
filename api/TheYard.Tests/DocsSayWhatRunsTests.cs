using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

// #region docs-say-what-runs
/// <summary>
/// The pages and the doc comments that say what runs say what actually runs (ADR: The gate runs where
/// the memory is): the CI/CD page names every workflow in .github/workflows, a workflow either page
/// links at main exists there, neither says the gate runs on the machine that ships, which stopped
/// being true when the gate moved to a GitHub runner, and every doc comment that lists the Cloud
/// infrastructure cards reads its keys and its count off the catalogue the host built.
/// </summary>
public sealed partial class DocsSayWhatRunsTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>The counts these doc comments spell out, as far as a card list is ever likely to run.</summary>
    private static readonly string[] Counted =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty",
    ];

    /// <summary>
    /// Every doc comment that names the resource cards names the catalogue's own keys in its own order,
    /// and the one that counts them counts the catalogue's number: four places said seven keys or eight
    /// cards while the catalogue held eleven, so a card added since went unsaid (R3, 1.0.4.113).
    /// </summary>
    [Fact]
    public void Every_doc_that_lists_the_cloud_cards_reads_its_keys_and_its_count_off_the_catalogue()
    {
        var keys = factory.Services.GetRequiredService<IReadOnlyList<(ResourceCard Card, IResourceHistory History)>>()
            .Select(entry => entry.Card.Key)
            .ToList();
        Assert.NotEmpty(keys);
        string root = Repo.Root();
        string Host(params string[] parts) => Path.Combine([root, "api", "TheYard.Api", "Features", "Admin", "Cloud", .. parts]);
        (string Where, string File, Regex Pattern)[] lists =
        [
            ("ICloudCards.Knows", Path.Combine(root, "api", "TheYard.Application", "Features", "Admin", "Cloud", "ReadCloudCardHandler.cs"), KnowsKeys()),
            ("the cloud route's summary", Host("CloudEndpoints.cs"), RouteKeys()),
            ("ResourceCard.Key", Host("CloudCards.cs"), CardKeys()),
        ];
        var wrong = new List<string>();
        foreach (var (where, file, pattern) in lists)
        {
            var match = pattern.Match(File.ReadAllText(file));
            if (!match.Success)
            {
                wrong.Add($"{where} no longer lists its keys where this test reads them");
                continue;
            }
            var said = match.Groups[1].Value.Split([",", " or ", " and "], StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim().TrimEnd('.'))
                .Where(part => part.Length > 0)
                .ToList();
            if (!said.SequenceEqual(keys, StringComparer.Ordinal))
            {
                wrong.Add($"{where} says {string.Join(", ", said)}; the catalogue holds {string.Join(", ", keys)}");
            }
        }
        var counted = ModuleCount().Match(File.ReadAllText(Host("Module.cs")));
        if (!counted.Success)
        {
            wrong.Add("Cloud/Module.cs's summary no longer counts the resource cards where this test reads it");
        }
        else if (counted.Groups[1].Value != Counted[keys.Count])
        {
            wrong.Add($"Cloud/Module.cs's summary says {counted.Groups[1].Value} resource cards; the catalogue holds {Counted[keys.Count]}");
        }
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [GeneratedRegex(@"<param name=""key"">([^<]+)</param>")]
    private static partial Regex KnowsKeys();

    [GeneratedRegex(@"One resource under Cloud infrastructure: ([^""]+), for 24h")]
    private static partial Regex RouteKeys();

    [GeneratedRegex(@"The card's route and container key: ([^<]+)</param>")]
    private static partial Regex CardKeys();

    [GeneratedRegex(@"The (\w+) resource cards under Cloud infrastructure")]
    private static partial Regex ModuleCount();

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
