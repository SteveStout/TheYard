using System.Text.RegularExpressions;
using JobHunt.Application.Shared.Runs;
using JobHunt.Functions;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>zeroresult1112 rung 2: every process has a floor or the words for why empty is normal, and an empty run says so.</summary>
public sealed class RunFloorsTests
{
    private static readonly DateTimeOffset Slot = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Every_process_that_writes_a_run_line_and_every_process_on_the_Admin_tab_has_a_row()
    {
        var written = Directory.GetFiles(Path.Combine(Root(), "functions", "jobhunt"), "*.cs")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"new RunLine\(""(\w+)""").Select(m => m.Groups[1].Value));
        var admin = Regex.Matches(File.ReadAllText(Path.Combine(Root(), "api", "TheYard.Domain", "Features", "Admin", "FunctionProcesses.cs")), @"new\(""(\w+)"",")
            .Select(m => m.Groups[1].Value);

        var missing = written.Concat(admin).Distinct().Where(p => RunFloors.For(p) is null).ToList();

        Assert.True(missing.Count == 0, "no floor row for: " + string.Join(", ", missing));
        Assert.All(RunFloors.All, f => Assert.True(f.Proves is not null ^ f.Normal is not null, f.Process));
    }

    [Fact]
    public void A_run_under_its_floor_comes_to_blocked_zero_results_and_an_empty_normal_run_stays()
    {
        Assert.Equal(("blocked", "zero_results"), Outcome(RunFloors.Apply(new RunLine("FreshScan", Slot).Count("read", 0).Came("success"))));
        Assert.Equal(("success", null), Outcome(RunFloors.Apply(new RunLine("FreshScan", Slot).Count("read", 4).Came("success"))));
        Assert.Equal(("unchanged", "scan_hour"), Outcome(RunFloors.Apply(new RunLine("FreshScan", Slot).Came("unchanged", "scan_hour"))));
        Assert.Equal(("unchanged", null), Outcome(RunFloors.Apply(new RunLine("Merger", Slot).Count("merged", 0).Came("unchanged"))));
        Assert.Equal(("success", null), Outcome(RunFloors.Apply(new RunLine("AtsSweep", Slot).Count("read", 0).Came("success"))));
    }

    [Fact]
    public void A_dispatch_GitHub_did_not_take_is_blocked_on_the_clock_s_line()
    {
        Assert.Equal(("blocked", "dispatch_refused"), Outcome(Nightly.Dispatched(new RunLine("Nightly", Slot), "dispatch nightly 422", "4_AM_reply")));
        Assert.Equal(("success", "4_AM_reply"), Outcome(Nightly.Dispatched(new RunLine("Nightly", Slot), "dispatch nightly 204", "4_AM_reply")));
        Assert.Equal(("unchanged", "switch_off"), Outcome(Nightly.Dispatched(new RunLine("Nightly", Slot), "dispatch off (NIGHTLY_DISPATCH is not on)", "jobs")));
        Assert.Contains("dispatched=0", Nightly.Dispatched(new RunLine("Nightly", Slot), "dispatch blocked: NIGHTLY_GITHUB_TOKEN is not set", "jobs").Text(Slot), StringComparison.Ordinal);
    }

    private static (string, string?) Outcome(RunLine run) => (run.Outcome, run.Detail);

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "functions", "JobHunt.Database")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("functions/JobHunt.Database not found above " + AppContext.BaseDirectory);
    }
}
