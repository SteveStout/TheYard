using System.Diagnostics;
using System.Text.Json;

namespace TheYard.Tests;

/// <summary>
/// The outside self-heal list, held in the gate (the year-end plan's 7b; selfheal1120 version 5): a heal branch that
/// touches a path in data/heal-outside.json, or deletes a file, goes red; any other branch passes; and both the gate's
/// node job and preflight's catch list run the one script. The changes are a fixture file in a folder born and thrown
/// away with the test, never a live branch (RULE 79).
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
        Assert.Contains("python3 scripts/heal_outside.py --branch \"$GITHUB_REF_NAME\" --base \"${BASE:-origin/main}\" > \"$RAW/heal-outside.txt\" 2>&1 || code=$?\n          echo \"healoutside=$code\" >> \"$r\"", workflow, StringComparison.Ordinal);
        Assert.Contains("gate_job.sh \"$RAW\" prettier lint tsc vitest emdash quality healoutside", workflow, StringComparison.Ordinal);
        Assert.Contains("catch heal-outside python3 scripts/heal_outside.py --base \"$base\"", File.ReadAllText(Path.Combine(Repo.Root(), "scripts", "catch_list.sh")), StringComparison.Ordinal);
    }

    private (int Code, string Output) Run(string branch, string changes, string lane = "")
    {
        string file = Path.Combine(_root, "changes.txt");
        File.WriteAllText(file, changes);
        var start = new ProcessStartInfo("python3") { RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["GATE_LANE"] = lane;
        start.Environment.Remove("GITHUB_REF_NAME");
        foreach (string arg in new[] { Path.Combine(Repo.Root(), "scripts", "heal_outside.py"), "--branch", branch, "--changes", file })
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
