using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The template for the plan and its two sites, held to the two container
/// group files it took over from (ADR: One plan, two sites). The move's
/// instruction was to read every setting out of those files rather than
/// remember it, and a list copied once is a list that drifts: a setting added
/// to a container group tomorrow and not to the sites would come up missing on
/// the machine that actually serves, with nothing red anywhere.
/// </summary>
public class AppServiceTemplateTests
{
    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([Repo.Root(), .. parts]));

    private static HashSet<string> SettingsOf(string containerGroupFile)
    {
        // The container's environment: "- name: X" followed by a value or a
        // secureValue. The container's own name is a "- name:" too, followed
        // by "properties:", which is how it is told apart.
        var names = Regex.Matches(
                Read("infra", containerGroupFile),
                @"^\s*- name: (\S+)\s*\n(?:\s*#.*\n)*\s*(?:value|secureValue):",
                RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.True(names.Count >= 10, $"{containerGroupFile} should carry the container's settings, and {names.Count} were found");
        return names;
    }

    // #region settings-carried
    [Theory]
    [InlineData("aci-theyard.yaml")]
    [InlineData("aci-theyard-cosmos.yaml")]
    public void Every_setting_a_container_group_carried_is_a_setting_the_sites_carry(string containerGroupFile)
    {
        string template = Read("infra", "appservice.bicep");
        var missing = SettingsOf(containerGroupFile)
            .Where(name => !template.Contains($"{{ name: '{name}', value: ", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0, $"{containerGroupFile} sets these and infra/appservice.bicep does not: {string.Join(", ", missing)}");
    }
    // #endregion settings-carried

    [Fact]
    public void The_two_sites_default_to_the_two_stores_and_listen_where_the_image_listens()
    {
        string template = Read("infra", "appservice.bicep");

        Assert.Contains("storeDefault: 'sql'", template, StringComparison.Ordinal);
        Assert.Contains("storeDefault: 'cosmos'", template, StringComparison.Ordinal);
        Assert.Contains("{ name: 'WEBSITES_PORT', value: '8080' }", template, StringComparison.Ordinal);
        Assert.Contains("ENV ASPNETCORE_URLS=http://+:8080", Read("Dockerfile"), StringComparison.Ordinal);
        Assert.Contains("healthCheckPath: '/healthz'", template, StringComparison.Ordinal);
    }

    // #region edge-and-rolls-agree
    [Theory]
    [InlineData("deploy.yml", "/* ")]
    [InlineData("deploy-cosmos.yml", "https://theyard-cosmos.stevenstout.biz/* ")]
    public void The_edge_sends_each_name_to_the_origin_its_deploy_verifies(string workflow, string rule)
    {
        // Three places name an origin: the template creates it, the deploy
        // verifies it, and the edge sends visitors to it. A site renamed in
        // one of them is a roll that goes green while the public name serves
        // something else.
        var origin = Regex.Match(Read(".github", "workflows", workflow), @"^\s{6}ORIGIN: (\S+)\s*$", RegexOptions.Multiline);
        Assert.True(origin.Success, $"{workflow} should name the origin it verifies");

        string line = Read("edge", "_redirects")
            .Split('\n')
            .Single(candidate => candidate.StartsWith(rule, StringComparison.Ordinal));
        Assert.Equal($"{rule}{origin.Groups[1].Value}/:splat 200!", line.TrimEnd());

        var app = Regex.Match(Read(".github", "workflows", workflow), @"^\s{6}APP: (\S+)\s*$", RegexOptions.Multiline);
        Assert.True(app.Success, $"{workflow} should name the web app it rolls");
        Assert.Equal($"https://{app.Groups[1].Value.ToLowerInvariant()}.azurewebsites.net", origin.Groups[1].Value);
    }

    [Fact]
    public void The_template_and_the_deploys_name_the_same_database()
    {
        string template = Read("infra", "appservice.bicep");
        var named = Regex.Match(Read(".github", "workflows", "deploy.yml"), @"^\s{6}SQL_DB: (\S+)\s*$", RegexOptions.Multiline);

        Assert.True(named.Success, "deploy.yml should name the database");
        Assert.Contains($"param sqlDatabase string = '{named.Groups[1].Value}'", template, StringComparison.Ordinal);
    }
    // #endregion edge-and-rolls-agree

    // #region never-complete
    [Fact]
    public void Nothing_in_the_repository_deploys_a_template_in_complete_mode()
    {
        // The resource group holds the databases, the document store, the
        // registry and the identity, and none of them is in a template. A
        // complete-mode deployment deletes whatever the template leaves out.
        var offenders = new List<string>();
        foreach (string folder in new[] { "infra", "scripts", Path.Combine(".github", "workflows") })
        {
            foreach (string file in Directory.EnumerateFiles(Path.Combine(Repo.Root(), folder), "*", SearchOption.AllDirectories))
            {
                if (Regex.IsMatch(File.ReadAllText(file), @"--mode\s+complete|mode:\s*'?complete", RegexOptions.IgnoreCase))
                {
                    offenders.Add(Path.GetRelativePath(Repo.Root(), file));
                }
            }
        }

        Assert.True(offenders.Count == 0, "complete mode is named in: " + string.Join(", ", offenders));
    }
    // #endregion never-complete
}
