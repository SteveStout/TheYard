using System.Diagnostics;

namespace TheYard.Tests;

/// <summary>
/// The gate's verdict run for real, the script lifted out of <c>.github/workflows/gate.yml</c> and given a folder of
/// planted result files (ADR-098, the addendum of 1.0.4.119).
///
/// <para>Which checks the verdict reads is held as text by <c>GateWorkflowTests</c>. What it PRINTS is a different
/// thing and text cannot hold it: until this version every red run printed the last twelve lines of all seven text
/// logs, so a run whose only failure was the quality ratchet also sent "All matched files use Prettier code style!"
/// as an error annotation, and the failure email that reached the owner named six checks when one had failed. These
/// cases plant a red check beside green ones and insist the annotations name only the red one's log.</para>
/// </summary>
public sealed class GateVerdictTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "verdict-" + Guid.NewGuid().ToString("N"));

    /// <summary>The planted gate-raw folder goes with the case that planted it.</summary>
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

    // #region the-script
    /// <summary>
    /// The body of the gate job's verdict step: the lines under its <c>run: |</c>, dedented. Lifted rather than
    /// copied, so a verdict changed in the workflow and not here fails these cases instead of passing them.
    /// </summary>
    private static string Verdict()
    {
        string gate = File.ReadAllText(Path.Combine(Repo.Root(), ".github", "workflows", "gate.yml")).ReplaceLineEndings("\n");
        int step = gate.IndexOf("- name: The verdict", StringComparison.Ordinal);
        Assert.True(step > 0, "gate.yml should hold the verdict step");
        int body = gate.IndexOf("run: |\n", step, StringComparison.Ordinal);
        Assert.True(body > step, "the verdict step should hold a run block");
        var lines = gate[(body + "run: |\n".Length)..].Split('\n').TakeWhile(line => line.Length == 0 || line.StartsWith("          ", StringComparison.Ordinal));
        string script = string.Join("\n", lines.Select(line => line.Length == 0 ? line : line["          ".Length..]));
        Assert.Contains("red=\"\"", script, StringComparison.Ordinal);
        return script;
    }

    private void Plant(string name, string text) => File.WriteAllText(Path.Combine(_root, name), text.ReplaceLineEndings("\n"));

    /// <summary>A gate-raw folder where every check passed, which each case then breaks in one place.</summary>
    private void Green()
    {
        Directory.CreateDirectory(_root);
        Plant("node.result", "prettier=0\nprettier_s=4\nlint=0\nlint_s=9\ntsc=0\ntsc_s=7\nvitest=0\nvitest_s=60\nbrowser=0\nbrowser_s=570\nbrowser_cosmos=skipped\nbrowser_cosmos_s=0\n");
        Plant("dotnet.result", "format=0\nformat_s=20\nsqlproj=0\nsqlproj_s=30\nfunctions=0\nfunctions_s=60\nxunit=0\nxunit_s=300\nlive=0\nlive_s=40\noncosmos=skipped\noncosmos_s=0\n");
        Plant("rest.result", "emdash=0\nquality=0\n");
        Plant("build.result", "warnings=0\n");
        // Every text log holds what it holds on a GREEN run: this is the noise the verdict used to print.
        Plant("prettier.txt", "\n> theyard@0.1.0 format:check\n\nChecking formatting...\nAll matched files use Prettier code style!\n");
        Plant("lint.txt", "\n> theyard@0.1.0 lint\n\nFound 0 warnings and 0 errors.\n");
        Plant("tsc.txt", "");
        Plant("format.txt", "");
        Plant("sqlproj.txt", "Build succeeded.\n");
        Plant("emdash.txt", "");
        Plant("quality.txt", "undocumented_declarations 543 ok\ndotnet_tests_higher_is_better 2274 ok\n");
        Plant("build.txt", "Build succeeded.\n    0 Warning(s)\n");
        Plant("functions.txt", "Passed!  - Failed:     0, Passed:   386\n");
        Plant("xunit-sqlite.txt", "Passed!  - Failed:     0, Passed:  1693\n");
        Plant("xunit-live.txt", "Passed!  - Failed:     0, Passed:    31\n");
        Plant("browser-sqlite.txt", "  193 passed (9.5m)\n");
    }

    /// <summary>
    /// Whether this machine can run the verdict at all. The script is bash, as the gate's runner is, and the
    /// case is skipped where bash is absent rather than where Windows is: the laptop that ships has Git Bash,
    /// and a case only the runner can run is a case the push finds out about the expensive way.
    /// </summary>
    private static bool NoBash()
    {
        try
        {
            using var probe = Process.Start(new ProcessStartInfo("bash", "-c \"exit 0\"") { RedirectStandardOutput = true, RedirectStandardError = true })!;
            probe.WaitForExit();
            return probe.ExitCode != 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return true;
        }
    }

    /// <summary>The verdict as the gate runs it, with GitHub's own default shell for a run step.</summary>
    private (int Code, string Output) Run()
    {
        string file = Path.Combine(_root, "verdict.sh");
        File.WriteAllText(file, Verdict().ReplaceLineEndings("\n"));
        var start = new ProcessStartInfo("bash", $"-e \"{file}\"")
        {
            WorkingDirectory = _root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["RAW"] = _root;
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + errors.GetAwaiter().GetResult());
    }
    // #endregion the-script

    // #region the-cases
    /// <summary>The control: every check green, and the two the cuts may skip skipped, leaves the run green and silent.</summary>
    [Fact]
    public void A_run_with_nothing_red_says_so_and_prints_no_annotation()
    {
        if (NoBash())
        {
            return;
        }
        Green();

        var (code, output) = Run();

        Assert.True(code == 0, output);
        Assert.Contains("red: nothing", output, StringComparison.Ordinal);
        Assert.DoesNotContain("::error::", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The case the owner's inbox was full of: one check red, and the green checks' logs stay out of the
    /// annotations. Prettier's log holds its own cheerful sentence, and that sentence must not be an error.
    /// </summary>
    [Fact]
    public void A_red_check_prints_its_own_log_and_no_green_check_s()
    {
        if (NoBash())
        {
            return;
        }
        Green();
        Plant("node.result", File.ReadAllText(Path.Combine(_root, "node.result")).Replace("lint=0", "lint=1", StringComparison.Ordinal));
        Plant("lint.txt", "\n> theyard@0.1.0 lint\n\nsrc/app/App.tsx:12:1 error: planted, this is the real failure\nFound 0 warnings and 1 error.\n");

        var (code, output) = Run();

        Assert.True(code != 0, output);
        Assert.Contains("::error::red: lint", output, StringComparison.Ordinal);
        Assert.Contains("lint.txt: src/app/App.tsx:12:1 error: planted, this is the real failure", output, StringComparison.Ordinal);
        Assert.DoesNotContain("All matched files use Prettier code style!", output, StringComparison.Ordinal);
        foreach (string green in new[] { "prettier.txt", "sqlproj.txt", "quality.txt", "xunit-sqlite.txt", "browser-sqlite.txt", "build.txt" })
        {
            Assert.DoesNotContain($"::error::{green}", output, StringComparison.Ordinal);
        }
    }

    /// <summary>Each log belongs to one check, and only that check's red prints it.</summary>
    [Theory]
    [InlineData("rest.result", "quality=0", "quality=1", "quality.txt", "clock_outside_composition 46 OVER 45", "quality")]
    [InlineData("dotnet.result", "xunit=0", "xunit=1", "xunit-sqlite.txt", "  Failed AuthBidTests.A_vehicle_bought_outright_is_sold_to_everybody", "xunit")]
    [InlineData("node.result", "browser=0", "browser=1", "browser-sqlite.txt", "  1) tests/e2e/admin.spec.ts:40:3 planted", "browser")]
    [InlineData("build.result", "warnings=0", "warnings=3", "build.txt", "api/TheYard.Api/Program.cs(9,1): warning CA2016: planted", "warnings")]
    public void Only_the_log_of_the_check_that_failed_reaches_the_annotations(string result, string was, string now, string log, string line, string check)
    {
        if (NoBash())
        {
            return;
        }
        Green();
        Plant(result, File.ReadAllText(Path.Combine(_root, result)).Replace(was, now, StringComparison.Ordinal));
        Plant(log, line + "\n");

        var (code, output) = Run();

        Assert.True(code != 0, output);
        Assert.Contains($"::error::red: {check}", output, StringComparison.Ordinal);
        // The annotation carries the log's line as the log holds it, leading whitespace and all: the verdict
        // only cuts it at 300 characters. Measured against the shipped verdict, not assumed; the first push of
        // this version asserted a trimmed line here and the runner was the only place that could see it, because
        // these cases need bash and return early on Windows.
        Assert.Contains($"::error::{log}: {line}", output, StringComparison.Ordinal);
        // The six text logs the verdict used to print on any red, each of them green here.
        foreach (string other in new[] { "prettier.txt", "lint.txt", "format.txt", "sqlproj.txt", "emdash.txt", "quality.txt", "build.txt" })
        {
            if (other != log)
            {
                Assert.DoesNotContain($"::error::{other}", output, StringComparison.Ordinal);
            }
        }
    }

    /// <summary>A check the two cuts skipped is not a red one, and a skipped log that is not there is not read.</summary>
    [Fact]
    public void The_two_checks_the_cuts_may_skip_are_green_when_they_are_skipped()
    {
        if (NoBash())
        {
            return;
        }
        Green();

        var (code, output) = Run();

        Assert.True(code == 0, output);
        Assert.DoesNotContain("oncosmos", output, StringComparison.Ordinal);
        Assert.DoesNotContain("browser_cosmos", output, StringComparison.Ordinal);
    }
    // #endregion the-cases
}
