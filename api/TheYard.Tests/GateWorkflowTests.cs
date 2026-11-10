using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The ship's gate on a GitHub runner, <c>.github/workflows/gate.yml</c>, held to the gate it
/// replaced (ADR: The gate runs where the memory is).
///
/// <para>The gate moved off the laptop because the laptop ran out of memory, not because any check
/// stopped mattering. The danger in a move like that is a check that quietly does not make the
/// trip: a workflow that is green because it no longer runs the browser suite looks exactly like
/// one that is green because the browser suite passed. So every check the laptop's gate ran is
/// named here with the command that runs it, every report the results writer reads is one the
/// workflow writes, and every check feeds the verdict that turns the run red.</para>
/// </summary>
public class GateWorkflowTests
{
    private static string Workflow() =>
        File.ReadAllText(Path.Combine(Repo.Root(), ".github", "workflows", "gate.yml")).ReplaceLineEndings("\n");

    /// <summary>One job of gate.yml, from its key to the next job's key.</summary>
    private static string Job(string key)
    {
        string workflow = Workflow();
        int start = workflow.IndexOf($"\n  {key}:\n", StringComparison.Ordinal);
        Assert.True(start > 0, $"gate.yml should hold the {key} job");
        var next = Regex.Match(workflow[(start + 1)..], @"\n  [a-z-]+:\n");
        return next.Success ? workflow[start..(start + 1 + next.Index)] : workflow[start..];
    }

    /// <summary>The gate job: the fold and the verdict, from its key to The Shed's job, which runs on Windows beside it.</summary>
    private static string GateJob()
    {
        string workflow = Workflow();
        int start = workflow.IndexOf("\n  gate:\n", StringComparison.Ordinal);
        int end = workflow.IndexOf("\n  shed:\n", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "gate.yml should hold the gate job and then The Shed's");
        return workflow[start..end];
    }

    /// <summary>The check jobs, which run side by side: from the first job's key to the gate job's.</summary>
    private static string CheckJobs()
    {
        string workflow = Workflow();
        int start = workflow.IndexOf("\njobs:\n", StringComparison.Ordinal);
        int end = workflow.IndexOf("\n  gate:\n", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "gate.yml should hold the check jobs and then the gate job");
        return workflow[start..end];
    }

    /// <summary>The check jobs the gate job folds, as each one names its artifact, and as the fold and promote.sh name them.</summary>
    public static TheoryData<string, string> CheckJobNames => new()
    {
        { "dotnet", "raw-dotnet-${{ github.run_attempt }}" },
        { "cosmos", "raw-cosmos-${{ github.run_attempt }}" },
        { "node", "raw-node-${{ github.run_attempt }}" },
        { "browser", "raw-browser-${{ matrix.shard }}-${{ github.run_attempt }}" },
    };

    /// <summary>The Shed's job, to CodeRabbit's job after it.</summary>
    private static string ShedJob()
    {
        string workflow = Workflow();
        int start = workflow.IndexOf("\n  shed:\n", StringComparison.Ordinal);
        int end = workflow.IndexOf("\n  # #region coderabbit-job\n", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "gate.yml should hold The Shed's job and then CodeRabbit's");
        return workflow[start..end];
    }

    // #region every-check
    /// <summary>
    /// Each check the laptop's gate ran, by the command that runs it. Every one of these is a check
    /// a version has failed on at least once; none is decoration.
    /// </summary>
    public static TheoryData<string, string> Checks => new()
    {
        { "the build, every project", "dotnet build api/TheYard.slnx -c Debug" },
        { "the bundle", "npx vite build" },
        { "the rendering service's bundle", "npx vite build --config render/vite.config.ts" },
        { "dotnet format", "dotnet format api/TheYard.slnx --verify-no-changes" },
        { "the SQL project", "dotnet build api/TheYard.Database/TheYard.Database.sqlproj" },
        { "xUnit on SQLite", "--filter \"Store!=cosmos\" --logger \"trx;LogFileName=xunit-sqlite.trx\"" },
        { "the live Cosmos DB tests", "--filter \"Store=cosmos\" --logger \"trx;LogFileName=xunit-live.trx\"" },
        { "xUnit booted on Cosmos DB", "--logger \"trx;LogFileName=xunit-cosmos.trx\"" },
        { "Prettier", "npm run format:check" },
        { "oxlint", "npm run lint" },
        { "the type check", "npx tsc -b" },
        { "Vitest", "npx vitest run" },
        { "the browser suite on SQLite", "npx playwright test --shard=\"$SHARD/2\" --workers=2 --retries=1" },
        { "the store specs on Cosmos DB", "tests/e2e/store-toggle.spec.ts tests/e2e/account.spec.ts tests/e2e/admin.spec.ts" },
        { "the em dash scan", "grep -rlP '\\x{2014}'" },
    };

    [Theory]
    [MemberData(nameof(Checks))]
    public void The_workflow_runs_every_check_the_laptop_gate_ran(string check, string command)
    {
        Assert.True(Workflow().Contains(command, StringComparison.Ordinal), $"gate.yml should run {check}: {command}");
    }
    // #endregion every-check

