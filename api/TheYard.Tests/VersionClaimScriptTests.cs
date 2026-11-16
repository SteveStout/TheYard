using System.Diagnostics;

namespace TheYard.Tests;

/// <summary>
/// <c>scripts/versions.sh</c> run for real, as preflight runs it, against a bare repository standing in for GitHub
/// (ADR: The version comes from the changelog, the addendum on the claim check). A lane learns that its number is taken
/// before it claims, not after a whole gate: each refusal names the next free version, a gate branch at the lane's own
/// commit passes (the re-run after its own push), and a leftover gate branch below main never blocks.
///
/// <para>Text alone would survive a check weakened to <c>|| true</c>, so these cases run the script, the way
/// PromoteScriptTests runs promote.sh. Nothing here needs dotnet format or npm: the script needs only git.</para>
/// </summary>
public sealed class VersionClaimScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "claim-" + Guid.NewGuid().ToString("N"));

    private string Work => Path.Combine(_root, "work");

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

    private (int Code, string Output) Claim(IReadOnlyDictionary<string, string>? env = null) => Run(Work, "bash", "scripts/versions.sh", env);
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
        Assert.Contains("version claim ok: 1.0.4.106 is main's 1.0.4.105 plus one", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Another lane's gate branch holds the number: refused whatever its age, with its commit and subject, and the
    /// way past it named as a force-push with lease that is the owner's to run or allow, because the push after a
    /// pass here would be refused.
    /// </summary>
    [Fact]
    public void A_number_another_gate_branch_holds_is_refused_and_says_the_force_push_is_the_owners()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();
        AnotherLanesGate("1.0.4.106");
        Version("1.0.4.106");

        var (code, output) = Claim();

        Assert.True(code != 0, output);
        Assert.Contains("version claim REFUSED: gate/1.0.4.106 is already on origin at", output, StringComparison.Ordinal);
        Assert.Contains("\"other-lane\"", output, StringComparison.Ordinal);
        Assert.Contains("taken whatever its age", output, StringComparison.Ordinal);
        Assert.Contains("--force-with-lease=gate/1.0.4.106:", output, StringComparison.Ordinal);
        Assert.Contains("Steve's to run or allow", output, StringComparison.Ordinal);
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

    /// <summary>The gate branch is this commit itself, the re-run of preflight after the lane's own push: it passes.</summary>
    [Fact]
    public void The_lanes_own_gate_branch_at_head_passes()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();
        Version("1.0.4.106");
        Git("push -q origin HEAD:refs/heads/gate/1.0.4.106");

        var (code, output) = Claim();

        Assert.True(code == 0, output);
        Assert.Contains("your own branch", output, StringComparison.Ordinal);
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
        Version("@VER@");

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
}
