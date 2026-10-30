namespace TheYard.Tests;

/// <summary>
/// The gate promotes its own green run (ADR: The gate runs where the memory is, the addendum of 1.0.4.101):
/// the promote job in <c>.github/workflows/gate.yml</c>, the script it runs, <c>scripts/promote.sh</c>, and the
/// two deploys it calls, held to the step the laptop ran before them.
///
/// <para>The step moved off the laptop so that a version ships with no machine on. The danger in that move is
/// the same as the gate's: a part of the step that quietly does not make the trip. So each part the laptop's
/// ship script did after a green run is named here by the line that does it now, main moves only forward and
/// only after both check jobs are green, and the two deploys still roll on a push as well as on the call.</para>
/// </summary>
public class PromoteWorkflowTests
{
    private static string Read(params string[] path) =>
        File.ReadAllText(Path.Combine([Repo.Root(), .. path])).ReplaceLineEndings("\n");

    private static string Gate() => Read(".github", "workflows", "gate.yml");

    private static string Promote() => Read("scripts", "promote.sh");

    /// <summary>One job of gate.yml, from its key to the next job's key or the end.</summary>
    private static string Job(string key)
    {
        string gate = Gate();
        int start = gate.IndexOf($"\n  {key}:\n", StringComparison.Ordinal);
        Assert.True(start > 0, $"gate.yml should hold the {key} job");
        var next = System.Text.RegularExpressions.Regex.Match(gate[(start + 1)..], @"\n  [a-z-]+:\n");
        return next.Success ? gate[start..(start + 1 + next.Index)] : gate[start..];
    }

    // #region the-order
    /// <summary>Promote waits for both check jobs; the deploys wait for promote; only promote and the branch's deletion write.</summary>
    [Fact]
    public void Promote_runs_only_after_both_checks_are_green_and_is_the_one_job_that_moves_main()
    {
        string promote = Job("promote");
        Assert.Contains("needs: [gate, shed]", promote, StringComparison.Ordinal);
        Assert.Contains("contents: write", promote, StringComparison.Ordinal);
        Assert.Contains("bash scripts/promote.sh", promote, StringComparison.Ordinal);
        Assert.Contains("needs: promote", Job("deploy"), StringComparison.Ordinal);
        Assert.Contains("needs: promote", Job("deploy-render"), StringComparison.Ordinal);
        Assert.Contains("needs: [promote, deploy, deploy-render]", Job("after-roll"), StringComparison.Ordinal);
        Assert.Equal(2, Gate().Split("contents: write").Length - 1);
        Assert.DoesNotContain("git push", Job("after-roll"), StringComparison.Ordinal);
        Assert.DoesNotContain("\n    if:", promote, StringComparison.Ordinal);
        Assert.Contains("\"$GITHUB_RUN_ID\"", promote, StringComparison.Ordinal);
    }
    // #endregion the-order

    // #region only-forward
    /// <summary>Main moves only forward: an ancestor check before every push, and never a force.</summary>
    [Fact]
    public void Main_is_only_fast_forwarded_and_never_forced()
    {
        string script = Promote();
        Assert.Contains("git merge-base --is-ancestor origin/main HEAD", script, StringComparison.Ordinal);
        Assert.Contains("git push -q origin \"HEAD:refs/heads/main\"", script, StringComparison.Ordinal);
        foreach (string text in new[] { script, Gate() })
        {
            Assert.DoesNotContain("--force", text, StringComparison.Ordinal);
            Assert.DoesNotContain("push -f", text, StringComparison.Ordinal);
            Assert.DoesNotContain("+HEAD", text, StringComparison.Ordinal);
        }
    }
    // #endregion only-forward

