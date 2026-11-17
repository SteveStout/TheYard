using System.Diagnostics;

namespace TheYard.Tests;

/// <summary>
/// <c>scripts/gate_lock.sh</c> run for real, against a lock in a folder of its own, a bare repository standing in for
/// GitHub and a stand-in gh (ADR-031, the addendum on the fill at the claim). The claim creates the lock only when none
/// is there and reads it back; the holder's line gains its run and branch; and a peer may release another lane's lock
/// only when its run ended red, or ended green with main already on its version. A green run that has not promoted is
/// never a peer's to release.
/// </summary>
public sealed class GateLockScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gate-lock-" + Guid.NewGuid().ToString("N"));

    private string Work => Path.Combine(_root, "work");

    private string Lock => Path.Combine(_root, "locks", "theyard-gate.lock");

    public void Dispose()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder left behind is the system's to clear.
        }
    }

    // #region the-sandbox
    private (int Code, string Output) Run(string dir, string file, string args, IReadOnlyDictionary<string, string>? env = null)
    {
        var start = new ProcessStartInfo(file, args) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["GIT_AUTHOR_NAME"] = start.Environment["GIT_COMMITTER_NAME"] = "Gate Test";
        start.Environment["GIT_AUTHOR_EMAIL"] = start.Environment["GIT_COMMITTER_EMAIL"] = "gate@example.com";
        start.Environment["PATH"] = Path.Combine(_root, "bin") + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        start.Environment["ROOT"] = _root;
        start.Environment["GATE_LOCK"] = Lock;
        start.Environment["GATE_FLOCK"] = Path.Combine(_root, "theyard-gate.flock");
        start.Environment["GATE_READ_BACK_SECONDS"] = "0";
        start.Environment.Remove("GATE_LANE");
        foreach (var (name, value) in env ?? new Dictionary<string, string>())
        {
            start.Environment[name] = value;
        }
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + errors.GetAwaiter().GetResult());
    }

    private void Git(string args)
    {
        var (code, output) = Run(Work, "git", args);
        Assert.True(code == 0, $"git {args}: {output}");
    }

    /// <summary>Main on 1.0.4.105 in a bare origin, a clone holding the scripts, the lock's folder, and a gh that answers from the environment.</summary>
    private void Sandbox()
    {
        Directory.CreateDirectory(Path.Combine(_root, "locks"));
        Directory.CreateDirectory(Path.Combine(_root, "bin"));
        Assert.Equal(0, Run(_root, "git", "init -q --bare -b main origin.git").Code);
        Assert.Equal(0, Run(_root, "git", "clone -q origin.git work").Code);
        Git("checkout -q -b main");
        Directory.CreateDirectory(Path.Combine(Work, "scripts"));
        Directory.CreateDirectory(Path.Combine(Work, "docs"));
        foreach (string script in new[] { "versions.sh", "gate_lock.sh" })
        {
            File.Copy(Path.Combine(Repo.Root(), "scripts", script), Path.Combine(Work, "scripts", script));
        }
        File.WriteAllText(Path.Combine(Work, "docs", "CHANGELOG.md"), "# Changelog\n\n- **1.0.4.105** (2026-10-27): a line.\n");
        Git("add -A");
        Git("commit -q -m main");
        Git("push -q origin main");
        string gh = Path.Combine(_root, "bin", "gh");
        File.WriteAllText(gh, "#!/usr/bin/env bash\necho \"$*\" >> \"$ROOT/calls.log\"\ncase \"$*\" in\n"
            + "  \"run view\"*) [ -n \"${FAKE_STATE:-}\" ] || exit 1; echo \"$FAKE_STATE\";;\n"
            + "  \"run list\"*) echo \"${FAKE_LISTED:-}\";;\n"
            + "  *) exit 1;;\nesac\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(gh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private (int Code, string Output) Lane(string args, IReadOnlyDictionary<string, string>? env = null) => Run(Work, "bash", "scripts/gate_lock.sh " + args, env);

    private string Calls() => File.Exists(Path.Combine(_root, "calls.log")) ? File.ReadAllText(Path.Combine(_root, "calls.log")) : "";
    // #endregion the-sandbox

    // #region the-release-rule
    /// <summary>
    /// The peer release rule, line by line: a run that ended red frees its number; a green run frees it only once main
    /// holds its version; a run still going, a run that cannot be read, a run that ended any other way, and a line with
    /// nothing to find its run by are all no. A line with no run is judged by the run gh lists for its commit.
    /// </summary>
    [Theory]
    [InlineData("lane=coding1112 version=1.0.4.106 run=4242", "completed failure", "", "yes: gate run 4242 concluded failure")]
    [InlineData("lane=coding1112 version=1.0.4.106 run=4242", "completed cancelled", "", "yes: gate run 4242 concluded cancelled")]
    [InlineData("lane=coding1112 version=1.0.4.105 run=4242", "completed success", "", "yes: gate run 4242 concluded success and main is on 1.0.4.105")]
    [InlineData("lane=coding1112 version=1.0.4.106 run=4242", "completed success", "", "no: gate run 4242 is green and main is on 1.0.4.105, not 1.0.4.106")]
    [InlineData("lane=coding1112 version=1.0.4.106 run=4242", "in_progress ", "", "no: gate run 4242 is in_progress, not ended")]
    [InlineData("lane=coding1112 version=1.0.4.106 run=4242", "completed skipped", "", "no: gate run 4242 concluded skipped")]
    [InlineData("lane=coding1112 version=1.0.4.106 run=4242", "", "", "no: gate run 4242 could not be read")]
    [InlineData("lane=coding1112 version=1.0.4.106", "completed failure", "", "no: the line names no run")]
    [InlineData("lane=coding1112 version=1.0.4.106 sha=13c45731aaaabbbbccccddddeeeeffff00001111", "completed failure", "77", "yes: gate run 77 concluded failure")]
    [InlineData("lane=coding1112 version=1.0.4.106 sha=13c45731aaaabbbbccccddddeeeeffff00001111", "completed failure", "", "no: no gate run is listed for 13c45731 yet")]
    public void A_peer_may_release_only_a_red_run_or_a_promoted_one(string line, string state, string listed, string said)
    {
        // The script runs on Linux; a Windows checkout has no bash to give it.
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Sandbox();

        var (code, output) = Lane($"may_release \"{line}\"", new Dictionary<string, string> { ["FAKE_STATE"] = state, ["FAKE_LISTED"] = listed });

        Assert.True(code == 0, output);
        Assert.StartsWith(said, output.Trim(), StringComparison.Ordinal);
        if (line.Contains("sha=", StringComparison.Ordinal))
        {
            Assert.Contains("run list -R SteveStout/TheYard --workflow gate.yml --commit 13c45731aaaabbbbccccddddeeeeffff00001111", Calls(), StringComparison.Ordinal);
        }
    }

    /// <summary>A peer's release of a green run that has not promoted is refused and the lock stays; the holder's own release always renames it.</summary>
    [Fact]
    public void A_green_unpromoted_run_is_released_by_its_holder_and_never_by_a_peer()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Sandbox();
        File.WriteAllText(Lock, "lane=coding1112 pid=1 since=2026-11-17T10:00:00-06:00 version=1.0.4.106 run=4242 branch=gate/1.0.4.106-coding1112-13c45731\n");
        var green = new Dictionary<string, string> { ["FAKE_STATE"] = "completed success", ["GATE_LANE"] = "coding1116" };

        var (peer, refused) = Lane("release", green);

        Assert.True(peer != 0, refused);
        Assert.Contains("a peer may not release it", refused, StringComparison.Ordinal);
        Assert.True(File.Exists(Lock), "the lock should stay when a peer's release is refused");

        var (holder, released) = Lane("release", new Dictionary<string, string> { ["GATE_LANE"] = "coding1112" });

        Assert.True(holder == 0, released);
        Assert.False(File.Exists(Lock));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "locks"), "theyard-gate.released-*"));
    }
    // #endregion the-release-rule

    // #region the-claim
    /// <summary>
    /// The claim writes the one line and reads it back; a second claim while it is held is refused with the holder's
    /// line; the holder's annotation adds the run and the branch; and a red run's release by a peer frees the number.
    /// </summary>
    [Fact]
    public void A_claim_holds_the_lock_until_its_red_run_releases_it()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Sandbox();

        var (claimed, first) = Lane("claim coding1116 1.0.4.106");
        var (again, second) = Lane("claim coding1112 1.0.4.106");
        var (annotated, third) = Lane("annotate 4242 gate/1.0.4.106-coding1116-13c45731");
        var (other, fourth) = Lane("annotate 4243 gate/1.0.4.106-coding1112-13c45731");
        string line = File.ReadAllText(Lock);
        var (released, fifth) = Lane("release", new Dictionary<string, string> { ["FAKE_STATE"] = "completed failure", ["GATE_LANE"] = "coding1112" });

        Assert.True(claimed == 0, first);
        Assert.Matches(@"^claimed: lane=coding1116 pid=\d+ since=\S+ version=1\.0\.4\.106\n$", first);
        Assert.True(again != 0, second);
        Assert.Contains("gate lock REFUSED: the gate lock is held: lane=coding1116", second, StringComparison.Ordinal);
        Assert.True(annotated == 0, third);
        Assert.EndsWith(" version=1.0.4.106 run=4242 branch=gate/1.0.4.106-coding1116-13c45731\n", line, StringComparison.Ordinal);
        Assert.True(other != 0, fourth);
        Assert.Contains("the lock is held by coding1116, not coding1112", fourth, StringComparison.Ordinal);
        Assert.True(released == 0, fifth);
        Assert.Contains("yes: gate run 4242 concluded failure, so 1.0.4.106 is free again", fifth, StringComparison.Ordinal);
        Assert.False(File.Exists(Lock));
        Assert.Equal(0, Lane("claim coding1112 1.0.4.106").Code);
    }

    /// <summary>The lock is created with noclobber and the claim takes a local flock for the seconds it reads, writes and reads back.</summary>
    [Fact]
    public void The_claim_creates_the_lock_exclusively_under_a_local_flock()
    {
        string script = File.ReadAllText(Path.Combine(Repo.Root(), "scripts", "gate_lock.sh"));
        Assert.Contains("set -o noclobber", script, StringComparison.Ordinal);
        Assert.Contains("flock -w", script, StringComparison.Ordinal);
        Assert.Contains("sleep \"${GATE_READ_BACK_SECONDS:-2}\"", script, StringComparison.Ordinal);
        Assert.Contains("${GATE_FLOCK:-$HOME/.cache/theyard-gate.flock}", script, StringComparison.Ordinal);
    }
    // #endregion the-claim
}
