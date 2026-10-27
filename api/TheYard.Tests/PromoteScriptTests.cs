using System.Diagnostics;

namespace TheYard.Tests;

/// <summary>
/// <c>scripts/promote.sh</c> run for real, against a bare repository standing in for GitHub (ADR-098, the addendum of
/// 1.0.4.107): a green run moves main, and each push it must refuse leaves main where it was.
///
/// <para>The text of the script is held elsewhere (PromoteWorkflowTests). Text survives a check weakened to
/// <c>|| true</c>, so these cases run it: a run whose checks were red, a commit the run did not test, a commit that
/// does not sit on main, two commits ahead of main, a version that goes down, and a stray file beside the results.
/// <c>gh</c>, <c>curl</c>, <c>node</c> and <c>python3</c> are stand-ins on the path: the run's answer, a Cloud card
/// that does not answer, and the two results writers, which this test does not judge.</para>
/// </summary>
public sealed class PromoteScriptTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "promote-" + Guid.NewGuid().ToString("N"));

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
        start.Environment.Remove("GITHUB_ACTIONS");
        start.Environment.Remove("GITHUB_OUTPUT");
        start.Environment.Remove("GITHUB_RUN_ID");
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

    private static string Changelog(params string[] versions) =>
        "# Changelog\n\n" + string.Concat(versions.Select(v => $"- **{v}** (2026-10-27): a line.\n"));

    /// <summary>Main on 1.0.4.105 in a bare origin, a clone of it, and the four stand-ins on the path.</summary>
    private void Main()
    {
        Directory.CreateDirectory(_root);
        Assert.Equal(0, Run(_root, "git", "init -q --bare -b main origin.git").Code);
        Assert.Equal(0, Run(_root, "git", "clone -q origin.git work").Code);
        Git("checkout -q -b main");
        Write("docs/CHANGELOG.md", Changelog("1.0.4.105", "1.0.4.104"));
        foreach (string results in new[] { "data/test-results.json", "README.md", "docs/built-with-ai/BUILT-WITH-AI.md", "docs/images/og.svg", "public/og.png" })
        {
            Write(results, "results\n");
        }
        string resume = Path.Combine("api", "TheYard.Api", "wwwroot", "docs", "resume.pdf");
        Directory.CreateDirectory(Path.Combine(Work, Path.GetDirectoryName(resume)!));
        File.Copy(Path.Combine(Repo.Root(), resume), Path.Combine(Work, resume));
        Directory.CreateDirectory(Path.Combine(Work, "scripts"));
        File.Copy(Path.Combine(Repo.Root(), "scripts", "promote.sh"), Path.Combine(Work, "scripts", "promote.sh"));
        Git("add -A");
        Git("commit -q -m main");
        Git("push -q origin main");

        string bin = Path.Combine(_root, "bin");
        Directory.CreateDirectory(bin);
        File.WriteAllText(Path.Combine(bin, "gh"), "#!/usr/bin/env bash\ncase \"$*\" in *headSha*) echo \"$FAKE_TESTED\";; *jobs*) echo \"$FAKE_GREEN\";; *) exit 1;; esac\n");
        File.WriteAllText(Path.Combine(bin, "curl"), "#!/usr/bin/env bash\nexit 22\n");
        File.WriteAllText(Path.Combine(bin, "node"), "#!/usr/bin/env bash\nexit 0\n");
        File.WriteAllText(Path.Combine(bin, "python3"), "#!/usr/bin/env bash\nexit 0\n");
        Assert.Equal(0, Run(bin, "chmod", "+x gh curl node python3").Code);
        Directory.CreateDirectory(Path.Combine(_root, "raw"));
        File.WriteAllText(Path.Combine(_root, "raw", "dotnet.result"), "oncosmos=0\n");
    }

    /// <summary>One gate commit on HEAD naming <paramref name="version"/> at the top of the changelog.</summary>
    private void Version(string version, string below = "1.0.4.105")
    {
        Write("docs/CHANGELOG.md", Changelog(version, below, "1.0.4.104"));
        Git("commit -q -am " + version);
    }

    private string MainOnOrigin() => Run(_root, "git", "--git-dir origin.git rev-parse main").Output.Trim();

    /// <summary>promote.sh as a person runs it, the run's answer given by the stand-in gh.</summary>
    private (int Code, string Output) Promote(string version, string? tested = null, int green = 2) =>
        Run(Work, "bash", $"scripts/promote.sh {Path.Combine(_root, "raw")} 60 {version} 4242", new Dictionary<string, string>
        {
            ["PATH"] = Path.Combine(_root, "bin") + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
            ["FAKE_TESTED"] = tested ?? Git("rev-parse HEAD"),
            ["FAKE_GREEN"] = green.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });
    // #endregion the-sandbox

    // #region the-cases
    /// <summary>The control: one green, tested commit on main, a version up by one, moves main to it.</summary>
    [Fact]
    public void A_green_tested_commit_one_on_main_moves_main()
    {
        // The script runs on GitHub's Ubuntu runners and on Linux; a Windows checkout has no bash to give it.
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();
        Version("1.0.4.106");

        var (code, output) = Promote("1.0.4.106");

        Assert.True(code == 0, output);
        Assert.Equal(Git("rev-parse HEAD"), MainOnOrigin());
    }

    public static TheoryData<string, string> Refusals => new()
    {
        { "red", "gate and shed jobs are not both green" },
        { "untested", "tested deadbeef" },
        { "not-on-main", "not one commit on main" },
        { "two-ahead", "not one commit on main" },
        { "goes-down", "the version goes down" },
        { "extra-file", "the results touched more than their three files" },
    };

    /// <summary>Each push it must refuse is refused with its reason, and main does not move.</summary>
    [Theory]
    [MemberData(nameof(Refusals))]
    public void A_push_promote_must_refuse_leaves_main_where_it_was(string shape, string reason)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();
        string before = MainOnOrigin();
        string? tested = null;
        int green = 2;
        string version = "1.0.4.106";
        switch (shape)
        {
            case "red":
                Version(version);
                green = 1;
                break;
            case "untested":
                Version(version);
                tested = "deadbeef00000000000000000000000000000000";
                break;
            case "not-on-main":
                Git("checkout -q -b old");
                Write("docs/old.txt", "a commit main does not hold\n");
                Git("add -A");
                Git("commit -q -m side");
                Git("checkout -q main");
                Write("docs/newer.txt", "main moved\n");
                Git("add -A");
                Git("commit -q -m newer");
                Git("push -q origin main");
                before = MainOnOrigin();
                Git("checkout -q old");
                Version(version);
                break;
            case "two-ahead":
                Write("api/Any.cs", "class Any {}\n");
                Git("add -A");
                Git("commit -q -m code");
                Version(version);
                break;
            case "goes-down":
                version = "1.0.4.104";
                Version(version);
                break;
            case "extra-file":
                Version(version);
                Write("stray.txt", "not a result\n");
                break;
        }

        var (code, output) = Promote(version, tested, green);

        Assert.NotEqual(0, code);
        Assert.Contains(reason, output, StringComparison.Ordinal);
        Assert.Equal(before, MainOnOrigin());
    }
    // #endregion the-cases
}
