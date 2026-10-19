using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The deploy workflows. Until 19 October two of them carried the same constants
/// twice (ADR: A permanent address for the second site), and on 14 September one
/// was changed and the other was not: 1.0.0.128 pointed the first site at the Basic
/// database while the second site's workflow still named the paused one (ADR: The
/// SQL Server backend, addendum of 14 September). One web app now serves both
/// sites and The Shed (ADR: One web app for every .NET process), so one workflow
/// rolls it, and these hold that workflow to what it rolls and verifies.
/// </summary>
public class DeployWorkflowTests
{
    private static Dictionary<string, string> EnvOf(string workflow)
    {
        string path = Path.Combine(Repo.Root(), ".github", "workflows", workflow);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(File.ReadAllText(path), @"^\s{6}([A-Z_]+): (\S+)\s*$", RegexOptions.Multiline))
        {
            values[match.Groups[1].Value] = match.Groups[2].Value;
        }

        return values;
    }

    // #region keep-ten
    /// <summary>
    /// The registry keeps the newest ten images (Steve, 25 September). The
    /// deploy prunes after the site answers, keeps ten, refuses to delete when
    /// either site runs an image outside them, and never fails the deploy over
    /// a refusal (ADR: The deploy pipeline, the addendum of 25 September).
    /// </summary>
    [Fact]
    public void The_deploy_keeps_the_newest_ten_images_and_never_the_one_a_site_runs()
    {
        string workflow = File.ReadAllText(Path.Combine(Repo.Root(), ".github", "workflows", "deploy.yml"));
        int step = workflow.IndexOf("- name: Keep the newest ten images", StringComparison.Ordinal);
        Assert.True(step > 0, "deploy.yml should carry the step that keeps the newest ten images");
        Assert.True(step > workflow.IndexOf("- name: Verify", StringComparison.Ordinal), "the prune runs after the site answers");
        int next = workflow.IndexOf("- name:", step + 1, StringComparison.Ordinal);
        string body = next < 0 ? workflow[step..] : workflow[step..next];
        Assert.Contains("continue-on-error: true", body, StringComparison.Ordinal);
        Assert.Contains("timeout-minutes: 5", body, StringComparison.Ordinal);
        Assert.Contains(".[0:10]", body, StringComparison.Ordinal);
        Assert.Contains("if [ \"$tagged\" -lt 10 ]", body, StringComparison.Ordinal);
        Assert.Contains("outside the newest ten; nothing pruned", body, StringComparison.Ordinal);
        // A refused delete (no AcrDelete) warns and ends the step green.
        Assert.Contains("::warning::the registry refused a delete", body, StringComparison.Ordinal);
        // Digests are fixed strings, not patterns.
        Assert.DoesNotContain("grep -qx", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The prune deletes every untagged digest, which is safe only while each
    /// digest is a whole image. Provenance or a second platform makes each image
    /// an index whose children are untagged, and the prune would delete the
    /// children of the ten it keeps. Every build step holds provenance off.
    /// </summary>
    [Fact]
    public void Every_image_build_is_a_single_manifest_so_the_prune_deletes_whole_images()
    {
        string workflow = File.ReadAllText(Path.Combine(Repo.Root(), ".github", "workflows", "deploy.yml"));
        int builds = Regex.Matches(workflow, @"uses: docker/build-push-action@").Count;
        Assert.True(builds > 0, "deploy.yml should build an image");
        Assert.Equal(builds, Regex.Matches(workflow, @"^\s+provenance: false\s*$", RegexOptions.Multiline).Count);
        Assert.DoesNotContain("platforms:", workflow, StringComparison.Ordinal);
    }

    /// <summary>
    /// The prune reads which image the web app runs before it deletes anything. One web app
    /// runs the image since 19 October, so it reads that one and names no retired site.
    /// </summary>
    [Fact]
    public void The_prune_reads_the_image_of_the_one_web_app_it_rolls()
    {
        string workflow = File.ReadAllText(Path.Combine(Repo.Root(), ".github", "workflows", "deploy.yml"));
        Assert.Contains("for app in \"$APP\"; do", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("COSMOS_APP", workflow, StringComparison.Ordinal);
    }
    // #endregion keep-ten

    // #region warm-edge
    [Theory]
    [InlineData("deploy.yml")]
    public void Each_deploy_warms_the_edge_through_the_public_address_after_the_site_answers_and_never_fails_for_it(string file)
    {
        string workflow = File.ReadAllText(Path.Combine(Repo.Root(), ".github", "workflows", file));
        int verify = workflow.IndexOf("- name: Verify", StringComparison.Ordinal);
        int step = workflow.IndexOf("- name: Warm the edge", StringComparison.Ordinal);
        Assert.True(step > verify && verify > 0, $"{file} should warm the edge after Verify");
        string body = workflow[step..workflow.IndexOf("#endregion warm-edge", step, StringComparison.Ordinal)];
        Assert.Contains("continue-on-error: true", body, StringComparison.Ordinal);
        Assert.Contains("node scripts/warm-edge.mjs \"$SITE\" deploy", body, StringComparison.Ordinal);
        Assert.Contains("node scripts/warm-edge.mjs \"$COSMOS_SITE\" deploy", body, StringComparison.Ordinal);
        Assert.StartsWith("https://theyard", EnvOf(file)["SITE"], StringComparison.Ordinal);
        Assert.Contains("TheYard-SelfRead/1", File.ReadAllText(Path.Combine(Repo.Root(), "scripts", "warm-edge.mjs")), StringComparison.Ordinal);
    }
    // #endregion warm-edge

    // #region listen-first-verify
    /// <summary>
    /// The container listens before its catalogue has loaded (ADR: The ports learn to wait,
    /// the addendum on listening first), so a deploy is done only when readiness says the
    /// catalogue is in: each deploy's Verify step polls /readyz, and reads the filters, which
    /// need the catalogue, only after it.
    /// </summary>
    [Theory]
    [InlineData("deploy.yml")]
    public void Each_deploy_waits_for_readiness_before_it_reads_the_filters(string file)
    {
        string workflow = File.ReadAllText(Path.Combine(Repo.Root(), ".github", "workflows", file));
        int verify = workflow.IndexOf("- name: Verify", StringComparison.Ordinal);
        Assert.True(verify > 0, $"{file} should have a Verify step");
        int poll = workflow.IndexOf("\"$ORIGIN/readyz\"", verify, StringComparison.Ordinal);
        int facets = workflow.IndexOf("\"$ORIGIN/api/facets\"", verify, StringComparison.Ordinal);
        Assert.True(poll > verify, $"{file} should poll the origin's /readyz in Verify");
        Assert.True(facets > poll, $"{file} should read the filters only after readiness");
        string loop = workflow[workflow.LastIndexOf("for i in $(seq 1 120)", poll, StringComparison.Ordinal)..poll];
        Assert.Contains("for i in $(seq 1 120)", loop, StringComparison.Ordinal);
    }
    // #endregion listen-first-verify

    // #region verify-budget
    /// <summary>
    /// Verify's wait for the version is sized from the rolls it reads: the new container
    /// starts 8 to 12 minutes after the image push and is ready 2 to 3.5 minutes after that,
    /// and a ten-minute wait reported a roll that worked as a failure (Deploy #282). Each
    /// deploy waits twenty-five minutes for the version, gives each read thirty seconds,
    /// because an API read waits for the catalogue, and holds the job open longer than
    /// Verify can take (ADR: The deploy pipeline, the addendum on sizing Verify).
    /// </summary>
    [Theory]
    [InlineData("deploy.yml")]
    public void Each_deploy_waits_long_enough_for_the_roll_it_measured(string file)
    {
        string workflow = File.ReadAllText(Path.Combine(Repo.Root(), ".github", "workflows", file));
        int verify = workflow.IndexOf("- name: Verify", StringComparison.Ordinal);
        int ready = workflow.IndexOf("\"$ORIGIN/readyz\"", verify, StringComparison.Ordinal);
        Assert.True(verify > 0 && ready > verify, $"{file} should wait for the version before readiness");
        string wait = workflow[verify..ready];
        var budget = Regex.Match(wait, @"^\s+budget=(\d+)\s*$", RegexOptions.Multiline);
        Assert.True(budget.Success, $"{file} should name its wait for the version as budget=");
        Assert.Equal(1500, int.Parse(budget.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains("--max-time 30 \"$ORIGIN/api/version\"", wait, StringComparison.Ordinal);
        Assert.DoesNotContain("--max-time 10 \"$ORIGIN/api/version\"", wait, StringComparison.Ordinal);
        var job = Regex.Match(workflow, @"^    timeout-minutes: (\d+)\s*$", RegexOptions.Multiline);
        Assert.True(job.Success, $"{file} should give its job a timeout");
        // The version's twenty-five minutes, readiness's ten, and ten for what comes before Verify.
        Assert.True(int.Parse(job.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) * 60 >= 1500 + 600 + 600, $"{file}'s job ends before its Verify can");
    }
    // #endregion verify-budget

    // #region deploy-render
    /// <summary>
    /// The renderer rolls on the same push as the two sites, as the same version, and is done when it
    /// answers its own readiness with that version (ADR: A rendering service beside the API). The
    /// version comes from the changelog the way Deploy reads it, the image is one manifest like every
    /// other, and its wait is sized like the sites'.
    /// </summary>
    [Fact]
    public void The_renderer_rolls_as_the_version_the_sites_roll_and_waits_for_its_own_readiness()
    {
        string workflow = File.ReadAllText(Path.Combine(Repo.Root(), ".github", "workflows", "deploy-render.yml"));
        string deploy = File.ReadAllText(Path.Combine(Repo.Root(), ".github", "workflows", "deploy.yml"));
        Assert.Contains("file: render/Dockerfile", workflow, StringComparison.Ordinal);
        Assert.Contains("IMAGE=$ACR/theyard-render:v$N", workflow, StringComparison.Ordinal);
        Assert.Contains("provenance: false", workflow, StringComparison.Ordinal);
        Assert.Contains("APP_VERSION=${{ env.APP_VERSION }}", workflow, StringComparison.Ordinal);
        string versionLine = Regex.Match(deploy, @"VERSIONS=\$\(sed -n '([^']+)' docs/CHANGELOG\.md\)").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(versionLine), "deploy.yml should read the version from the changelog");
        Assert.Contains($"sed -n '{versionLine}' docs/CHANGELOG.md", workflow, StringComparison.Ordinal);
        int verify = workflow.IndexOf("- name: Verify", StringComparison.Ordinal);
        Assert.True(verify > 0, "deploy-render.yml should verify the roll");
        string wait = workflow[verify..];
        Assert.Contains("budget=1500", wait, StringComparison.Ordinal);
        Assert.Contains("--max-time 30 \"$ORIGIN/readyz\"", wait, StringComparison.Ordinal);
        Assert.Contains("\\\"version\\\":\\\"$APP_VERSION\\\"", wait, StringComparison.Ordinal);
        Assert.Equal(EnvOf("deploy.yml")["ACR"], EnvOf("deploy-render.yml")["ACR"]);
        Assert.Equal(EnvOf("deploy.yml")["RG"], EnvOf("deploy-render.yml")["RG"]);
    }
    // #endregion deploy-render

    // #region one-web-app
    /// <summary>
    /// Deploy rolls the one web app the template describes, and nothing rolls the three it
    /// replaced (ADR: One web app for every .NET process).
    /// </summary>
    [Fact]
    public void Deploy_rolls_the_one_web_app_the_template_describes_and_the_retired_deploys_are_gone()
    {
        var env = EnvOf("deploy.yml");
        Assert.Equal("APP-THEYARD-ONE-SS-ZMNETJ67BN5H2", env["APP"]);
        Assert.Equal($"https://{env["APP"].ToLowerInvariant()}.azurewebsites.net", env["ORIGIN"]);
        Assert.Contains("'APP-${toUpper(baseName)}-ONE-${toUpper(ownerTag)}-${toUpper(suffix)}'", File.ReadAllText(Path.Combine(Repo.Root(), "infra", "appservice.bicep")), StringComparison.Ordinal);
        foreach (string retired in new[] { "deploy-cosmos.yml", "deploy-shed.yml" })
        {
            Assert.False(File.Exists(Path.Combine(Repo.Root(), ".github", "workflows", retired)), $"{retired} rolled a web app that no longer exists");
        }
        Assert.Equal(env["ACR"], EnvOf("deploy-render.yml")["ACR"]);
    }

    /// <summary>
    /// Before any domain, Verify reads the other two names at the origin, as the edge sends
    /// them: the Cosmos DB site's name must get Azure Cosmos DB with no store named, and The
    /// Shed's name must reach The Shed's own health check and page. A roll that lost the host
    /// routing would otherwise go green while one domain served the other site's store.
    /// </summary>
    [Fact]
    public void Verify_reads_each_name_at_the_origin_before_any_domain()
    {
        string workflow = File.ReadAllText(Path.Combine(Repo.Root(), ".github", "workflows", "deploy.yml"));
        int names = workflow.IndexOf("#region verify-names", StringComparison.Ordinal);
        int domains = workflow.IndexOf("#region verify-domains", StringComparison.Ordinal);
        Assert.True(names > 0 && domains > names, "deploy.yml should read the names at the origin before the domains");
        string body = workflow[names..domains];
        Assert.Contains("-H \"X-Yard-Host: ${COSMOS_SITE#https://}\" \"$ORIGIN/api/stores\"", body, StringComparison.Ordinal);
        Assert.Contains("'\"current\":\"cosmos\"'", body, StringComparison.Ordinal);
        Assert.Contains("-H \"X-Yard-Host: ${SHED_SITE#https://}\" \"$ORIGIN/healthz\"", body, StringComparison.Ordinal);
        Assert.Contains("<title>The Shed</title>", body, StringComparison.Ordinal);
        Assert.Equal("X-Yard-Host", TheYard.Api.SiteHosts.HeaderName);
    }

    /// <summary>
    /// The edge moves after Deploy succeeds and never beside it, and reads both names until each
    /// serves the build: a rule that moved before its origin was verified would send visitors to a
    /// web app still starting (ADR: One web app for every .NET process).
    /// </summary>
    [Fact]
    public void The_edge_moves_only_after_deploy_succeeds_and_reads_both_names()
    {
        string workflow = File.ReadAllText(Path.Combine(Repo.Root(), ".github", "workflows", "edge-branch.yml"));
        Assert.Contains("workflow_run:\n    workflows: [Deploy]\n    types: [completed]", workflow.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("github.event.workflow_run.conclusion == 'success'", workflow, StringComparison.Ordinal);
        Assert.Contains("git diff --quiet origin/edge HEAD -- edge/ netlify.toml", workflow, StringComparison.Ordinal);
        Assert.Contains("https://theyard.stevenstout.biz https://theyard-cosmos.stevenstout.biz", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("  push:", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void The_deploy_points_at_the_database_the_record_says_the_sites_run_on()
    {
        // The Basic tier beside the paused serverless one, Steve's pick on 14 September.
        Assert.Equal("sqldb-theyard-ss-basic", EnvOf("deploy.yml")["SQL_DB"]);
    }
    // #endregion one-web-app
}
