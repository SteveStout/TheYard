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

    /// <summary>The gate job: from its key to The Shed's job, which runs on Windows beside it.</summary>
    private static string GateJob()
    {
        string workflow = Workflow();
        int start = workflow.IndexOf("\n  gate:\n", StringComparison.Ordinal);
        int end = workflow.IndexOf("\n  shed:\n", StringComparison.Ordinal);
        Assert.True(start > 0 && end > start, "gate.yml should hold the gate job and then The Shed's");
        return workflow[start..end];
    }

    /// <summary>The Shed's job, to the end of the file.</summary>
    private static string ShedJob()
    {
        string workflow = Workflow();
        int start = workflow.IndexOf("\n  shed:\n", StringComparison.Ordinal);
        Assert.True(start > 0, "gate.yml should hold The Shed's job");
        return workflow[start..];
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
        { "the browser suite on SQLite", "npx playwright test --workers=2 --retries=1" },
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
        string workflow = GateJob();
        int verdict = workflow.IndexOf("- name: The verdict", StringComparison.Ordinal);
        Assert.True(verdict > 0, "the gate job should end with the verdict");
        string judged = workflow[verdict..];

        var keys = Regex.Matches(workflow[..verdict], @"echo ""([a-z_]+)=")
            .Select(match => match.Groups[1].Value)
            .Where(key => !key.EndsWith("_s", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        Assert.Contains("browser", keys);
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
    /// The workflow runs on a gate branch and nowhere else, and it cannot push: main moves only when
    /// the laptop reads a green run and fast-forwards it, so a red run leaves main as it was.
    /// </summary>
    [Fact]
    public void The_gate_runs_on_gate_branches_only_and_cannot_push()
    {
        string workflow = Workflow();
        Assert.Contains("on:\n  push:\n    branches: [\"gate/**\"]\n", workflow, StringComparison.Ordinal);
        Assert.Contains("permissions:\n  contents: read\n", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("contents: write", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("git push", workflow, StringComparison.Ordinal);
    }
    // #endregion the-boundary
}
