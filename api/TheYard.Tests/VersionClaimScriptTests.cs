using System.Diagnostics;

namespace TheYard.Tests;

/// <summary>
/// <c>scripts/versions.sh</c> run for real, as preflight runs it, against a bare repository standing in for GitHub
/// (ADR-031, the addendums on the claim check and on the fill at the claim). A lane learns that its number is taken
/// before it pushes, not after a whole gate: each refusal names the next free version. Taken means on main or named in
/// the held gate lock by another lane, never "a branch exists": a gate branch on origin, red or leftover, is named and
/// never blocks. The gate's own first check (<c>versions.sh gate</c>) and the one reading of a gate branch's name
/// (<c>branch_version</c>) are run here too.
///
/// <para>Text alone would survive a check weakened to <c>|| true</c>, so these cases run the script, the way
/// PromoteScriptTests runs promote.sh. Nothing here needs dotnet format or npm: the script needs only git.</para>
/// </summary>
public sealed class VersionClaimScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "claim-" + Guid.NewGuid().ToString("N"));

    private string Work => Path.Combine(_root, "work");

    /// <summary>The gate lock these cases read, in a folder of their own: never the share's.</summary>
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
    private static (int Code, string Output) Run(string dir, string file, string args, IReadOnlyDictionary<string, string>? env = null)
    {
        var start = new ProcessStartInfo(file, args) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["GIT_AUTHOR_NAME"] = start.Environment["GIT_COMMITTER_NAME"] = "Gate Test";
        start.Environment["GIT_AUTHOR_EMAIL"] = start.Environment["GIT_COMMITTER_EMAIL"] = "gate@example.com";
        start.Environment.Remove("OWNER_YES");
        start.Environment.Remove("GATE_LANE");
        start.Environment.Remove("GATE_LOCK");
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

    private string Git(string args)
    {
        var (code, output) = Run(Work, "git", args);
        Assert.True(code == 0, $"git {args}: {output}");
        return output.Trim();
    }

    private void Changelog(params string[] versions)
    {
        Directory.CreateDirectory(Path.Combine(Work, "docs"));
        File.WriteAllText(Path.Combine(Work, "docs", "CHANGELOG.md"), "# Changelog\n\n" + string.Concat(versions.Select(v => $"- **{v}** (2026-10-27): a line.\n")));
    }

    /// <summary>Main on 1.0.4.105, one commit on 1.0.4.104, in a bare origin, and a clone of it holding the script.</summary>
    private void Main()
    {
        Directory.CreateDirectory(_root);
        // The lock's folder, empty: no lock is held until a case writes one.
        Directory.CreateDirectory(Path.GetDirectoryName(Lock)!);
        Assert.Equal(0, Run(_root, "git", "init -q --bare -b main origin.git").Code);
        Assert.Equal(0, Run(_root, "git", "clone -q origin.git work").Code);
        Git("checkout -q -b main");
        Directory.CreateDirectory(Path.Combine(Work, "scripts"));
        File.Copy(Path.Combine(Repo.Root(), "scripts", "versions.sh"), Path.Combine(Work, "scripts", "versions.sh"));
        Changelog("1.0.4.104");
        Git("add -A");
        Git("commit -q -m 1.0.4.104");
        Git("tag older");
        Changelog("1.0.4.105", "1.0.4.104");
        Git("commit -q -am 1.0.4.105");
        Git("push -q origin main");
    }

    /// <summary>One commit on HEAD whose changelog's top line is <paramref name="version"/>.</summary>
    private void Version(string version)
    {
        Changelog(version, "1.0.4.105", "1.0.4.104");
        Git("commit -q -am " + version.Replace("@", "", StringComparison.Ordinal));
    }

    /// <summary>A gate branch on origin at a commit of its own, as another lane's push leaves it.</summary>
    private void AnotherLanesGate(string version)
    {
        Git("checkout -q -b other main");
        Changelog(version, "1.0.4.105", "1.0.4.104");
        Git("commit -q -am other-lane");
        Git($"push -q origin other:refs/heads/gate/{version}");
        Git("checkout -q main");
    }

    /// <summary>The claim check as preflight runs it, reading the lock in this case's own folder unless told otherwise.</summary>
    private (int Code, string Output) Claim(IReadOnlyDictionary<string, string>? env = null)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal) { ["GATE_LOCK"] = Lock };
        foreach (var (name, value) in env ?? new Dictionary<string, string>())
        {
            values[name] = value;
        }
        return Run(Work, "bash", "scripts/versions.sh", values);
    }

    /// <summary>The gate lock held, with the one line gate_lock.sh claim writes.</summary>
    private void Held(string lane, string version)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Lock)!);
        File.WriteAllText(Lock, $"lane={lane} pid=4242 since=2026-11-17T10:00:00-06:00 version={version}\n");
    }
    // #endregion the-sandbox

    // #region the-cases
    /// <summary>The control: main plus one, and no gate branch of that number anywhere.</summary>
    [Fact]
    public void A_filled_number_one_above_main_with_no_gate_branch_passes()
    {
        // The script runs on GitHub's Ubuntu runners and on Linux; a Windows checkout has no bash to give it.
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();
        Version("1.0.4.106");

        var (code, output) = Claim();

        Assert.True(code == 0, output);
        Assert.Contains("version claim ok: 1.0.4.106 is main's 1.0.4.105 plus one and no gate lock is held", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A gate branch of the number on origin, as a red gate leaves it, no longer takes the number: it is named and the
    /// claim passes, because a red gate frees its number with its lock and nothing waits on a branch delete.
    /// </summary>
    [Theory]
    [InlineData("1.0.4.106")]
    [InlineData("1.0.4.106-coding1112-13c45731")]
    public void A_gate_branch_of_the_number_on_origin_is_named_and_never_takes_it(string tail)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();
        AnotherLanesGate(tail);
        Version("1.0.4.106");

        var (code, output) = Claim();

        Assert.True(code == 0, output);
        Assert.Contains("version claim ok: 1.0.4.106 is main's 1.0.4.105 plus one and no gate lock is held", output, StringComparison.Ordinal);
        Assert.Contains($"on origin above main, not blocking (a branch never takes a number): {tail}", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The held lock is the other place a number is taken: another lane's lock naming it is refused in one sentence
    /// that names the holder; the lane's own lock passes; another lane's lock for another number does not take this one.
    /// </summary>
    [Theory]
    [InlineData("coding1112", "1.0.4.106", "", false, "1.0.4.106 is named in the gate lock held by coding1112 since 2026-11-17T10:00:00-06:00")]
    [InlineData("coding1112", "1.0.4.106", "coding1116", false, "1.0.4.106 is named in the gate lock held by coding1112")]
    [InlineData("coding1116", "1.0.4.106", "coding1116", true, "and the gate lock is yours, coding1116")]
    [InlineData("coding1112", "1.0.4.105", "coding1116", true, "the gate lock is held by coding1112 for 1.0.4.105, not this one")]
    [InlineData("coding1116", "1.0.4.107", "coding1116", false, "the gate lock you hold names 1.0.4.107 and this commit claims 1.0.4.106")]
    public void The_held_lock_takes_a_number_only_for_the_lane_that_holds_it(string holder, string named, string lane, bool passes, string said)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();
        Version("1.0.4.106");
        Held(holder, named);

        var (code, output) = Claim(lane.Length > 0 ? new Dictionary<string, string> { ["GATE_LANE"] = lane } : null);

        Assert.True(passes == (code == 0), output);
        Assert.Contains(said, output, StringComparison.Ordinal);
    }

    /// <summary>A lock folder that cannot be reached (another machine, the share not mounted) is said and not judged.</summary>
    [Fact]
    public void A_lock_folder_that_cannot_be_reached_is_not_judged()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();
        Version("1.0.4.106");

        var (code, output) = Claim(new Dictionary<string, string> { ["GATE_LOCK"] = Path.Combine(_root, "no-such-folder", "theyard-gate.lock") });

        Assert.True(code == 0, output);
        Assert.Contains("could not be reached, so it is not judged", output, StringComparison.Ordinal);
    }

    /// <summary>A number that is not main's top line plus one is refused and the next free version is printed.</summary>
    [Theory]
    [InlineData("1.0.4.107")]
    [InlineData("1.0.4.105")]
    [InlineData("1.0.4.104")]
    public void A_number_not_one_above_main_is_refused_with_the_next_free_version(string version)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();
        Version(version);

        var (code, output) = Claim();

        Assert.True(code != 0, output);
        Assert.Contains($"the changelog says {version} and main is on 1.0.4.105", output, StringComparison.Ordinal);
        Assert.Contains("the next free version is 1.0.4.106", output, StringComparison.Ordinal);
    }

    /// <summary>A gate branch left behind below main never blocks; it is named for whoever rules on it.</summary>
    [Fact]
    public void A_leftover_gate_branch_below_main_is_named_and_does_not_block()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();
        AnotherLanesGate("1.0.4.90");
        Version("1.0.4.106");

        var (code, output) = Claim();

        Assert.True(code == 0, output);
        Assert.Contains("left on origin at or below main, not blocking: 1.0.4.90", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A stacked version built on an older main, one commit filled to the live main plus one at its claim, passes:
    /// the check reads origin's main, not the branch's own base, so a stack filled at its claim is never refused here.
    /// </summary>
    [Fact]
    public void A_stacked_one_commit_branch_filled_to_main_plus_one_passes()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();
        Git("checkout -q -b stacked older");
        Version("1.0.4.106");

        var (code, output) = Claim();

        Assert.True(code == 0, output);
        Assert.Contains("version claim ok: 1.0.4.106", output, StringComparison.Ordinal);
    }

    /// <summary>A changelog whose top line is still the placeholder is told to fill the version first.</summary>
    [Fact]
    public void An_unfilled_top_line_is_refused_until_the_version_is_filled()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();
        // The placeholder in two parts, because the ship's fill replaces every whole one in the tree, this file's too.
        Version("@" + "VER@");

        var (code, output) = Claim();

        Assert.True(code != 0, output);
        Assert.Contains("fill the version first", output, StringComparison.Ordinal);
    }

    /// <summary>An origin that cannot be read is a refusal, never a pass and never left unjudged.</summary>
    [Fact]
    public void An_origin_that_cannot_be_read_is_refused()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();
        Version("1.0.4.106");
        Git("remote set-url origin " + Path.Combine(_root, "gone.git"));

        var (code, output) = Claim();

        Assert.True(code != 0, output);
        Assert.Contains("origin could not be read", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The owner's move of the third number passes only with OWNER_YES naming it, as promote.sh allows, and is
    /// refused without it.
    /// </summary>
    [Fact]
    public void The_owners_move_of_the_third_number_passes_only_with_owner_yes()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();
        Version("1.0.5.0");

        var (refused, without) = Claim();
        var (passed, with) = Claim(new Dictionary<string, string> { ["OWNER_YES"] = "1.0.5.0" });

        Assert.True(refused != 0, without);
        Assert.Contains("the next free version is 1.0.4.106", without, StringComparison.Ordinal);
        Assert.True(passed == 0, with);
        Assert.Contains("version claim ok: 1.0.5.0", with, StringComparison.Ordinal);
    }

    /// <summary>
    /// Preflight runs the check before its slow checks and counts a refusal as red, and promote.sh reads versions
    /// through the same file rather than a copy of its own.
    /// </summary>
    [Fact]
    public void Preflight_runs_the_claim_first_and_promote_shares_the_version_reading()
    {
        string preflight = File.ReadAllText(Path.Combine(Repo.Root(), "scripts", "preflight.sh"));
        string promote = File.ReadAllText(Path.Combine(Repo.Root(), "scripts", "promote.sh"));
        int claim = preflight.IndexOf("if bash scripts/versions.sh", StringComparison.Ordinal);

        Assert.True(claim > 0 && claim < preflight.IndexOf("check format ", StringComparison.Ordinal), "the claim check runs before dotnet format");
        Assert.Contains("red=\"$red version\"", preflight, StringComparison.Ordinal);
        Assert.Contains(". scripts/versions.sh", promote, StringComparison.Ordinal);
        Assert.DoesNotContain("versions() {", promote, StringComparison.Ordinal);
    }
    // #endregion the-cases

    // #region the-branch-name
    /// <summary>
    /// The version a gate branch names is the text after gate/ up to the first hyphen, so the new name
    /// (gate/version-lane-sha8, a lane with a hyphen of its own included) and the older gate/version both read.
    /// </summary>
    [Theory]
    [InlineData("gate/1.0.5.76-coding1112-13c45731", "1.0.5.76")]
    [InlineData("refs/heads/gate/1.0.5.76-autohunt1109-3-13c45731", "1.0.5.76")]
    [InlineData("gate/1.0.5.76", "1.0.5.76")]
    [InlineData("1.0.5.76", "1.0.5.76")]
    public void A_gate_branch_names_its_version_up_to_the_first_hyphen_in_both_forms(string branch, string version)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string script = Path.Combine(Repo.Root(), "scripts", "versions.sh");
        var (shell, fromShell) = Run(Repo.Root(), "bash", $"-c \". '{script}' && branch_version '{branch}'\"");
        var (python, fromPython) = Run(Repo.Root(), "python3", $"-B -c \"import sys; sys.path.insert(0, 'scripts'); import coderabbit_review as c; print(c.branch_version('{branch}'))\"");

        Assert.True(shell == 0, fromShell);
        Assert.Equal(version, fromShell.Trim());
        Assert.True(python == 0, fromPython);
        Assert.Equal(version, fromPython.Trim());
    }

    /// <summary>
    /// The gate's first check: a branch whose version is still main's top line plus one passes in seconds; once main
    /// has moved to that number (a second claim of it) or the changelog names another, the run is turned away at once.
    /// </summary>
    [Theory]
    [InlineData("1.0.4.106", "gate/1.0.4.106-coding1116-abcdef12", true, "number ok: 1.0.4.106 is main's 1.0.4.105 plus one")]
    [InlineData("1.0.4.106", "gate/1.0.4.106", true, "number ok: 1.0.4.106")]
    [InlineData("1.0.4.106", "gate/1.0.4.107-coding1116-abcdef12", false, "the branch names 1.0.4.107 and the changelog's top line is 1.0.4.106")]
    [InlineData("1.0.4.105", "gate/1.0.4.105-coding1116-abcdef12", false, "1.0.4.105 is no longer main's top line plus one: main is on 1.0.4.105")]
    public void The_gates_first_check_turns_away_a_number_that_is_no_longer_main_plus_one(string version, string branch, bool passes, string said)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();
        Version(version);

        var (code, output) = Run(Work, "bash", $"scripts/versions.sh gate {branch}");

        Assert.True(passes == (code == 0), output);
        Assert.Contains(said, output, StringComparison.Ordinal);
    }

    /// <summary>`next` prints the number a lane claims the lock for: origin/main's top line plus one.</summary>
    [Fact]
    public void Next_prints_main_plus_one()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();

        var (code, output) = Run(Work, "bash", "scripts/versions.sh next");

        Assert.True(code == 0, output);
        Assert.Equal("1.0.4.106", output.Trim());
    }
    // #endregion the-branch-name
}
