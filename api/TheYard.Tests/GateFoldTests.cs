using System.Diagnostics;
using System.Text.Json;

namespace TheYard.Tests;

/// <summary>
/// The gate's fold run for real (ADR-098, the addendum of 1.0.5.4): <c>scripts/gate_fold.py</c> given the reports of
/// the check jobs as the gate job downloads them, one folder per job and attempt, and read for the one gate-raw
/// folder it writes.
///
/// <para>The gate ran in one job until 1.0.5.4, and its reports were one folder. Split, they are five, and the fold is
/// where a wrong merge would ship a record of another attempt (the 1.0.4.122 failure) or half the browser suite as the
/// whole of it. These cases plant the folders and read what the verdict, promote.sh and the results writer would.</para>
/// </summary>
public sealed class GateFoldTests : IDisposable
{
    private static readonly string[] Jobs = ["dotnet", "cosmos", "node", "browser-1", "browser-2"];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fold-" + Guid.NewGuid().ToString("N"));

    private string Downloads => Path.Combine(_root, "jobs");

    private string Raw => Path.Combine(_root, "raw");

    /// <summary>The planted folders go with the case that planted them.</summary>
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
    private void Plant(string artifact, string name, string text)
    {
        string full = Path.Combine(Downloads, artifact, name);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text.ReplaceLineEndings("\n"));
    }

    private static string Shard(params string[] specs) => JsonSerializer.Serialize(new
    {
        config = new { },
        suites = specs.Select(spec => new { title = spec, file = spec, specs = new[] { new { title = "opens", tests = new[] { new { status = "expected", results = new[] { new { duration = 10 } } } } } } }),
        errors = Array.Empty<object>(),
        stats = new { expected = specs.Length, unexpected = 0, flaky = 0, skipped = 0, duration = 100.0 * specs.Length },
    });

    /// <summary>One green attempt of every check job, in the files each one writes.</summary>
    private void Green(int attempt = 1)
    {
        Plant($"raw-dotnet-{attempt}", "dotnet.result", "format=skipped\nformat_s=0\nsqlproj=0\nsqlproj_s=9\nfunctions=0\nfunctions_s=28\nserver=0\nserver_s=20\nxunit=0\nxunit_s=50\n");
        Plant($"raw-dotnet-{attempt}", "build.result", "warnings=0\n");
        Plant($"raw-dotnet-{attempt}", "touched.txt", "api/A.cs\n");
        Plant($"raw-dotnet-{attempt}", "trx/xunit-sqlite.trx", "<TestRun />");
        Plant($"raw-cosmos-{attempt}", "dotnet.result", "live=0\nlive_s=17\noncosmos=0\noncosmos_s=76\n");
        Plant($"raw-cosmos-{attempt}", "node.result", "browser_cosmos=0\nbrowser_cosmos_s=82\n");
        Plant($"raw-cosmos-{attempt}", "touched.txt", "api/A.cs\n");
        Plant($"raw-cosmos-{attempt}", "trx/xunit-live.trx", "<TestRun />");
        Plant($"raw-node-{attempt}", "node.result", "prettier=0\nprettier_s=8\nlint=0\nlint_s=3\ntsc=0\ntsc_s=2\nvitest=0\nvitest_s=5\n");
        Plant($"raw-node-{attempt}", "rest.result", "emdash=0\nquality=0\n");
        Plant($"raw-browser-1-{attempt}", "node.result", "browser=0\nbrowser_s=150\n");
        Plant($"raw-browser-1-{attempt}", "browser-sqlite.json", Shard("a.spec.ts", "b.spec.ts"));
        Plant($"raw-browser-1-{attempt}", "browser-sqlite.txt", "  2 passed (2.5m)\n");
        Plant($"raw-browser-2-{attempt}", "node.result", "browser=0\nbrowser_s=170\n");
        Plant($"raw-browser-2-{attempt}", "browser-sqlite.json", Shard("c.spec.ts"));
        Plant($"raw-browser-2-{attempt}", "browser-sqlite.txt", "  1 passed (2.8m)\n");
    }

    private static bool NoPython()
    {
        try
        {
            using var probe = Process.Start(new ProcessStartInfo("python3", "-c \"pass\"") { RedirectStandardOutput = true, RedirectStandardError = true })!;
            probe.WaitForExit();
            return probe.ExitCode != 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return true;
        }
    }

    /// <summary>The fold as the gate job runs it, in the given attempt, naming every check job.</summary>
    private (int Code, string Output) Fold(int attempt = 1, string needs = "")
    {
        var start = new ProcessStartInfo("python3")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.Environment["GATE_NEEDS"] = needs;
        foreach (string arg in new[] { Path.Combine(Repo.Root(), "scripts", "gate_fold.py"), Downloads, Raw, attempt.ToString(System.Globalization.CultureInfo.InvariantCulture) }.Concat(Jobs))
        {
            start.ArgumentList.Add(arg);
        }
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + errors.GetAwaiter().GetResult());
    }

    private Dictionary<string, string> Result(string name) =>
        File.ReadAllLines(Path.Combine(Raw, name))
            .Select(line => line.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
    // #endregion the-sandbox

    // #region the-cases
    /// <summary>The control: every job green folds to the files the verdict and the results writer read, as one job wrote them.</summary>
    [Fact]
    public void Every_job_green_folds_to_one_gate_raw_in_the_shape_it_had()
    {
        if (NoPython())
        {
            return;
        }
        Green();

        var (code, output) = Fold();

        Assert.True(code == 0, output);
        var dotnet = Result("dotnet.result");
        Assert.Equal(new[] { "format", "format_s", "sqlproj", "sqlproj_s", "functions", "functions_s", "server", "server_s", "xunit", "xunit_s", "live", "live_s", "oncosmos", "oncosmos_s" }, dotnet.Keys);
        var node = Result("node.result");
        Assert.Equal("0", node["browser"]);
        Assert.Equal("170", node["browser_s"]);
        Assert.Equal("0", node["browser_cosmos"]);
        Assert.Equal("0", Result("rest.result")["fold"]);
        Assert.True(File.Exists(Path.Combine(Raw, "trx", "xunit-sqlite.trx")));
        Assert.True(File.Exists(Path.Combine(Raw, "trx", "xunit-live.trx")));
        Assert.Equal("api/A.cs\n", File.ReadAllText(Path.Combine(Raw, "touched.txt")));
    }

    /// <summary>The two shards' Playwright reports become the one browser-sqlite report, every spec file once.</summary>
    [Fact]
    public void The_two_shards_are_one_browser_report_with_every_spec_file()
    {
        if (NoPython())
        {
            return;
        }
        Green();

        var (code, output) = Fold();

        Assert.True(code == 0, output);
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(Raw, "browser-sqlite.json")));
        var files = report.RootElement.GetProperty("suites").EnumerateArray().Select(suite => suite.GetProperty("file").GetString()).ToList();
        Assert.Equal(new[] { "a.spec.ts", "b.spec.ts", "c.spec.ts" }, files);
        Assert.Equal(3, report.RootElement.GetProperty("stats").GetProperty("expected").GetInt32());
        string text = File.ReadAllText(Path.Combine(Raw, "browser-sqlite.txt"));
        Assert.Contains("2 passed", text, StringComparison.Ordinal);
        Assert.Contains("1 passed", text, StringComparison.Ordinal);
    }

    /// <summary>One shard red is the whole browser suite red, whichever shard it was and whatever the other said.</summary>
    [Fact]
    public void One_red_shard_is_the_browser_suite_red()
    {
        if (NoPython())
        {
            return;
        }
        Green();
        Plant("raw-browser-2-1", "node.result", "browser=1\nbrowser_s=175\n");

        var (code, output) = Fold();

        Assert.True(code == 0, output);
        Assert.Equal("1", Result("node.result")["browser"]);
    }

    /// <summary>
    /// A job that left no reports is the fold red, never a pass: with one shard missing, the other shard's green would
    /// otherwise read as the whole browser suite's.
    /// </summary>
    [Fact]
    public void A_job_with_no_reports_turns_the_fold_red_and_is_named()
    {
        if (NoPython())
        {
            return;
        }
        Green();
        Directory.Delete(Path.Combine(Downloads, "raw-browser-2-1"), recursive: true);

        var (code, output) = Fold();

        Assert.True(code == 0, output);
        Assert.Equal("1", Result("rest.result")["fold"]);
        Assert.Contains("browser-2: NO REPORTS", File.ReadAllText(Path.Combine(Raw, "fold.txt")), StringComparison.Ordinal);
        Assert.Contains("::error::the gate's fold found no reports from: browser-2", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The 1.0.4.122 case, split: attempt 1 had the dotnet job red, and only that job was re-run. Attempt 2 folds the
    /// re-run's green reports for it and keeps attempt 1's for every job that was not re-run.
    /// </summary>
    [Fact]
    public void A_job_re_run_green_replaces_its_red_attempt_and_the_rest_keep_theirs()
    {
        if (NoPython())
        {
            return;
        }
        Green();
        Plant("raw-dotnet-1", "dotnet.result", "format=skipped\nformat_s=0\nsqlproj=0\nsqlproj_s=9\nfunctions=0\nfunctions_s=28\nserver=0\nserver_s=20\nxunit=1\nxunit_s=50\n");
        Plant("raw-dotnet-2", "dotnet.result", "format=skipped\nformat_s=0\nsqlproj=0\nsqlproj_s=9\nfunctions=0\nfunctions_s=28\nserver=0\nserver_s=20\nxunit=0\nxunit_s=52\n");
        Plant("raw-dotnet-2", "build.result", "warnings=0\n");

        var (code, output) = Fold(attempt: 2);

        Assert.True(code == 0, output);
        var dotnet = Result("dotnet.result");
        Assert.Equal("0", dotnet["xunit"]);
        Assert.Equal("52", dotnet["xunit_s"]);
        Assert.Equal("0", dotnet["live"]);
        string fold = File.ReadAllText(Path.Combine(Raw, "fold.txt"));
        Assert.Contains("dotnet: raw-dotnet-2", fold, StringComparison.Ordinal);
        Assert.Contains("node: raw-node-1", fold, StringComparison.Ordinal);
    }

    /// <summary>The first attempt, read again after a second exists, folds only what the first attempt had.</summary>
    [Fact]
    public void An_attempt_never_folds_a_later_attempt_s_reports()
    {
        if (NoPython())
        {
            return;
        }
        Green();
        Plant("raw-dotnet-2", "dotnet.result", "xunit=7\n");

        var (code, output) = Fold(attempt: 1);

        Assert.True(code == 0, output);
        Assert.Equal("0", Result("dotnet.result")["xunit"]);
    }
    /// <summary>A check job that did not succeed is the fold red whatever reports it left, and the fold names it.</summary>
    [Fact]
    public void A_check_job_that_did_not_succeed_turns_the_fold_red()
    {
        if (NoPython())
        {
            return;
        }
        Green();

        var (code, output) = Fold(needs: "{\"dotnet\":{\"result\":\"success\"},\"browser\":{\"result\":\"failure\"}}");

        Assert.True(code == 0, output);
        Assert.Equal("1", Result("rest.result")["fold"]);
        Assert.Contains("job browser: failure", File.ReadAllText(Path.Combine(Raw, "fold.txt")), StringComparison.Ordinal);
    }

    /// <summary>Every check job that succeeded leaves the fold green.</summary>
    [Fact]
    public void Every_check_job_that_succeeded_leaves_the_fold_green()
    {
        if (NoPython())
        {
            return;
        }
        Green();

        var (code, output) = Fold(needs: "{\"dotnet\":{\"result\":\"success\"},\"cosmos\":{\"result\":\"success\"},\"node\":{\"result\":\"success\"},\"browser\":{\"result\":\"success\"}}");

        Assert.True(code == 0, output);
        Assert.Equal("0", Result("rest.result")["fold"]);
    }

    /// <summary>
    /// A check job's own verdict (scripts/gate_job.sh) is red on a red key and on a key it never wrote, and green on
    /// a pass or a skip the cuts made.
    /// </summary>
    [Theory]
    [InlineData("xunit=0\nformat=skipped\n", "xunit format", 0, "this job: nothing red")]
    [InlineData("xunit=1\nformat=0\n", "xunit format", 1, "::error::red in this job: xunit")]
    [InlineData("format=0\n", "xunit format", 1, "xunit: no result")]
    public void A_check_job_s_own_verdict_is_red_on_a_red_or_missing_key(string results, string keys, int expected, string said)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Directory.CreateDirectory(Raw);
        File.WriteAllText(Path.Combine(Raw, "dotnet.result"), results.ReplaceLineEndings("\n"));
        var start = new ProcessStartInfo("bash") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { Path.Combine(Repo.Root(), "scripts", "gate_job.sh"), Raw }.Concat(keys.Split(' ')))
        {
            start.ArgumentList.Add(arg);
        }
        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(expected == 0 ? process.ExitCode == 0 : process.ExitCode != 0, output);
        Assert.Contains(said, output, StringComparison.Ordinal);
    }
    // #endregion the-cases
}