    // #region nothing-dropped
    /// <summary>Each part of the laptop's step after a green run, by the line that does it now.</summary>
    public static TheoryData<string, string, string> Parts => new()
    {
        { "the run's reports", "gate.yml", "name: gate-raw" },
        { "the version the branch names is the changelog's", "promote.sh", "the branch names $version and the changelog's top line is $top" },
        { "the first three numbers are Steve's", "promote.sh", "the first three numbers are Steve's" },
        { "no live card says a role is missing", "promote.sh", "(no role on|not read: no role|holds no role)" },
        { "the served resume is the locked one", "promote.sh", "api/TheYard.Api/wwwroot/docs/resume.pdf\" | sha256sum -c" },
        { "the Cosmos DB passes carried forward", "promote.sh", "YARD_CARRY_COSMOS" },
        { "the results file", "promote.sh", "node scripts/test_results.mjs \"$raw\" \"$version\" \"$seconds\"" },
        { "the results describe this run and not another attempt", "promote.sh", "the results file records a suite this green gate did not have" },
        { "the README's figures", "promote.sh", "python3 scripts/readme_results.py" },
        { "the results touch three files only", "promote.sh", "the results touched more than their three files" },
        { "the results folded into the gate's commit", "promote.sh", "git commit -q --amend --no-edit" },
        { "four tries at main", "promote.sh", "for try in 1 2 3 4" },
        { "the edge moved after the deploy", "gate.yml", "gh workflow run edge-branch.yml --ref main" },
        { "the roll read on both names", "gate.yml", "python3 scripts/admin_after_roll.py" },
        { "CI on main read to its end", "gate.yml", "gh run watch \"$run\" --exit-status" },
        { "the gate branch deleted", "gate.yml", "git/refs/heads/$GITHUB_REF_NAME" },
    };

    [Theory]
    [MemberData(nameof(Parts))]
    public void Nothing_the_laptop_did_after_a_green_run_is_dropped(string part, string file, string line)
    {
        string text = file == "gate.yml" ? Gate() : Promote();
        Assert.True(text.Contains(line, StringComparison.Ordinal), $"{file} should do {part}: {line}");
    }
    // #endregion nothing-dropped

