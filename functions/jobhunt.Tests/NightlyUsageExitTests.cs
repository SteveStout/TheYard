using System.Diagnostics;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// A nightly lane's usage post carries the lane's exit (ADR-112): the conclusion of the attempt that ran, as the
/// verdict step reads it, and no exit field when there is none. The step's own script runs here with a curl that keeps
/// what it was asked to post.
/// </summary>
public sealed class NightlyUsageExitTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("nightly-usage-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void The_usage_step_is_given_the_verdict()
    {
        Assert.Contains("EXIT: ${{ steps.verdict.outputs.came }}", NightlyWorkflowTests.Step("Report the usage to the site"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("failure", "\"exit\":\"failure\"")]
    [InlineData("success", "\"exit\":\"success\"")]
    public void The_post_carries_the_lane_s_conclusion(string exit, string field)
    {
        if (Post(exit) is { } posted)
        {
            Assert.Contains(field, posted, StringComparison.Ordinal);
            Assert.Contains("\"tokens_out\":29847", posted, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_post_with_no_verdict_has_no_exit_field()
    {
        if (Post(string.Empty) is { } posted)
        {
            Assert.DoesNotContain("\"exit\"", posted, StringComparison.Ordinal);
            Assert.Contains("\"run\":\"42795386305\"", posted, StringComparison.Ordinal);
        }
    }

    /// <summary>The document the step posted, or null where there is no bash and jq to run it (the gate has both).</summary>
    private string? Post(string exit)
    {
        string script = NightlyWorkflowTests.Script("Report the usage to the site");
        if (OperatingSystem.IsWindows() || !File.Exists("/bin/bash") || !File.Exists("/usr/bin/jq"))
        {
            return null;
        }
        string bin = Directory.CreateDirectory(Path.Combine(_root, "bin")).FullName;
        string posted = Path.Combine(_root, "posted.json");
        File.WriteAllText(Path.Combine(bin, "curl"), $"#!/bin/bash\nwhile [ $# -gt 0 ]; do [ \"$1\" = --data ] && printf '%s' \"$2\" > '{posted}'; shift; done\n");
        File.SetUnixFileMode(Path.Combine(bin, "curl"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string usage = Path.Combine(_root, "usage.json");
        File.WriteAllText(usage, """{"duration_ms": 412000, "num_turns": 52, "usage": {"input_tokens": 12, "cache_creation_input_tokens": 3, "cache_read_input_tokens": 4, "output_tokens": 29847}, "total_cost_usd": 1.5}""");
        File.WriteAllText(Path.Combine(_root, "step.sh"), script);
        var start = new ProcessStartInfo("/bin/bash", ["--noprofile", "--norc", "-eo", "pipefail", Path.Combine(_root, "step.sh")]) { RedirectStandardError = true, RedirectStandardOutput = true };
        start.Environment["PATH"] = bin + ":/usr/bin:/bin";
        start.Environment["USAGE"] = usage;
        start.Environment["KEY"] = "k";
        start.Environment["SITE"] = "https://example.test";
        start.Environment["MODEL"] = "claude-opus-5-5";
        start.Environment["MODE"] = "reply";
        start.Environment["DRY"] = "false";
        start.Environment["SLOT"] = "04:00";
        start.Environment["STARTED"] = "2026-11-10T10:00:06Z";
        start.Environment["RUN"] = "42795386305";
        start.Environment["LANE"] = "Azure Functions, nightly lane";
        start.Environment["EXIT"] = exit;
        start.Environment["GITHUB_STEP_SUMMARY"] = Path.Combine(_root, "summary.md");
        using Process bash = Process.Start(start)!;
        string error = bash.StandardError.ReadToEnd();
        bash.WaitForExit();
        Assert.True(bash.ExitCode == 0, error);
        Assert.True(File.Exists(posted), "nothing posted: " + (File.Exists(start.Environment["GITHUB_STEP_SUMMARY"]) ? File.ReadAllText(start.Environment["GITHUB_STEP_SUMMARY"]!) : error));
        return File.ReadAllText(posted);
    }
}
