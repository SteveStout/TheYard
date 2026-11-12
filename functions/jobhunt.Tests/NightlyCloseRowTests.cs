using System.Diagnostics;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// A nightly lane that ends closes its state.md row (ADR-109): nightly.yml's last step before the unmount looks for the
/// run's registration and its close, and writes a CLOSED row through the state.md spool when the lane registered and
/// wrote none, whatever the run came to. The step's own script runs here against a scratch share.
/// </summary>
public sealed class NightlyCloseRowTests : IDisposable
{
    private const string Lane = "reply1110-0400";

    private const string RunDir = "nightly/2026-11-10/0400-reply-opus-5-5";

    private readonly string _root = Directory.CreateTempSubdirectory("nightly-close-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Spool => Path.Combine(_root, "inbox", "append-inbox", "state.md");

    [Fact]
    public void The_close_step_runs_always_and_is_given_the_lane_and_what_the_run_came_to()
    {
        string step = NightlyWorkflowTests.Step("Close the lane's row");
        Assert.Contains("if: always()", step, StringComparison.Ordinal);
        Assert.Contains("LANE_ID: ${{ steps.day.outputs.lane_id }}", step, StringComparison.Ordinal);
        Assert.Contains("CAME_TO: ${{ job.status }}", step, StringComparison.Ordinal);
        Assert.Contains("echo \"lane_id=${mode}${day:5:2}${day:8:2}-${slot}\"", NightlyWorkflowTests.Step("Date, slot, mode and model"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_lane_that_registered_and_wrote_no_close_gets_one()
    {
        Registered();

        if (!Run("failure"))
        {
            return;
        }

        string row = Assert.Single(Closes());
        Assert.StartsWith($"\n## LANE: {Lane} CLOSED ", row, StringComparison.Ordinal);
        Assert.Contains("came to failure", row, StringComparison.Ordinal);
        Assert.Contains("Holds: none.", row, StringComparison.Ordinal);
        Assert.EndsWith(" | CLOSED |\n", row, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Spool, ".*"));
    }

    [Fact]
    public void A_lane_whose_registration_the_merger_already_appended_gets_one_too()
    {
        File.WriteAllText(Path.Combine(_root, "state.md"), $"\n## LANE: {Lane} (Opus 5.5, GitHub Actions runner). REGISTERED 04:02 CST. | IN PROGRESS |\n<!-- AID:20261110-100256Z-{Lane}-registry -->\n");
        Directory.CreateDirectory(Spool);

        if (!Run("success"))
        {
            return;
        }

        Assert.Single(Closes());
    }

    [Theory]
    [InlineData("## LANE: reply1110-0400 CLOSED 04:12 CST Tue 11/10. Holds: none. | CLOSED |")]
    [InlineData("## LANE: reply1110-0400 closing note, DONE written. | CLOSED |")]
    public void A_lane_that_closed_itself_gets_no_second_row(string close)
    {
        Registered();
        File.WriteAllText(Path.Combine(Spool, $"20261110-101200Z-{Lane}-close.md"), "\n" + close + "\n");

        if (!Run("success"))
        {
            return;
        }

        Assert.Single(Directory.GetFiles(Spool, "*-close.md"));
    }

    [Fact]
    public void A_lane_that_never_registered_gets_no_row()
    {
        Directory.CreateDirectory(Spool);
        File.WriteAllText(Path.Combine(Spool, "20261110-100256Z-bookkeeping1110-0400-registry.md"), "\n## LANE: bookkeeping1110-0400 (x). | IN PROGRESS |\n");

        if (!Run("failure"))
        {
            return;
        }

        Assert.Empty(Closes());
    }

    private void Registered()
    {
        Directory.CreateDirectory(Path.Combine(Spool, "merged"));
        File.WriteAllText(Path.Combine(Spool, "merged", $"20261110-100256Z-{Lane}-registry.md"), $"\n## LANE: {Lane} (Opus 5.5, GitHub Actions runner, RUN_DIR {RunDir}). REGISTERED 04:02 CST. | IN PROGRESS |\n");
    }

    private List<string> Closes() =>
        Directory.Exists(Spool) ? [.. Directory.GetFiles(Spool, $"*-{Lane}-close.md").Select(File.ReadAllText)] : [];

    /// <summary>Runs the step's script as the runner does; false where there is no bash to run it (the gate has one).</summary>
    private bool Run(string cameTo)
    {
        string script = Path.Combine(_root, "close.sh");
        File.WriteAllText(script, NightlyWorkflowTests.Script("Close the lane's row"));
        if (!File.Exists("/bin/bash"))
        {
            return false;
        }
        var start = new ProcessStartInfo("/bin/bash", ["--noprofile", "--norc", "-eo", "pipefail", script]) { RedirectStandardError = true, RedirectStandardOutput = true };
        start.Environment["JOBHUNT"] = _root;
        start.Environment["RUN_DIR"] = RunDir;
        start.Environment["LANE_ID"] = Lane;
        start.Environment["CAME_TO"] = cameTo;
        start.Environment["RUN"] = "42795386305";
        start.Environment["GITHUB_STEP_SUMMARY"] = Path.Combine(_root, "summary.md");
        using Process bash = Process.Start(start)!;
        string error = bash.StandardError.ReadToEnd();
        bash.WaitForExit();
        Assert.True(bash.ExitCode == 0, error);
        return true;
    }
}
