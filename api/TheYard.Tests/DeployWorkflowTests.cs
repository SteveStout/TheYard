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