    // #region every-report
    /// <summary>
    /// Every file <c>scripts/test_results.mjs</c> reads from the gate's raw folder, read out of the
    /// script itself, is one the workflow writes. A report the writer looks for and the workflow
    /// never wrote turns into a suite with no tests in the results file, which nobody would notice.
    /// </summary>
    [Fact]
    public void Every_report_the_results_writer_reads_is_one_the_workflow_writes()
    {
        string writer = File.ReadAllText(Path.Combine(Repo.Root(), "scripts", "test_results.mjs"));
        var reads = Regex.Matches(writer, @"(?:results|readJson|trx)\('([^']+)'\)")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.True(reads.Count >= 8, $"the writer should read at least eight reports; read {reads.Count}");

        string workflow = Workflow();
        foreach (string report in reads)
        {
            string name = report.Split('/').Last();
            Assert.True(workflow.Contains(name, StringComparison.Ordinal), $"gate.yml should write {report}, which test_results.mjs reads");
        }
    }
    // #endregion every-report

    // #region the-verdict
    /// <summary>Every result key the workflow writes is one its verdict reads, so no check can run and not count.</summary>
    [Fact]
    public void Every_check_the_workflow_records_turns_the_run_red_when_it_fails()
    {
        string gate = GateJob();
        int verdict = gate.IndexOf("- name: The verdict", StringComparison.Ordinal);
        Assert.True(verdict > 0, "the gate job should end with the verdict");
        string judged = gate[verdict..];

        // The code's own numbers (the code-numbers region) are measurements for the tests card,
        // not checks: the build's warnings are the one of them that turns a run red, and the verdict
        // reads that. Only that region is set aside; a check written anywhere else still counts.
        // Since 1.0.5.4 the checks record their results in the check jobs, which the gate job folds.
        string before = CheckJobs() + gate[..verdict];
        int numbersStart = before.IndexOf("# #region code-numbers", StringComparison.Ordinal);
        int numbersEnd = before.IndexOf("# #endregion code-numbers", StringComparison.Ordinal);
        Assert.True(numbersStart > 0 && numbersEnd > numbersStart, "gate.yml should fence the code's own numbers in the code-numbers region");
        string recorded = before[..numbersStart] + before[numbersEnd..];
        var keys = Regex.Matches(recorded, @"echo ""([a-z_]+)=")
            .Select(match => match.Groups[1].Value)
            .Where(key => !key.EndsWith("_s", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.Contains("browser", keys);
        Assert.Contains("oncosmos", keys);
        // The fold's own key, written by scripts/gate_fold.py: a job that left no reports is red by name.
        Assert.Contains("[ \"$(val rest.result fold)\" = 0 ] || red=\"$red fold\"", judged, StringComparison.Ordinal);
        foreach (string key in keys)
        {
            string spelled = key.Replace('_', '-');
            Assert.True(
                Regex.IsMatch(judged, $@"\b{key}\b") || judged.Contains(spelled, StringComparison.Ordinal),
                $"the verdict should read {key}");
        }
        Assert.Contains("warnings", judged, StringComparison.Ordinal);
        Assert.Contains("exit 1", judged, StringComparison.Ordinal);
    }
    // #endregion the-verdict

    // #region the-shed
    /// <summary>
    /// The Shed's two suites run on Windows, as they did on the laptop, and either one failing fails
    /// the run: its path rules are written for the system they run on.
    /// </summary>
    [Fact]
    public void The_shed_runs_both_suites_on_windows_and_fails_the_run_when_either_is_red()
    {
        string shed = ShedJob();
        Assert.Contains("runs-on: windows-latest", shed, StringComparison.Ordinal);
        Assert.Contains("dotnet test TestProject.sln", shed, StringComparison.Ordinal);
        Assert.Contains("node --test tests/js/*.test.js", shed, StringComparison.Ordinal);
        Assert.Contains("exit 1", shed, StringComparison.Ordinal);
    }
    // #endregion the-shed

    // #region the-boundary
    /// <summary>
    /// The workflow runs on a gate branch and nowhere else, and its checks cannot push: the gate job and
    /// The Shed's read the code and nothing more, so a red run leaves main as it was. Only the promote job,
    /// after both are green, may write (PromoteWorkflowTests; ADR-098, the addendum of 1.0.4.101).
    /// </summary>
    [Fact]
    public void The_gate_runs_on_gate_branches_only_and_its_checks_cannot_push()
    {
        string workflow = Workflow();
        Assert.Contains("on:\n  push:\n    branches: [\"gate/**\"]\n", workflow, StringComparison.Ordinal);
        Assert.Contains("permissions:\n  contents: read\n", workflow, StringComparison.Ordinal);
        foreach (string job in new[] { CheckJobs(), GateJob(), ShedJob() })
        {
            Assert.DoesNotContain("contents: write", job, StringComparison.Ordinal);
            Assert.DoesNotContain("git push", job, StringComparison.Ordinal);
        }
    }
    // #endregion the-boundary

    // #region side-by-side
    /// <summary>
    /// The checks run side by side (ADR-098, the addendum of 1.0.5.4): four kinds of check job start together, each
    /// uploads its reports under its own name and attempt, and the gate job waits for all of them, whatever they did,
    /// folds every one and judges the fold. A job the fold does not name would run and never count.
    /// </summary>
    [Theory]
    [MemberData(nameof(CheckJobNames))]
    public void Every_check_job_leaves_its_reports_for_the_gate_job_to_fold(string job, string artifact)
    {
        Assert.Contains($"name: {artifact}", Job(job), StringComparison.Ordinal);
        string gate = GateJob();
        Assert.Contains("needs: [dotnet, cosmos, node, browser]", gate, StringComparison.Ordinal);
        Assert.Contains("if: always()", gate[..gate.IndexOf("steps:", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("python3 scripts/gate_fold.py \"$RUNNER_TEMP/jobs\" \"$RUNNER_TEMP/raw\" \"$GITHUB_RUN_ATTEMPT\" dotnet cosmos node browser-1 browser-2", gate, StringComparison.Ordinal);
        Assert.Contains("pattern: raw-*", gate, StringComparison.Ordinal);
    }

    /// <summary>The browser suite on SQLite runs in two shards, and promote.sh trusts both by the names the matrix gives them.</summary>
    [Fact]
    public void The_browser_suite_runs_in_two_shards_and_promote_trusts_every_check_job_by_name()
    {
        string browser = Job("browser");
        Assert.Contains("name: browser ${{ matrix.shard }} of 2", browser, StringComparison.Ordinal);
        Assert.Contains("shard: [1, 2]", browser, StringComparison.Ordinal);
        Assert.Contains("fail-fast: false", browser, StringComparison.Ordinal);
        string promote = File.ReadAllText(Path.Combine(Repo.Root(), "scripts", "promote.sh"));
        Assert.Contains("checks='[\"gate\",\"shed\",\"dotnet\",\"cosmos\",\"node\",\"browser 1 of 2\",\"browser 2 of 2\"]'", promote, StringComparison.Ordinal);
        Assert.Contains("[ \"$green\" = 7 ]", promote, StringComparison.Ordinal);
    }

    /// <summary>
    /// Everything that reads the Cosmos DB account stays in one job, one pass after another, because the passes share
    /// the tests- containers on it; and that job is the only one that signs in to Azure, so a sign-in that times out
    /// reddens its passes and nothing else.
    /// </summary>
    [Fact]
    public void Every_pass_on_the_cosmos_db_account_runs_in_the_one_job_that_signs_in()
    {
        string cosmos = Job("cosmos");
        foreach (string pass in new[] { "trx;LogFileName=xunit-live.trx", "trx;LogFileName=xunit-cosmos.trx", "tests/e2e/store-toggle.spec.ts tests/e2e/account.spec.ts tests/e2e/admin.spec.ts", "Cosmos__ContainerPrefix=\"tests-\"" })
        {
            Assert.Contains(pass, cosmos, StringComparison.Ordinal);
        }
        Assert.Contains("environment: gate", cosmos, StringComparison.Ordinal);
        Assert.Contains("uses: azure/login@", cosmos, StringComparison.Ordinal);
        string others = CheckJobs().Replace(cosmos, "", StringComparison.Ordinal) + GateJob() + ShedJob();
        foreach (string signIn in new[] { "azure/login@", "environment: gate", "Cosmos__AccountEndpoint", "xunit-cosmos.trx", "xunit-live.trx" })
        {
            Assert.DoesNotContain(signIn, others, StringComparison.Ordinal);
        }
    }
    /// <summary>
    /// Each check job ends with its own verdict over exactly the keys it records, so it fails by name when one is red or
    /// was never written, and "re-run failed jobs" runs it again (the gate job's fold then takes the new attempt).
    /// </summary>
    [Theory]
    [InlineData("dotnet")]
    [InlineData("cosmos")]
    [InlineData("node")]
    [InlineData("browser")]
    public void Every_check_job_judges_exactly_the_keys_it_records(string job)
    {
        string text = Job(job);
        int numbersStart = text.IndexOf("# #region code-numbers", StringComparison.Ordinal);
        int numbersEnd = text.IndexOf("# #endregion code-numbers", StringComparison.Ordinal);
        string recorded = numbersStart > 0 ? text[..numbersStart] + text[numbersEnd..] : text;
        var keys = Regex.Matches(recorded, @"echo ""([a-z_]+)=")
            .Select(match => match.Groups[1].Value)
            .Where(key => !key.EndsWith("_s", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        var judged = Regex.Match(text, @"run: bash scripts/gate_job\.sh ""\$RAW"" ([a-z_ ]+)\n");
        Assert.True(judged.Success, $"the {job} job should end with scripts/gate_job.sh over its keys");
        Assert.Equal(keys.Order(StringComparer.Ordinal), judged.Groups[1].Value.Split(' ').Order(StringComparer.Ordinal));
        Assert.Contains("GATE_NEEDS: ${{ toJSON(needs) }}", GateJob(), StringComparison.Ordinal);
    }
    // #endregion side-by-side
}
