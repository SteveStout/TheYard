using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The template for the plan and the one web app that serves both sites and The
/// Shed (ADR: One web app for every .NET process), held to what the code reads,
/// to what the deploy rolls and to where the edge sends each name. Until 19
/// October it was held to the two container group files it took over from (ADR:
/// One plan, two sites), which retired with the per-site settings they carried.
/// </summary>
public class AppServiceTemplateTests
{
    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([Repo.Root(), .. parts]));

    // #region settings-carried
    /// <summary>
    /// The names the process answers to and The Shed's folder are settings the code reads
    /// (Shared/SiteSettings.cs, Shared/Sites/HostedSiteMount.cs), so a template without them is a web app that serves one
    /// store on every name and no Shed at all, with nothing red anywhere. The per-site settings
    /// of the four-app shape are gone: a Store__Default left behind would pin every name to one
    /// store, and a Peer__Url would read a web app that no longer exists.
    /// </summary>
    [Fact]
    public void The_web_app_carries_the_names_and_the_sheds_folder_and_none_of_the_per_site_settings()
    {
        string template = Read("infra", "appservice.bicep");
        foreach (string setting in new[] { "Sites__0__Host", "Sites__0__Store", "Sites__1__Host", "Sites__1__Store", "Sites__2__Host", "Shed__ContentRoot", "Files__Home", "Files__AllowWrites", "Files__MaxUploadBytes", "Site__Url", "Catalogue__Keep" })
        {
            Assert.Contains($"{{ name: '{setting}', value: ", template, StringComparison.Ordinal);
        }
        foreach (string retired in new[] { "Store__Default", "Peer__Url", "Peer__Site" })
        {
            Assert.DoesNotContain(retired, template, StringComparison.Ordinal);
        }
        Assert.Contains("ENV Shed__ContentRoot=/app/shed", Read("Dockerfile"), StringComparison.Ordinal);
        Assert.Contains("{ name: 'Shed__ContentRoot', value: '/app/shed' }", template, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(Repo.Root(), "infra", "aci-theyard.yaml")));
        Assert.False(File.Exists(Path.Combine(Repo.Root(), "samples", "theshed", "infra", "site.bicep")));
    }
    // #endregion settings-carried

    [Fact]
    public void The_one_web_app_answers_the_three_names_and_listens_where_the_image_listens()
    {
        string template = Read("infra", "appservice.bicep");

        Assert.Contains("hostSql: 'theyard.stevenstout.biz'", template, StringComparison.Ordinal);
        Assert.Contains("hostCosmos: 'theyard-cosmos.stevenstout.biz'", template, StringComparison.Ordinal);
        Assert.Contains("hostShed: 'theshed.stevenstout.biz'", template, StringComparison.Ordinal);
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
    /// <summary>
    /// Netlify proxy rules from netlify.toml, in file order: where each comes from, where it
    /// goes, and the X-Yard-Host header it adds.
    /// </summary>
    private static List<(string From, string To, string? Host)> ProxyRules()
    {
        return Regex.Matches(
                Read("netlify.toml"),
                @"\[\[redirects\]\]\s*\n\s*from = ""([^""]+)""\s*\n\s*to = ""([^""]+)""\s*\n\s*status = 200\s*\n\s*force = true\s*\n\s*headers = \{X-Yard-Host = ""([^""]+)""\}")
            .Select(match => (match.Groups[1].Value, match.Groups[2].Value, (string?)match.Groups[3].Value))
            .ToList();
    }

    [Theory]
    [InlineData("/*", "theyard.stevenstout.biz", "SITE")]
    [InlineData("https://theyard-cosmos.stevenstout.biz/*", "theyard-cosmos.stevenstout.biz", "COSMOS_SITE")]
    public void The_edge_sends_each_name_to_the_origin_the_deploy_verifies_and_says_which_name_it_was(string from, string host, string siteKey)
    {
        // Three places name the origin: the template creates it, the deploy verifies it, and the
        // edge sends visitors to it. A web app renamed in one of them is a roll that goes green
        // while the public name serves something else. And the edge says which name the visitor
        // typed, because both names reach the same origin and the store follows the name.
        string workflow = Read(".github", "workflows", "deploy.yml");
        var origin = Regex.Match(workflow, @"^\s{6}ORIGIN: (\S+)\s*$", RegexOptions.Multiline);
        Assert.True(origin.Success, "deploy.yml should name the origin it verifies");
        var site = Regex.Match(workflow, $@"^\s{{6}}{siteKey}: (\S+)\s*$", RegexOptions.Multiline);
        Assert.Equal($"https://{host}", site.Groups[1].Value);

        var rule = ProxyRules().Single(candidate => candidate.From == from);
        Assert.Equal($"{origin.Groups[1].Value}/:splat", rule.To);
        Assert.Equal(host, rule.Host);
        Assert.Contains($"host{(host.StartsWith("theyard-cosmos", StringComparison.Ordinal) ? "Cosmos" : "Sql")}: '{host}'", Read("infra", "appservice.bicep"), StringComparison.Ordinal);

        var app = Regex.Match(workflow, @"^\s{6}APP: (\S+)\s*$", RegexOptions.Multiline);
        Assert.Equal($"https://{app.Groups[1].Value.ToLowerInvariant()}.azurewebsites.net", origin.Groups[1].Value);
        // The Cosmos DB site's rule comes before the catch-all, because the first that matches wins.
        var rules = ProxyRules();
        Assert.True(rules.FindIndex(r => r.From.StartsWith("https://theyard-cosmos", StringComparison.Ordinal)) < rules.FindIndex(r => r.From == "/*"));
        // And edge/_redirects, which Netlify reads first, proxies nothing to a web app of the API's.
        Assert.DoesNotContain("app-theyard-ss-", Read("edge", "_redirects"), StringComparison.Ordinal);
        Assert.DoesNotContain("/* https://app-theyard-one", Read("edge", "_redirects"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/ ", "sql")]
    [InlineData("https://theyard-cosmos.stevenstout.biz/ ", "cosmos")]
    public void The_edge_sends_each_domains_page_to_the_renderer_under_its_site_name(string rule, string site)
    {
        // The page is the bare path; the renderer draws it for the site the path names, and the
        // address it is sent to is the one Deploy Render verifies (ADR: A rendering service beside the API).
        // These are in edge/_redirects, which Netlify reads before netlify.toml's proxy rules, so the
        // page is never caught by the catch-all.
        var origin = Regex.Match(Read(".github", "workflows", "deploy-render.yml"), @"^\s{6}ORIGIN: (\S+)\s*$", RegexOptions.Multiline);
        Assert.True(origin.Success, "deploy-render.yml should name the origin it verifies");
        var lines = Read("edge", "_redirects").Split('\n').Select(line => line.TrimEnd()).ToList();
        int page = lines.FindIndex(line => line.StartsWith(rule, StringComparison.Ordinal));
        Assert.True(page >= 0, $"the edge should have a rule for {rule.Trim()}");
        Assert.Equal($"{rule}{origin.Groups[1].Value}/site/{site} 200!", lines[page]);
        Assert.Contains("sites.get(name)", Read("render", "server.mjs"), StringComparison.Ordinal);
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
    /// The rendering service's web app (ADR: A rendering service beside the API) is the second web app
    /// on the plan. It holds no secret and no store: it listens where its image listens, answers the
    /// platform's health check at the path its door answers, reads both sites at the one web app's
    /// address with the site's store named on each read, and pulls its image as the identity the
    /// web app runs as. It is a template of its own,
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
        Assert.Contains("{ name: 'YARD_SITES', value: 'sql=https://${toLower(oneSite)}.azurewebsites.net,cosmos=https://${toLower(oneSite)}.azurewebsites.net' }", template, StringComparison.Ordinal);
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
