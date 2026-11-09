using System.Diagnostics;

namespace TheYard.Tests;

/// <summary>
/// <c>scripts/yard-test.sh</c>, the box's test runner, run for real against a bare repository standing in for GitHub
/// (ADR-098, the addendum of 1.0.4.163): a suite whose source folder the branch does not carry is skipped and says
/// so, and the run stays green; a folder that is there runs, and its red is still red.
///
/// <para>The script reads everything from <c>$HOME</c>, so the sandbox is a home of its own: the repository under
/// <c>repos/TheYard</c> and stand-ins for <c>dotnet</c>, <c>npx</c> and <c>npm</c> in <c>.local/bin</c>, which the
/// script puts first on its path. Each stand-in writes what it was asked to run, so a case can say a suite never
/// started rather than only that the output did not mention it.</para>
/// </summary>
public sealed class YardTestScriptTests : IDisposable
{
    private readonly string _home = Path.Combine(Path.GetTempPath(), "yard-test-" + Guid.NewGuid().ToString("N"));

    private string Clone => Path.Combine(_home, "repos", "TheYard");

    public void Dispose()
    {
        try
        {
            foreach (string file in Directory.EnumerateFiles(_home, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(_home, recursive: true);
        }
        catch (IOException)
        {
            // A temporary folder left behind is the system's to clear.
        }
    }

    // #region the-sandbox
    private (int Code, string Output) Run(string dir, string file, string args, string red = "")
    {
        var start = new ProcessStartInfo(file, args) { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["HOME"] = _home;
        start.Environment["FAKE_RED"] = red;
        start.Environment["GIT_AUTHOR_NAME"] = start.Environment["GIT_COMMITTER_NAME"] = "Gate Test";
        start.Environment["GIT_AUTHOR_EMAIL"] = start.Environment["GIT_COMMITTER_EMAIL"] = "gate@example.com";
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + errors.GetAwaiter().GetResult());
    }

    private void Git(string args)
    {
        var (code, output) = Run(Clone, "git", args);
        Assert.True(code == 0, $"git {args}: {output}");
    }

    private void Write(string path, string text)
    {
        string full = Path.Combine(Clone, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    /// <summary>Main in a bare origin holding the four suites' folders, less any named in <paramref name="absent"/>.</summary>
    private void Main(params string[] absent)
    {
        Directory.CreateDirectory(Path.Combine(_home, "repos"));
        Assert.Equal(0, Run(_home, "git", "init -q --bare -b main origin.git").Code);
        Assert.Equal(0, Run(Path.Combine(_home, "repos"), "git", $"clone -q {Path.Combine(_home, "origin.git")} TheYard").Code);
        Git("checkout -q -b main");
        Write("package-lock.json", "{}\n");
        foreach (string folder in new[] { "api", "server", "src", Path.Combine("tests", "e2e") }.Except(absent))
        {
            Write(Path.Combine(folder, "keep.txt"), "a file\n");
        }
        Git("add -A");
        Git("commit -q -m main");
        Git("push -q origin main");

        string bin = Path.Combine(_home, ".local", "bin");
        Directory.CreateDirectory(bin);
        const string Suite = "#!/usr/bin/env bash\necho \"$(basename \"$0\") $*\" >> \"$HOME/calls.log\"\n"
            + "case \" $* \" in *\" $FAKE_RED\"*) [ -n \"$FAKE_RED\" ] && { echo 'Failed!'; exit 1; };; esac\necho 'Passed!'\n";
        File.WriteAllText(Path.Combine(bin, "dotnet"), Suite);
        File.WriteAllText(Path.Combine(bin, "npx"), Suite);
        File.WriteAllText(Path.Combine(bin, "npm"), "#!/usr/bin/env bash\nmkdir -p node_modules\n");
        Assert.Equal(0, Run(bin, "chmod", "+x dotnet npx npm").Code);
    }

    private (int Code, string Output) YardTest(string suite = "all", string red = "") =>
        Run(_home, "bash", $"{Path.Combine(Repo.Root(), "scripts", "yard-test.sh")} main {suite}", red);

    private string Calls()
    {
        string log = Path.Combine(_home, "calls.log");
        return File.Exists(log) ? File.ReadAllText(log) : "";
    }
    // #endregion the-sandbox

    // #region the-cases
    /// <summary>A branch without server/ skips that suite by name, never starts it, runs the other three and stays green.</summary>
    [Fact]
    public void A_suite_whose_folder_is_absent_is_skipped_and_the_run_stays_green()
    {
        // The script runs on the box and on GitHub's Ubuntu runners; it takes its one-at-a-time lock with flock, which
        // the laptop's Git Bash does not carry, so a Windows checkout skips these cases rather than fail them.
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main("server");

        var (code, output) = YardTest();

        Assert.True(code == 0, output);
        Assert.Contains("skipped: server/ not on this branch", output, StringComparison.Ordinal);
        Assert.DoesNotContain("FAIL", output, StringComparison.Ordinal);
        string calls = Calls();
        Assert.DoesNotContain("server/JobHunt.Server.Tests", calls, StringComparison.Ordinal);
        Assert.Contains("dotnet test api", calls, StringComparison.Ordinal);
        Assert.Contains("npx vitest run", calls, StringComparison.Ordinal);
        Assert.Contains("npx playwright test", calls, StringComparison.Ordinal);
    }

    /// <summary>The control: a branch that carries server/ runs it, so the skip is about the folder and nothing else.</summary>
    [Fact]
    public void A_suite_whose_folder_is_there_runs()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();

        var (code, output) = YardTest("server");

        Assert.True(code == 0, output);
        Assert.DoesNotContain("skipped", output, StringComparison.Ordinal);
        Assert.Contains("dotnet test server/JobHunt.Server.Tests", Calls(), StringComparison.Ordinal);
    }

    /// <summary>A skip does not soften a real red: a suite that is there and fails still fails the run.</summary>
    [Fact]
    public void A_red_suite_beside_a_skipped_one_still_fails_the_run()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main("server");

        var (code, output) = YardTest(red: "api");

        Assert.Equal(1, code);
        Assert.Contains("skipped: server/ not on this branch", output, StringComparison.Ordinal);
        Assert.Contains("FAIL  api", output, StringComparison.Ordinal);
    }
    // #endregion the-cases
}
