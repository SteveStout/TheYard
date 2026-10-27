namespace TheYard.Tests;

/// <summary>
/// The workflows that took the last of the laptop's work (ADR: The gate runs where the memory is, the addendum of
/// 1.0.4.105): the gate draws the version card and runs the Function's suite, and the Function's deploy, a blog
/// post's save and an Azure Monitor read are each a workflow of their own.
///
/// <para>Each was a queue job that ran under the owner's sign-in on a machine that had to be on. The danger in
/// moving them is a workflow that does more than the job it replaced: a Function deploy that also sets an app
/// setting could turn the campaign on, and a save that prints the operator's key would publish it. So each is held
/// here to its job, its sign-in and what it must never do.</para>
/// </summary>
public class MachineFreeWorkflowsTests
{
    private static string Workflow(string name) =>
        File.ReadAllText(Path.Combine(Repo.Root(), ".github", "workflows", name)).ReplaceLineEndings("\n");

    // #region the-card
    /// <summary>The gate draws the version card before its tests and promote folds the drawn files into the commit.</summary>
    [Fact]
    public void The_gate_draws_the_version_card_and_promote_folds_it()
    {
        string gate = Workflow("gate.yml");
        int card = gate.IndexOf("node docs/images/og.mjs", StringComparison.Ordinal);
        Assert.True(card > 0 && card < gate.IndexOf("dotnet test api --no-build", StringComparison.Ordinal), "the card is drawn before xUnit reads it");
        Assert.Contains("cp docs/images/og.svg public/og.png \"$RAW/\"", gate, StringComparison.Ordinal);
        string promote = File.ReadAllText(Path.Combine(Repo.Root(), "scripts", "promote.sh"));
        Assert.Contains("cp \"$raw/og.svg\" docs/images/og.svg", promote, StringComparison.Ordinal);
        Assert.Contains("git add data/test-results.json README.md docs/built-with-ai/BUILT-WITH-AI.md docs/images/og.svg public/og.png", promote, StringComparison.Ordinal);
    }
    // #endregion the-card

    // #region the-function
    /// <summary>The Function deploys on a run of its own, signed in as the deploy identity, and never sets an app setting.</summary>
    [Fact]
    public void The_function_deploys_on_its_own_run_and_never_sets_an_app_setting()
    {
        string deploy = Workflow("deploy-function.yml");
        Assert.Contains("environment: gate", deploy, StringComparison.Ordinal);
        Assert.Contains("az functionapp deployment source config-zip", deploy, StringComparison.Ordinal);
        Assert.Contains("defaults:\n  run:\n    shell: bash\n", deploy, StringComparison.Ordinal);
        Assert.Contains("git merge-base --is-ancestor \"$SHA\" origin/main", deploy, StringComparison.Ordinal);
        Assert.Contains("[ -s \"$RUNNER_TEMP/before.txt\" ] || {", deploy, StringComparison.Ordinal);
        Assert.Contains("-p:SourceRevisionId=\"$SHA\"", deploy, StringComparison.Ordinal);
        Assert.Contains("[ \"$commit\" = \"$SHA\" ] || {", deploy, StringComparison.Ordinal);
        Assert.DoesNotContain("[ \"$code\" = 204 ]", deploy, StringComparison.Ordinal);
        Assert.True(deploy.IndexOf("Wait out the records run", StringComparison.Ordinal) > deploy.IndexOf("name: Publish and zip", StringComparison.Ordinal), "the records-run wait is read just before the zip deploy");
        Assert.DoesNotContain("workflow_call", deploy, StringComparison.Ordinal);

        string gate = Workflow("gate.yml");
        Assert.Contains("gh workflow run deploy-function.yml --ref main", gate, StringComparison.Ordinal);
        Assert.DoesNotContain("uses: ./.github/workflows/deploy-function.yml", gate, StringComparison.Ordinal);
    }

    /// <summary>Every az call the Function's deploy makes is one of the reads and the one zip deploy it needs: nothing that sets, assigns or deletes.</summary>
    [Fact]
    public void The_function_deploy_calls_only_the_az_verbs_it_needs()
    {
        string[] allowed = ["functionapp function list", "functionapp show", "resource show", "functionapp deployment source config-zip"];
        var calls = System.Text.RegularExpressions.Regex.Matches(string.Join('\n', Workflow("deploy-function.yml").Split('\n').Where(line => !line.TrimStart().StartsWith('#'))), @"\baz ((?:[a-z-]+ ?)+?)(?= -|$)", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value.Trim()).ToList();
        Assert.NotEmpty(calls);
        Assert.All(calls, call => Assert.Contains(call, allowed));
    }

    /// <summary>The Function's suite runs in the gate, before main moves, and its red is the run's red.</summary>
    [Fact]
    public void The_functions_suite_runs_in_the_gate_and_turns_it_red()
    {
        string gate = Workflow("gate.yml");
        Assert.Contains("dotnet test functions/jobhunt.Tests", gate, StringComparison.Ordinal);
        Assert.Contains("for k in sqlproj functions xunit live;", gate, StringComparison.Ordinal);
    }
    // #endregion the-function

    // #region the-blog
    /// <summary>A body is saved from the commit with the repository's secret in a header file, and both names are read back.</summary>
    [Fact]
    public void A_blog_body_is_saved_from_the_commit_and_read_back_on_both_names()
    {
        string blog = Workflow("blog-post.yml");
        Assert.Contains("data/blog/$SLUG.md", blog, StringComparison.Ordinal);
        Assert.Contains("ADMIN_KEY: ${{ secrets.ADMIN_KEY }}", blog, StringComparison.Ordinal);
        Assert.Contains("-H @\"$RUNNER_TEMP/key.header\"", blog, StringComparison.Ordinal);
        Assert.DoesNotContain("echo \"$ADMIN_KEY", blog, StringComparison.Ordinal);
        Assert.Contains("for site in \"$SITE\" \"$COSMOS_SITE\"; do", blog, StringComparison.Ordinal);
        Assert.Contains("workflow_dispatch:", blog, StringComparison.Ordinal);
        foreach (string slug in new[] { "the-spec-has-to-run", "solid-in-five-real-files" })
        {
            Assert.True(File.Exists(Path.Combine(Repo.Root(), "data", "blog", $"{slug}.md")), $"data/blog/{slug}.md");
        }
    }
    // #endregion the-blog

    // #region the-read
    /// <summary>An Azure read only reads: every metric with a start and an end, and nothing that writes.</summary>
    [Fact]
    public void An_azure_read_only_reads_and_bounds_every_metric()
    {
        string read = Workflow("azure-read.yml");
        Assert.Contains("--start-time \"$5\" --end-time \"$6\"", read, StringComparison.Ordinal);
        Assert.Contains("--top 100", read, StringComparison.Ordinal);
        foreach (string write in new[] { " create", " update", " delete", " set ", "deployment" })
        {
            Assert.DoesNotContain($"az{write}", read, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("appsettings", read, StringComparison.Ordinal);
    }
    // #endregion the-read
}
