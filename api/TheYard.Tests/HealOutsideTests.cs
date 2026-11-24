using System.Diagnostics;
using System.Text.Json;

namespace TheYard.Tests;

/// <summary>
/// The outside self-heal list, held in the gate (the year-end plan's 7b; selfheal1120 version 5): a heal branch that
/// touches a path in data/heal-outside.json, or deletes a file, goes red; any other branch passes; and both the gate's
/// node job and preflight's catch list run the one script. The gate judges the heal's own ready commit, never the cop's
/// fill of it, so the ratchet the fill raises for a heal's new test is not the heal's change. The changes are a fixture
/// file, or a fake git on the PATH that answers from fixture files, in a folder born and thrown away with the test,
/// never a live branch (RULE 79).
/// </summary>
public sealed class HealOutsideTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "heal-outside-" + Guid.NewGuid().ToString("N"));

    public HealOutsideTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void A_heal_branch_that_touches_a_ceiling_an_infra_size_or_deletes_a_test_is_red_and_names_each()
    {
        if (NoPython())
        {
            return;
        }
        var (code, output) = Run("ready/heal-w12-merger-20261120-abcd1234",
            "M\tfunctions/JobHunt.Domain/Features/Watch/WatchJudge.cs\nM\tserver/JobHunt.Server.Domain/Shared/Gates/Ceilings.cs\nD\ttests/e2e/cloud.spec.ts\nR100\tsrc/a.ts\tinfra/jobhunt.bicep\nM\tfunctions/JobHunt.Application/Features/Campaign/Templates/first.txt\n");
        Assert.Equal(1, code);
        Assert.Contains("Ceilings.cs: on the outside list", output, StringComparison.Ordinal);
        Assert.Contains("tests/e2e/cloud.spec.ts: deleted (f:", output, StringComparison.Ordinal);
        Assert.Contains("infra/jobhunt.bicep: on the outside list (infra/*.bicep)", output, StringComparison.Ordinal);
        Assert.Contains("Templates/first.txt: on the outside list", output, StringComparison.Ordinal);
        Assert.DoesNotContain("WatchJudge.cs", output, StringComparison.Ordinal);
        Assert.Contains("4 paths outside self-heal", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_heal_that_stays_inside_passes_and_a_lane_that_is_not_a_heal_is_never_held()
    {
        if (NoPython())
        {
            return;
        }
        const string Inside = "M\tfunctions/JobHunt.Domain/Features/Watch/WatchJudge.cs\nA\tfunctions/jobhunt.Tests/HealFixtureTests.cs\n";
        Assert.Equal(0, Run("ready/heal-w12-merger-20261120-abcd1234", Inside).Code);
        Assert.Equal(0, Run("ready/selfheal1120-abcd1234", "M\tserver/JobHunt.Server.Domain/Shared/Gates/Ceilings.cs\n").Code);
        Assert.Equal(1, Run(string.Empty, "M\t.github/quality-ratchet.json\n", lane: "heal-w12-merger-20261120").Code);
    }

    [Fact]
    public void The_list_names_the_paths_the_plan_protects_and_both_the_gate_and_the_catch_list_run_it()
    {
        using var list = JsonDocument.Parse(File.ReadAllText(Repo.DataFile("heal-outside.json")));
        string[] paths = [.. list.RootElement.GetProperty("paths").EnumerateArray().Select(p => p.GetString()!)];
        foreach (string protectedPath in new[] { "server/JobHunt.Server.Domain/Shared/Gates/Ceilings.cs", "functions/JobHunt.Domain/Shared/Pay/PayFloor.cs",
            "functions/JobHunt.Application/Shared/Runs/RunFloors.cs", "server/JobHunt.Server.Domain/Shared/Worklists/TextCheck.cs", ".github/quality-ratchet.json",
            ".github/byte-budgets.json", "infra/*.bicep", "data/heal-outside.json", "scripts/heal_outside.py" })
        {
            Assert.Contains(protectedPath, paths);
        }
        foreach (string path in paths.Where(p => !p.Contains('*', StringComparison.Ordinal)))
        {
            Assert.True(File.Exists(Path.Combine(Repo.Root(), path)), $"heal-outside.json names {path}, which is not in the repository");
        }

        string workflow = File.ReadAllText(Path.Combine(Repo.Root(), ".github", "workflows", "gate.yml")).ReplaceLineEndings("\n");
        Assert.Contains("python3 scripts/heal_outside.py --branch \"$GITHUB_REF_NAME\" --base \"${BASE:-origin/main}\" --head \"${READY:-HEAD}\" > \"$RAW/heal-outside.txt\" 2>&1 || code=$?\n          echo \"healoutside=$code\" >> \"$r\"", workflow, StringComparison.Ordinal);
        Assert.Contains("gate_job.sh \"$RAW\" prettier lint tsc vitest emdash quality healoutside", workflow, StringComparison.Ordinal);
        Assert.Contains("catch heal-outside python3 scripts/heal_outside.py --base \"$base\"", File.ReadAllText(Path.Combine(Repo.Root(), "scripts", "catch_list.sh")), StringComparison.Ordinal);
    }

    // The heal-server6am-apply-20261123 heal (ready 0538af53, filled 4430e05b as 1.0.5.151): its own commit and the fill.
    private const string Heal = "ready/heal-server6am-apply-20261123-0538af53";
    private const string ItsOwn = "M\tdocs/CHANGELOG.md\nM\tserver/JobHunt.Server.Application/Features/Run/ApplyPostings.cs\nM\tserver/JobHunt.Server.Domain/Shared/Worklists/ApplySkips.cs\nM\tserver/JobHunt.Server.Tests/ApplySkipsTests.cs\n";
    private const string TheFill = "M\t.github/quality-ratchet.json\nM\tREADME.md\n";

    [Fact]
    public void A_heal_is_judged_by_its_ready_commit_so_the_ratchet_the_fill_raises_for_its_test_passes()
    {
        // The fake git is a bash script, so these run where the gate does, never on The Shed's Windows runner.
        if (NoPython() || OperatingSystem.IsWindows())
        {
            return;
        }
        Fake(("ready0538", ItsOwn), ("filled4430", ItsOwn + TheFill));

        var (code, output) = RunAt(Heal, "ready0538");
        Assert.True(code == 0, output);
        Assert.Contains("a heal branch, no path on the outside list", output, StringComparison.Ordinal);
        Assert.Equal(["merge-base main7caa ready0538", "diff --name-status start-of-ready0538 ready0538"], File.ReadAllLines(Path.Combine(_root, "git.log")));

        // Read at the filled commit, as the gate did until the --head, the same heal is the red it was.
        (code, output) = RunAt(Heal, "filled4430");
        Assert.Equal(1, code);
        Assert.Contains("::error::heal-outside: .github/quality-ratchet.json: on the outside list (.github/quality-ratchet.json)", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ready_commit_that_edits_the_ratchet_or_deletes_a_file_is_still_red_and_one_that_cannot_be_read_is_red()
    {
        // The fake git is a bash script, so these run where the gate does, never on The Shed's Windows runner.
        if (NoPython() || OperatingSystem.IsWindows())
        {
            return;
        }
        Fake(("ratchet", ItsOwn + "M\t.github/quality-ratchet.json\n"), ("deletes", ItsOwn + "D\tserver/JobHunt.Server.Tests/ApplySkipsTests.cs\n"));

        var (code, output) = RunAt(Heal, "ratchet");
        Assert.Equal(1, code);
        Assert.Contains(".github/quality-ratchet.json: on the outside list", output, StringComparison.Ordinal);
        (code, output) = RunAt(Heal, "deletes");
        Assert.Equal(1, code);
        Assert.Contains("server/JobHunt.Server.Tests/ApplySkipsTests.cs: deleted (f:", output, StringComparison.Ordinal);
        (code, output) = RunAt(Heal, "missing");
        Assert.Equal(1, code);
        Assert.Contains("::error::heal-outside: the changes from main7caa to missing could not be read", output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_lane_that_is_not_a_heal_still_passes_and_a_heal_change_reads_the_same_at_its_head_as_from_a_changes_file()
    {
        // The fake git is a bash script, so these run where the gate does, never on The Shed's Windows runner.
        if (NoPython() || OperatingSystem.IsWindows())
        {
            return;
        }
        string outside = ItsOwn + "M\tinfra/jobhunt.bicep\nD\tsrc/a.ts\nM\t.github/workflows/gate.yml\n";
        Fake(("planning", ItsOwn + TheFill), ("outside", outside));

        Assert.Equal(0, RunAt("ready/healgate1124-abcd1234", "planning").Code);
        var atHead = RunAt(Heal, "outside");
        var fromFile = Run(Heal, outside);
        Assert.Equal(1, atHead.Code);
        Assert.Equal(fromFile, atHead);
        Assert.Contains("3 paths outside self-heal", atHead.Output, StringComparison.Ordinal);
    }

    /// <summary>A fake git on the PATH: merge-base answers start-of-head, and the diff from it is the head's fixture file.</summary>
    private void Fake(params (string Head, string Changes)[] heads)
    {
        string bin = Path.Combine(_root, "bin");
        Directory.CreateDirectory(bin);
        foreach (var (head, changes) in heads)
        {
            File.WriteAllText(Path.Combine(_root, $"diff-{head}.txt"), changes);
        }
        File.WriteAllText(Path.Combine(bin, "git"), """
            #!/usr/bin/env bash
            echo "$*" >> "$ROOT/git.log"
            case "$1" in
              merge-base) echo "start-of-$3";;
              diff) cat "$ROOT/diff-$4.txt";;
              *) exit 1;;
            esac

            """);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Path.Combine(bin, "git"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private (int Code, string Output) RunAt(string branch, string head) =>
        Start(["--branch", branch, "--base", "main7caa", "--head", head], start =>
        {
            start.Environment["PATH"] = Path.Combine(_root, "bin") + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
            start.Environment["ROOT"] = _root;
        });

    private (int Code, string Output) Run(string branch, string changes, string lane = "")
    {
        string file = Path.Combine(_root, "changes.txt");
        File.WriteAllText(file, changes);
        return Start(["--branch", branch, "--changes", file], start => start.Environment["GATE_LANE"] = lane);
    }

    private static (int Code, string Output) Start(string[] args, Action<ProcessStartInfo> setUp)
    {
        var start = new ProcessStartInfo("python3") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["GATE_LANE"] = string.Empty;
        start.Environment.Remove("GITHUB_REF_NAME");
        setUp(start);
        start.ArgumentList.Add(Path.Combine(Repo.Root(), "scripts", "heal_outside.py"));
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + errors.GetAwaiter().GetResult());
    }

    private static bool NoPython()
    {
        try
        {
            using var probe = Process.Start(new ProcessStartInfo("python3", "-c \"pass\"") { RedirectStandardOutput = true, RedirectStandardError = true })!;
            probe.WaitForExit();
            return probe.ExitCode != 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return true;
        }
    }
}
