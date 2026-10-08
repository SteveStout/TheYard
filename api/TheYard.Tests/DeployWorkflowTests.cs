using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The two deploy workflows carry the same constants twice (ADR: A permanent
/// address for the second site), and on 14 September one of them was changed
/// and the other was not: 1.0.0.128 pointed the first site at the Basic
/// database while the second site's workflow still named the paused one, and
/// its health card read the relational check as failed until 1.0.0.129
/// (ADR: The SQL Server backend, addendum of 14 September). This holds the
/// two files to one answer, and holds the database name to the one the
/// record says the sites run on.
/// </summary>
public class DeployWorkflowTests
{
    private static readonly string[] Shared = ["ACR", "RG", "AI_NAME", "SQL_SERVER", "SQL_DB", "MI_CLIENT_ID"];

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
    /// The prune reads which image the second site runs by its app name, a
    /// third copy of that name; it has to be the one the second site's own
    /// workflow deploys to, or the prune refuses on every run.
    /// </summary>
    [Fact]
    public void The_prune_names_the_second_site_the_way_its_own_workflow_does()
    {
        var match = Regex.Match(File.ReadAllText(Path.Combine(Repo.Root(), ".github", "workflows", "deploy.yml")), @"^\s+COSMOS_APP: (\S+)\s*$", RegexOptions.Multiline);
        Assert.True(match.Success, "deploy.yml's prune should name the second site");
        Assert.Equal(EnvOf("deploy-cosmos.yml")["APP"], match.Groups[1].Value);
    }
    // #endregion keep-ten

    // #region warm-edge
    [Theory]
    [InlineData("deploy.yml")]
    [InlineData("deploy-cosmos.yml")]
    public void Each_deploy_warms_the_edge_through_the_public_address_after_the_site_answers_and_never_fails_for_it(string file)
    {
        string workflow = File.ReadAllText(Path.Combine(Repo.Root(), ".github", "workflows", file));
        int verify = workflow.IndexOf("- name: Verify", StringComparison.Ordinal);
        int step = workflow.IndexOf("- name: Warm the edge", StringComparison.Ordinal);
        Assert.True(step > verify && verify > 0, $"{file} should warm the edge after Verify");
        string body = workflow[step..workflow.IndexOf("#endregion warm-edge", step, StringComparison.Ordinal)];
        Assert.Contains("continue-on-error: true", body, StringComparison.Ordinal);
        Assert.Contains("node scripts/warm-edge.mjs \"$SITE\" deploy", body, StringComparison.Ordinal);
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
    [InlineData("deploy-cosmos.yml")]
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
    [InlineData("deploy-cosmos.yml")]
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

    // #region workflows-agree
    [Fact]
    public void The_two_deploy_workflows_name_the_same_registry_group_server_database_and_identity()
    {
        var first = EnvOf("deploy.yml");
        var second = EnvOf("deploy-cosmos.yml");
        var wrong = Shared
            .Where(key => first.GetValueOrDefault(key) != second.GetValueOrDefault(key))
            .Select(key => $"{key}: deploy.yml says {first.GetValueOrDefault(key) ?? "(nothing)"} and deploy-cosmos.yml says {second.GetValueOrDefault(key) ?? "(nothing)"}")
            .ToList();
        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void The_deploys_point_at_the_database_the_record_says_the_sites_run_on()
    {
        // The Basic tier beside the paused serverless one, Steve's pick on 14 September.
        Assert.Equal("sqldb-theyard-ss-basic", EnvOf("deploy.yml")["SQL_DB"]);
        Assert.Equal("sqldb-theyard-ss-basic", EnvOf("deploy-cosmos.yml")["SQL_DB"]);
    }
    // #endregion workflows-agree
}
