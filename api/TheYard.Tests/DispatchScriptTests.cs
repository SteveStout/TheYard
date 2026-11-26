using System.Diagnostics;

namespace TheYard.Tests;

/// <summary>
/// <c>scripts/dispatch.sh</c> runs for real against a stand-in gh that answers each call from a planted list: the
/// after-roll starts the edge, the Function and CI on main through it, so a GitHub 5xx on one dispatch is called again
/// and no longer fails the whole after-roll, and a 4xx still fails at once.
/// </summary>
public sealed class DispatchScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "dispatch-" + Guid.NewGuid().ToString("N"));

    /// <summary>The planted answers go with the case that planted them.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder left behind is the system's to clear.
        }
    }

    // #region the-sandbox
    private string Bin => Path.Combine(_root, "bin");

    /// <summary>A stand-in gh that writes down each call and answers it with the next planted word: ok, or an HTTP status.</summary>
    private void Gh(params string[] answers)
    {
        Directory.CreateDirectory(Bin);
        File.WriteAllLines(Path.Combine(_root, "answers"), answers);
        string gh = Path.Combine(Bin, "gh");
        File.WriteAllText(gh, "#!/usr/bin/env bash\necho \"$*\" >> \"$ROOT/calls.log\"\n"
            + "n=$(wc -l < \"$ROOT/calls.log\")\nanswer=$(sed -n \"${n}p\" \"$ROOT/answers\")\n"
            + "case \"$answer\" in\n  ok) echo 'Created workflow_dispatch event';;\n"
            + "  offline) echo 'error connecting to api.github.com' >&2; exit 1;;\n"
            + "  *) echo \"could not create workflow dispatch event: HTTP $answer (https://api.github.com/x)\" >&2; exit 1;;\nesac\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(gh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private string[] Calls => File.Exists(Path.Combine(_root, "calls.log")) ? File.ReadAllLines(Path.Combine(_root, "calls.log")) : [];

    private (int Code, string Output) Dispatch(string args)
    {
        var start = new ProcessStartInfo("bash", $"\"{Path.Combine(Repo.Root(), "scripts", "dispatch.sh")}\" {args}") { WorkingDirectory = _root, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["PATH"] = Bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        start.Environment["ROOT"] = _root;
        start.Environment["DISPATCH_TRIES"] = "4";
        start.Environment["DISPATCH_PAUSE"] = "0";
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(1)))
        {
            process.Kill(entireProcessTree: true);
            return (-1, "the script did not finish in a minute");
        }
        return (process.ExitCode, output.GetAwaiter().GetResult() + errors.GetAwaiter().GetResult());
    }
    // #endregion

    /// <summary>GitHub's 504, 502 and a call that never reached it are each called again, and the start that took ends green.</summary>
    [Fact]
    public void A_5xx_or_no_answer_is_called_again_until_the_workflow_starts()
    {
        Gh("504", "offline", "502", "ok");
        var (code, output) = Dispatch("deploy-function.yml --ref main -f sha=abc");
        Assert.True(code == 0, output);
        Assert.Equal(4, Calls.Length);
        Assert.All(Calls, call => Assert.Equal("workflow run deploy-function.yml --ref main -f sha=abc", call));
        Assert.Contains("deploy-function.yml started (call 4 of 4)", output, StringComparison.Ordinal);
    }

    /// <summary>A 4xx is GitHub saying the call is wrong, so the script stops at the first one and fails.</summary>
    [Fact]
    public void A_4xx_fails_at_once()
    {
        Gh("422", "ok");
        var (code, output) = Dispatch("ci.yml --ref main -f sha=abc");
        Assert.Equal(1, code);
        Assert.Single(Calls);
        Assert.Contains("::error::ci.yml refused with a 4xx", output, StringComparison.Ordinal);
    }

    /// <summary>A 5xx on every call fails once the tries run out, and says how many it made.</summary>
    [Fact]
    public void An_outage_longer_than_the_tries_fails_and_says_so()
    {
        Gh("500", "500", "500", "500", "ok");
        var (code, output) = Dispatch("edge-branch.yml --ref main");
        Assert.Equal(1, code);
        Assert.Equal(4, Calls.Length);
        Assert.Contains("::error::edge-branch.yml did not start in 4 calls", output, StringComparison.Ordinal);
    }

    /// <summary>The after-roll starts every workflow it starts through the script, never with a bare gh workflow run.</summary>
    [Fact]
    public void The_after_roll_starts_its_workflows_through_the_script()
    {
        string[] lines = File.ReadAllLines(Path.Combine(Repo.Root(), ".github", "workflows", "after-roll.yml"));
        string[] bare = [.. lines.Where(line => line.TrimStart().StartsWith("gh workflow run", StringComparison.Ordinal) || line.TrimStart().StartsWith("run: gh workflow run", StringComparison.Ordinal))];
        Assert.Empty(bare);
        string workflow = string.Join('\n', lines);
        foreach (string started in new[] { "edge-branch.yml --ref main", "deploy-function.yml --ref main -f sha=\"$SHA\"", "ci.yml --ref main -f sha=\"$SHA\"" })
        {
            Assert.Contains("bash scripts/dispatch.sh " + started, workflow, StringComparison.Ordinal);
        }
    }
}
