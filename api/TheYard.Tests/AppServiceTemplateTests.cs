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

    [Fact]
    public void Both_sites_keep_the_portals_link_to_their_telemetry()
    {
        // A site's tags are written whole by a deployment, so a template that sets none removes the
        // hidden tag the portal links a site to its Application Insights component by; the what-if
        // of 7 October showed exactly that on the Azure SQL site.
        string template = Read("infra", "appservice.bicep");
        Assert.Contains("resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {", template, StringComparison.Ordinal);
        Assert.Contains("'hidden-link: /app-insights-resource-id': appInsights.id", template, StringComparison.Ordinal);
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

    [Theory]
    [InlineData("/ ", "sql")]
    [InlineData("https://theyard-cosmos.stevenstout.biz/ ", "cosmos")]
    public void The_edge_sends_each_domains_page_to_the_renderer_under_its_site_name(string rule, string site)
    {
        // The page is the bare path; the renderer draws it for the site the path names, and the
        // address it is sent to is the one Deploy Render verifies (ADR: A rendering service beside the API).
        var origin = Regex.Match(Read(".github", "workflows", "deploy-render.yml"), @"^\s{6}ORIGIN: (\S+)\s*$", RegexOptions.Multiline);
        Assert.True(origin.Success, "deploy-render.yml should name the origin it verifies");
        var lines = Read("edge", "_redirects").Split('\n').Select(line => line.TrimEnd()).ToList();
        int page = lines.FindIndex(line => line.StartsWith(rule, StringComparison.Ordinal));
        Assert.True(page >= 0, $"the edge should have a rule for {rule.Trim()}");
        Assert.Equal($"{rule}{origin.Groups[1].Value}/site/{site} 200!", lines[page]);
        // Above the catch-all for the same name, because the first rule that matches wins.
        int rest = lines.FindIndex(line => line.StartsWith(rule.TrimEnd() + "* ", StringComparison.Ordinal));
        Assert.True(rest > page, $"the page rule for {rule.Trim()} has to come before its catch-all");
        Assert.Contains($"sites.get(named[1])", Read("render", "server.mjs"), StringComparison.Ordinal);
        Assert.Contains($"{site}=https://", Read("infra", "render.bicep"), StringComparison.Ordinal);
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

    [Fact]
    public void The_template_is_what_runs_and_front_door_waits_behind_a_parameter_that_is_off()
    {
        string template = Read("infra", "main.bicep");

        // What runs is the module, and the module is given every secret the
        // template was given, so a deployment of it cannot blank a site.
        Assert.Contains("module compute 'appservice.bicep'", template, StringComparison.Ordinal);
        foreach (string parameter in new[] { "appImage", "appInsightsConnectionString", "authSigningKey", "adminKey" })
        {
            Assert.Contains($"{parameter}: {parameter}", template, StringComparison.Ordinal);
        }

        // Front Door is refused by the subscription this runs on, and a
        // default of true would make the file a description of something
        // that cannot be deployed, which is what it was until 1.0.0.157.
        Assert.Contains("param enableFrontDoor bool = false", template, StringComparison.Ordinal);
        // The platforms nothing runs on any more are gone with their branches.
        Assert.DoesNotContain("Microsoft.ContainerInstance", template, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.App/", template, StringComparison.Ordinal);
    }

    // #region the-renderer
    /// <summary>
    /// The rendering service's web app (ADR: A rendering service beside the API) is a third site on the
    /// plan. It holds no secret and no store: it listens where its image listens, answers the platform's
    /// health check at the path its door answers, reads the two sites at the addresses the template
    /// gives them, and pulls its image as the identity the sites run as. It is a template of its own,
    /// which main.bicep names as a module, so a deployment that creates it cannot touch either site.
    /// </summary>
    [Fact]
    public void The_renderer_is_a_site_on_the_same_plan_with_no_secret_and_no_store()
    {
        string template = Read("infra", "render.bicep");
        Assert.Contains("resource plan 'Microsoft.Web/serverfarms@2023-12-01' existing = {", template, StringComparison.Ordinal);
        Assert.Contains("{ name: 'WEBSITES_PORT', value: '8080' }", template, StringComparison.Ordinal);
        Assert.Contains("ENV PORT=8080", Read("render", "Dockerfile"), StringComparison.Ordinal);
        Assert.Contains("healthCheckPath: '/healthz'", template, StringComparison.Ordinal);
        Assert.Contains("url.pathname === '/healthz'", Read("render", "server.mjs"), StringComparison.Ordinal);
        Assert.Contains("acrUseManagedIdentityCreds: true", template, StringComparison.Ordinal);
        Assert.Contains("{ name: 'YARD_SITES', value: 'sql=https://${toLower(sqlSite)}.azurewebsites.net,cosmos=https://${toLower(cosmosSite)}.azurewebsites.net' }", template, StringComparison.Ordinal);
        foreach (string secret in new[] { "Auth__SigningKey", "Admin__Key", "ConnectionStrings", "APPLICATIONINSIGHTS_CONNECTION_STRING", "Cosmos__", "@secure()" })
        {
            Assert.DoesNotContain(secret, template, StringComparison.Ordinal);
        }

        string main = Read("infra", "main.bicep");
        Assert.Contains("module render 'render.bicep'", main, StringComparison.Ordinal);
    }

    [Fact]
    public void The_plan_the_templates_describe_is_the_size_it_runs_at()
    {
        // The plan moved to B2 on 8 October, when the memory gate read it at 91 per cent before a third
        // container (ADR: A rendering service beside the API). A template still saying B1 would move it
        // back on its next deployment.
        Assert.Contains("param skuName string = 'B2'", Read("infra", "appservice.bicep"), StringComparison.Ordinal);
        Assert.Contains("param skuName string = 'B2'", Read("infra", "main.bicep"), StringComparison.Ordinal);
    }

    // #region documents-agree
    /// <summary>
    /// The living documents that describe what runs name the plan size the template declares, and
    /// none of them still describes the old size as the one that runs. The plan moved and four pages
    /// kept saying B1 for a day; a reader of the Hosting page was told a machine that no longer existed.
    /// The size is read from the template, so the next move fails here until every page says so.
    /// </summary>
    [Fact]
    public void The_living_documents_name_the_plan_size_the_template_declares()
    {
        string sku = Regex.Match(Read("infra", "appservice.bicep"), @"param skuName string = '(B\d)'").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(sku), "appservice.bicep should declare the plan's size");
        string[] pages =
        [
            Path.Combine("docs", "hosting", "HOSTING.md"),
            Path.Combine("docs", "performance", "INFRASTRUCTURE-OVERVIEW.md"),
            Path.Combine("docs", "performance", "PERFORMANCE.md"),
            Path.Combine("docs", "app-architecture", "ARCHITECTURE.md"),
            "README.md",
            // The drawings claim the same machine: the hand-drawn infrastructure picture and the two-sites generator.
            Path.Combine("docs", "images", "infrastructure.svg"),
            Path.Combine("docs", "images", "two-sites.mjs"),
        ];
        foreach (string page in pages)
        {
            string text = Read(page);
            Assert.True(text.Contains($"Linux {sku}", StringComparison.Ordinal), $"{page} should name the plan as Linux {sku}");
            // The phrase every stale line used: the old size described as the plan that runs today.
            Assert.False(Regex.IsMatch(text, @"Linux B1 App Service plan\b(?! until)"), $"{page} still describes the plan as Linux B1");
        }
    }
    // #endregion documents-agree
    // #endregion the-renderer

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