    // #region the-roll
    /// <summary>The run calls both deploys on the promoted commit; each still rolls on a push of its own and keeps its group.</summary>
    [Theory]
    [InlineData("deploy.yml", "deploy", "group: deploy-production")]
    [InlineData("deploy-render.yml", "deploy-render", "group: deploy-render")]
    public void Each_deploy_rolls_on_the_gate_s_call_and_still_on_a_push(string file, string job, string group)
    {
        string called = Job(job);
        Assert.Contains($"uses: ./.github/workflows/{file}", called, StringComparison.Ordinal);
        Assert.Contains("sha: ${{ needs.promote.outputs.sha }}", called, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets: inherit", called, StringComparison.Ordinal);
        string deploy = Read(".github", "workflows", file);
        Assert.Contains("[ \"$(git rev-parse origin/main)\" = \"$SHA\" ] || { echo \"::error::$SHA is no longer main's head\"; exit 1; }", deploy, StringComparison.Ordinal);
        Assert.Contains("on:\n  push:\n    branches: [main]\n", deploy, StringComparison.Ordinal);
        Assert.Contains("  workflow_call:\n", deploy, StringComparison.Ordinal);
        Assert.Contains(group, deploy, StringComparison.Ordinal);
        Assert.Contains("environment: gate", deploy, StringComparison.Ordinal);
        Assert.DoesNotContain("${{ github.sha }}", deploy, StringComparison.Ordinal);
    }

    /// <summary>Deploy is handed the three secrets it reads, by name; Deploy Render, which reads none, is handed nothing.</summary>
    [Fact]
    public void Each_deploy_is_handed_only_the_secrets_it_reads()
    {
        string called = Job("deploy");
        string[] names = ["YARD_AUTH_SIGNING_KEY", "ADMIN_KEY", "NETLIFY_TOKEN"];
        foreach (string name in names)
        {
            Assert.Contains($"      {name}: ${{{{ secrets.{name} }}}}", called, StringComparison.Ordinal);
            Assert.Contains($"      {name}:\n        required: true", Read(".github", "workflows", "deploy.yml"), StringComparison.Ordinal);
        }
        Assert.Equal(names.Length, System.Text.RegularExpressions.Regex.Matches(called, @"secrets\.").Count);
        Assert.DoesNotContain("secrets", Job("deploy-render"), StringComparison.Ordinal);
    }
    // #endregion the-roll

    // #region the-token
    /// <summary>
    /// A version that changes a workflow takes one of two paths and fails on neither (ADR-098, the addendum of
    /// 1.0.4.119). The secret PROMOTE_TOKEN carries Contents and Workflows write, so checkout takes it where it
    /// exists and the push promote.sh makes carries the workflow; where it does not exist the job ends green with
    /// a notice and moves nothing. Seven of the eleven red Gate runs of 26 to 29 October were the old refusal,
    /// each on a gate that had already passed, and each one sent the owner a failure email for green code.
    /// </summary>
    [Fact]
    public void A_version_that_changes_a_workflow_is_carried_by_the_secret_or_ends_green_with_a_notice()
    {
        string promote = Job("promote");
        Assert.Contains("token: ${{ secrets.PROMOTE_TOKEN || github.token }}", promote, StringComparison.Ordinal);
        Assert.Contains("HAS_PROMOTE_TOKEN: ${{ secrets.PROMOTE_TOKEN != '' }}", promote, StringComparison.Ordinal);
        Assert.Contains("promoted: ${{ steps.promote.outputs.promoted }}", promote, StringComparison.Ordinal);
        Assert.Contains("by_push: ${{ steps.promote.outputs.by_push }}", promote, StringComparison.Ordinal);

        string script = Promote();
        Assert.Contains("::notice::this version changes a workflow: promote it by hand with scripts/promote.sh", script, StringComparison.Ordinal);
        // Green, not red: the notice path writes its outputs and leaves, and nothing in the script refuses for a
        // workflow any more.
        Assert.Contains("echo \"promoted=false\"", script, StringComparison.Ordinal);
        Assert.DoesNotContain("refuse \"this version changes a workflow", script, StringComparison.Ordinal);
        Assert.Contains("[ \"${HAS_PROMOTE_TOKEN:-}\" = true ]", script, StringComparison.Ordinal);
    }

    /// <summary>
    /// The secret's VALUE is never read by the script, printed, or put on a command line: checkout holds it, and
    /// all the script is told is whether it exists. The only two mentions of the secret in the workflow are the
    /// checkout's token and that one test.
    /// </summary>
    [Fact]
    public void The_promote_secret_is_never_read_printed_or_put_on_a_command_line()
    {
        string script = Promote();
        foreach (string reading in new[] { "$PROMOTE_TOKEN", "${PROMOTE_TOKEN", "secrets.PROMOTE_TOKEN" })
        {
            Assert.DoesNotContain(reading, script, StringComparison.Ordinal);
        }
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(Gate(), @"secrets\.PROMOTE_TOKEN").Count);
        foreach (string line in Gate().Split('\n').Where(line => line.Contains("PROMOTE_TOKEN", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("echo", line, StringComparison.Ordinal);
            Assert.DoesNotContain("run:", line, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A promote that pushed with a personal token started Deploy, Deploy Render and CI on that push, which the
    /// run's own token never does. So the two deploy jobs skip on it, the edge needs no start, and after-roll
    /// waits for the push's two runs and reads the push's CI run rather than rolling the site a second time in
    /// the same concurrency group.
    /// </summary>
    [Fact]
    public void A_promote_that_pushed_with_a_personal_token_lets_the_push_be_the_roll()
    {
        foreach (string job in new[] { Job("deploy"), Job("deploy-render") })
        {
            Assert.Contains("if: needs.promote.outputs.promoted == 'true' && needs.promote.outputs.by_push != 'true'", job, StringComparison.Ordinal);
        }
        string after = Job("after-roll");
        Assert.Contains("needs.promote.outputs.promoted == 'true'", after, StringComparison.Ordinal);
        Assert.Contains("- name: The roll the push started", after, StringComparison.Ordinal);
        Assert.Contains("--event push", after, StringComparison.Ordinal);
        Assert.Contains("if: needs.promote.outputs.by_push != 'true'", after, StringComparison.Ordinal);
        Assert.Contains("BY_PUSH: ${{ needs.promote.outputs.by_push }}", after, StringComparison.Ordinal);
        // The Function's deploy has no push trigger, so its dispatch is owed on both paths alike.
        Assert.DoesNotContain("by_push", after[after.IndexOf("- name: The Function, by a run of its own", StringComparison.Ordinal)..after.IndexOf("- name: The reads after the roll", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.DoesNotContain("on:\n  push:", Read(".github", "workflows", "deploy-function.yml"), StringComparison.Ordinal);
    }
    // #endregion the-token

    // #region the-preflight
    /// <summary>
    /// The checks a shipping machine runs before it pushes a gate branch (ADR-098, the addendum of 1.0.4.119):
    /// the four the gate runs that fit in 8 GB and three minutes, and a line saying which promote path this
    /// version is on. Each command is the gate's own, so a preflight that passes and a gate that fails on the
    /// same check cannot happen. It never runs the browser suite or the passes on Cosmos DB, which is why the
    /// gate moved to a 16 GB runner, and it reads the secret by NAME only.
    /// </summary>
    [Fact]
    public void The_preflight_runs_the_gate_s_own_quick_checks_and_names_the_promote_path()
    {
        string preflight = Read("scripts", "preflight.sh");
        string gate = Gate();
        foreach (string command in new[] { "dotnet format api/TheYard.slnx --verify-no-changes", "npm run format:check", "npm run lint", "npx tsc -b" })
        {
            Assert.Contains(command, preflight, StringComparison.Ordinal);
            Assert.True(gate.Contains(command, StringComparison.Ordinal), $"the gate should run what the preflight runs: {command}");
        }
        Assert.Contains("a red preflight is never pushed", preflight, StringComparison.Ordinal);
        Assert.Contains("exit 1", preflight, StringComparison.Ordinal);
        Assert.Contains("gh secret list -R SteveStout/TheYard", preflight, StringComparison.Ordinal);
        Assert.Contains("UNKNOWN", preflight, StringComparison.Ordinal);
        // The two it must not run, because they need the runner's memory.
        foreach (string heavy in new[] { "playwright", "Store=cosmos", "Cosmos__AccountEndpoint" })
        {
            Assert.DoesNotContain(heavy, preflight, StringComparison.Ordinal);
        }
        // The rule it enforces is written where whoever ships reads it.
        foreach (string file in new[] { "CLAUDE.md", Path.Combine("docs", "ci-cd", "CICD.md") })
        {
            Assert.Contains("scripts/preflight.sh", File.ReadAllText(Path.Combine(Repo.Root(), file)), StringComparison.Ordinal);
        }
    }
    // #endregion the-preflight

    // #region no-machine
    /// <summary>The scripts that came from the job hunt share name no machine's drive.</summary>
    [Theory]
    [InlineData("promote.sh")]
    [InlineData("preflight.sh")]
    [InlineData("readme_results.py")]
    [InlineData("admin_after_roll.py")]
    public void The_promote_scripts_name_no_machine(string script)
    {
        string text = Read("scripts", script);
        // Built from parts, so this file passes the same scan it holds the scripts to.
        foreach (string drive in new[] { "C:" + "\\", "J:" + "\\", "LOCAL" + "APPDATA" })
        {
            Assert.DoesNotContain(drive, text, StringComparison.Ordinal);
        }
    }
    // #endregion no-machine
}
