using System.Diagnostics;

namespace TheYard.Tests;

/// <summary>
/// <c>scripts/cop.sh</c>, the traffic cop's first step, run for real against planted repositories (ADR: One traffic cop
/// for main): a bare origin and a clone in a temporary folder, no network and no store (RULE 79). Two ready branches cut
/// from one main both land, as main plus one and main plus two, the second one's changelog line lifted over the first's;
/// a conflict outside the changelog, a branch of two commits and a branch carrying an older gate.yml are skipped green
/// with the reason; a workflow change with no PROMOTE_TOKEN goes on with its notice; gate/* takes the old path until
/// main carries the cop and is refused after.
/// </summary>
public sealed class CopScriptTests : IDisposable
{
    // Spelled in two parts, so the fill never rewrites this file.
    private static readonly string Ver = "@" + "VER@";
    private static readonly string Day = "@" + "DATE@";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "cop-" + Guid.NewGuid().ToString("N"));

    private string Work => Path.Combine(_root, "work");

    private string Outputs => Path.Combine(_root, "outputs.txt");

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
        start.Environment.Remove("HAS_PROMOTE_TOKEN");
        start.Environment.Remove("GITHUB_OUTPUT");
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

    private void Write(string path, string text)
    {
        string full = Path.Combine(Work, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private string Read(string path) => File.ReadAllText(Path.Combine(Work, path));

    private static string Gate(bool cop) => "name: Gate\non:\n  push:\n    branches: [\"gate/**\"" + (cop ? ", \"ready/**\"" : "") + "]\n";

    /// <summary>Main on 1.0.4.105 with a shared file, a gate.yml with or without the cop's trigger, in a bare origin and a clone holding the scripts.</summary>
    private string Main(bool cop = true)
    {
        Directory.CreateDirectory(_root);
        Assert.Equal(0, Run(_root, "git", "init -q --bare -b main origin.git").Code);
        Assert.Equal(0, Run(_root, "git", "clone -q origin.git work").Code);
        Git("checkout -q -b main");
        Directory.CreateDirectory(Path.Combine(Work, "scripts"));
        foreach (string script in new[] { "cop.sh", "versions.sh", "fill_version.sh" })
        {
            File.Copy(Path.Combine(Repo.Root(), "scripts", script), Path.Combine(Work, "scripts", script));
        }
        Write(".github/workflows/gate.yml", Gate(cop));
        Write("docs/CHANGELOG.md", "# Changelog\n\n- **1.0.4.105** (2026-10-27): a line.\n- **1.0.4.104** (2026-10-27): a line.\n");
        Write("shared.txt", "one\ntwo\nthree\n");
        Git("add -A");
        Git("commit -q -m main");
        Git("push -q origin main");
        return Git("rev-parse HEAD");
    }

    /// <summary>A lane's one commit on <paramref name="from"/>: its own file, a changed line of the shared file when named, and its changelog line with the placeholders.</summary>
    private string Lane(string from, string name, string? shared = null)
    {
        Git($"checkout -q --detach {from}");
        Write($"{name}.txt", name + "\n");
        if (shared is not null)
        {
            Write("shared.txt", $"one\n{shared}\nthree\n");
        }
        string log = Read("docs/CHANGELOG.md");
        int top = log.IndexOf("- **", StringComparison.Ordinal);
        Write("docs/CHANGELOG.md", log[..top] + $"- **{Ver}** ({Day}): {name} shipped.\n" + log[top..]);
        Git("add -A");
        Git($"commit -q -m \"TheYard {Ver}: {name}\"");
        return Git("rev-parse HEAD");
    }

    /// <summary>cop.sh on <paramref name="sha"/> as the cop job runs it, pushed as <paramref name="branch"/>.</summary>
    private (int Code, string Output, string Said) Cop(string sha, string branch, bool token = true)
    {
        Git($"checkout -q --detach {sha}");
        File.WriteAllText(Outputs, "");
        var env = new Dictionary<string, string>(StringComparer.Ordinal) { ["GITHUB_OUTPUT"] = Outputs };
        if (token)
        {
            env["HAS_PROMOTE_TOKEN"] = "true";
        }
        var (code, output) = Run(Work, "bash", $"scripts/cop.sh {branch} {Path.Combine(_root, "filled")}", env);
        return (code, output, File.ReadAllText(Outputs));
    }

    /// <summary>One output the cop wrote, by name.</summary>
    private static string Said(string said, string name) =>
        said.Split('\n').Where(line => line.StartsWith(name + "=", StringComparison.Ordinal)).Select(line => line[(name.Length + 1)..]).FirstOrDefault() ?? "";

    /// <summary>What promote does with a green run: main fast-forwards to the filled commit.</summary>
    private void Lands(string filled) => Git($"push -q origin {filled}:refs/heads/main");

    private string MainOnOrigin() => Run(_root, "git", "--git-dir origin.git rev-parse main").Output.Trim();
    // #endregion the-sandbox

    // #region the-cases
    /// <summary>The control and the race: two lanes cut from one main both land, the second rebased over the first, as N and N+1.</summary>
    [Fact]
    public void Two_ready_branches_cut_from_one_main_both_land_in_order()
    {
        // The script runs on Linux; a Windows checkout has no bash to give it.
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string main = Main();
        string first = Lane(main, "alpha");
        string second = Lane(main, "beta");

        var one = Cop(first, $"ready/alpha-{first[..8]}");
        Assert.True(one.Code == 0, one.Output);
        Assert.Equal("go", Said(one.Said, "landed"));
        Assert.Equal("1.0.4.106", Said(one.Said, "version"));
        Assert.Equal(first, Said(one.Said, "ready"));
        Assert.Equal(main, Git($"rev-parse {Said(one.Said, "sha")}~1"));
        Lands(Said(one.Said, "sha"));

        var two = Cop(second, $"ready/beta-{second[..8]}");
        Assert.True(two.Code == 0, two.Output);
        Assert.Equal("go", Said(two.Said, "landed"));
        Assert.Equal("1.0.4.107", Said(two.Said, "version"));
        string filled = Said(two.Said, "sha");
        Assert.Equal(MainOnOrigin(), Git($"rev-parse {filled}~1"));

        // Both lines, the newest on top, each with its own number; the placeholders gone from the tree and the message.
        Git($"checkout -q --detach {filled}");
        string log = Read("docs/CHANGELOG.md");
        Assert.Matches(@"- \*\*1\.0\.4\.107\*\* \(\d{4}-\d{2}-\d{2}\): beta shipped\.\n- \*\*1\.0\.4\.106\*\* \(\d{4}-\d{2}-\d{2}\): alpha shipped\.\n- \*\*1\.0\.4\.105\*\*", log);
        Assert.DoesNotContain(Ver, log, StringComparison.Ordinal);
        Assert.Equal("TheYard 1.0.4.107: beta", Git("log -1 --format=%s"));
        Assert.True(File.Exists(Path.Combine(Work, "alpha.txt")) && File.Exists(Path.Combine(Work, "beta.txt")));
    }

    /// <summary>The filled commit leaves as a bundle every later job checks out, from a fresh clone of origin.</summary>
    [Fact]
    public void The_filled_commit_is_handed_on_as_a_bundle_a_fresh_clone_can_check_out()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string main = Main();
        string lane = Lane(main, "alpha");

        var (code, output, said) = Cop(lane, $"ready/alpha-{lane[..8]}");

        Assert.True(code == 0, output);
        string filled = Said(said, "sha");
        Assert.Equal(filled, File.ReadAllText(Path.Combine(_root, "filled", "filled.sha")).Trim());
        Assert.Equal(0, Run(_root, "git", "clone -q --depth 1 file://" + Path.Combine(_root, "origin.git") + " fresh").Code);
        string fresh = Path.Combine(_root, "fresh");
        Assert.Equal(0, Run(fresh, "git", "fetch -q " + Path.Combine(_root, "filled", "filled.bundle") + " cop-filled").Code);
        Assert.Equal(0, Run(fresh, "git", $"checkout -q --detach {filled}").Code);
    }

    /// <summary>A rebase that conflicts outside the changelog is the lane's: skipped green, the files named, main untouched.</summary>
    [Fact]
    public void A_conflict_outside_the_changelog_is_skipped_green_with_its_files_named()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string main = Main();
        string first = Lane(main, "alpha", shared: "two by alpha");
        string second = Lane(main, "beta", shared: "two by beta");
        Lands(Said(Cop(first, "ready/alpha-" + first[..8]).Said, "sha"));
        string before = MainOnOrigin();

        var (code, output, said) = Cop(second, "ready/beta-" + second[..8]);

        Assert.True(code == 0, output);
        Assert.Equal("skip", Said(said, "landed"));
        Assert.Contains("shared.txt", Said(said, "reason"), StringComparison.Ordinal);
        Assert.Contains("::notice::", output, StringComparison.Ordinal);
        Assert.DoesNotContain("::error::", output, StringComparison.Ordinal);
        Assert.Equal(before, MainOnOrigin());
    }

    /// <summary>A ready branch is one commit: two are skipped green, with how to fix it.</summary>
    [Fact]
    public void A_branch_of_two_commits_is_skipped_green()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string main = Main();
        Lane(main, "alpha");
        Write("more.txt", "more\n");
        Git("add -A");
        Git("commit -q -m more");
        string two = Git("rev-parse HEAD");

        var (code, output, said) = Cop(two, "ready/alpha-" + two[..8]);

        Assert.True(code == 0, output);
        Assert.Equal("skip", Said(said, "landed"));
        Assert.Contains("2 commits off main", Said(said, "reason"), StringComparison.Ordinal);
    }

    /// <summary>A branch cut before main's gate.yml last changed runs the old gate.yml, so it is skipped and told to rebase; one that changes gate.yml itself goes on.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_ready_branch_carrying_an_older_gate_yml_is_skipped(bool changesItself)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string main = Main();
        string lane = Lane(main, "alpha");
        if (changesItself)
        {
            Write(".github/workflows/gate.yml", Gate(true) + "# alpha's own change\n");
            Git("add -A");
            Git("commit -q --amend --no-edit");
            lane = Git("rev-parse HEAD");
        }
        Git("checkout -q main");
        Write(".github/workflows/gate.yml", Gate(true) + "# main moved on\n");
        Git("commit -q -am \"TheYard 1.0.4.106: the gate moved\"");
        Write("docs/CHANGELOG.md", "# Changelog\n\n- **1.0.4.106** (2026-10-28): the gate moved.\n- **1.0.4.105** (2026-10-27): a line.\n- **1.0.4.104** (2026-10-27): a line.\n");
        Git("commit -q -a --amend --no-edit");
        Git("push -q origin main");

        var (code, output, said) = Cop(lane, "ready/alpha-" + lane[..8]);

        Assert.True(code == 0, output);
        Assert.Equal("skip", Said(said, "landed"));
        Assert.Contains(changesItself ? "conflicts" : "older gate.yml", Said(said, "reason"), StringComparison.Ordinal);
    }

    /// <summary>A version that changes a workflow with no PROMOTE_TOKEN is still gated, green, with the notice that a hand promote is owed.</summary>
    [Fact]
    public void A_workflow_change_with_no_token_goes_on_with_the_notice()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string main = Main();
        Git($"checkout -q --detach {main}");
        Write(".github/workflows/other.yml", "name: Other\n");
        // The lane's one commit carries the new workflow beside its own file and its line.
        string lane = Lane(main, "alpha");

        var (code, output, said) = Cop(lane, "ready/alpha-" + lane[..8], token: false);

        Assert.True(code == 0, output);
        Assert.Equal("go", Said(said, "landed"));
        Assert.Equal("true", Said(said, "workflow"));
        Assert.Contains("::notice::this version changes a workflow and PROMOTE_TOKEN is missing", output, StringComparison.Ordinal);
    }

    /// <summary>gate/* takes the old path while main's gate.yml has no ready/** trigger, and is refused red once it has.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Gate_branches_take_the_old_path_until_main_carries_the_cop(bool copOnMain)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string main = Main(cop: copOnMain);
        Git($"checkout -q --detach {main}");
        Write("docs/CHANGELOG.md", "# Changelog\n\n- **1.0.4.106** (2026-10-28): old path.\n- **1.0.4.105** (2026-10-27): a line.\n- **1.0.4.104** (2026-10-27): a line.\n");
        Git("commit -q -am \"TheYard 1.0.4.106: old path\"");
        string sha = Git("rev-parse HEAD");

        var (code, output, said) = Cop(sha, $"gate/1.0.4.106-alpha-{sha[..8]}");

        if (copOnMain)
        {
            Assert.Equal(1, code);
            Assert.Contains("::error::gate/* is closed", output, StringComparison.Ordinal);
            Assert.Contains("ready/", output, StringComparison.Ordinal);
            return;
        }
        Assert.True(code == 0, output);
        Assert.Equal("go", Said(said, "landed"));
        Assert.Equal("1.0.4.106", Said(said, "version"));
        Assert.Equal(sha, Said(said, "sha"));
    }

    /// <summary>A lane never picks a version: a ready branch whose top line is already a number is skipped.</summary>
    [Fact]
    public void A_ready_branch_that_picked_its_own_number_is_skipped()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string main = Main();
        Git($"checkout -q --detach {main}");
        Write("docs/CHANGELOG.md", "# Changelog\n\n- **1.0.4.106** (2026-10-28): picked.\n- **1.0.4.105** (2026-10-27): a line.\n- **1.0.4.104** (2026-10-27): a line.\n");
        Git("commit -q -am \"TheYard 1.0.4.106: picked\"");
        string sha = Git("rev-parse HEAD");

        var (code, output, said) = Cop(sha, "ready/alpha-" + sha[..8]);

        Assert.True(code == 0, output);
        Assert.Equal("skip", Said(said, "landed"));
        Assert.Contains("placeholder", Said(said, "reason"), StringComparison.Ordinal);
    }
    // #endregion the-cases
}
