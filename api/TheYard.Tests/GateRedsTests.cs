using System.Diagnostics;
using System.Text.Json;
using TheYard.Api.Features.Docs;

namespace TheYard.Tests;

/// <summary>
/// What the gate caught (G5's data): a red gate never promotes, so nothing on main records it unless promote keeps it.
/// <c>scripts/gate_reds.mjs</c> folds the gate feed's reds into <c>data/gate-reds.json</c>, each run once and never a
/// cancelled one, and Built with AI and the landing read that file. Held on planted files, then on the real one.
/// </summary>
public sealed class GateRedsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "gate-reds-" + Guid.NewGuid().ToString("N"));

    public GateRedsTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        File.WriteAllText(Path.Combine(_root, "data", "gate-reds.json"), """
            {
              "about": "kept",
              "rows": [
                {"run":30,"version":"1.0.0.3","lane":null,"sha":"cccccccc","at":"2026-11-17T19:25:20Z","failed":["cosmos"]},
                {"run":10,"version":"1.0.0.1","lane":null,"sha":"aaaaaaaa","at":"2026-10-14T16:03:20Z","failed":[]}
              ]
            }
            """);
    }

    /// <summary>The planted files go with the case that planted them.</summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static bool NoNode()
    {
        try
        {
            using var probe = Process.Start(new ProcessStartInfo("node", "--version") { RedirectStandardOutput = true, RedirectStandardError = true })!;
            probe.WaitForExit();
            return probe.ExitCode != 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return true;
        }
    }

    /// <summary>The script as promote.sh runs it, from the tree's root, given a feed.</summary>
    private (int Code, string Output) Fold(string feed)
    {
        string file = Path.Combine(_root, "feed.json");
        File.WriteAllText(file, feed);
        var start = new ProcessStartInfo("node") { WorkingDirectory = _root, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(Repo.Root(), "scripts", "gate_reds.mjs"));
        start.ArgumentList.Add(file);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output + errors.GetAwaiter().GetResult());
    }

    private long[] Runs() =>
        [.. JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "data", "gate-reds.json"))).RootElement.GetProperty("rows").EnumerateArray().Select(row => row.GetProperty("run").GetInt64())];

    /// <summary>A failure joins the list in time order, once, without the gate's own verdict job; a cancelled run never does.</summary>
    [Fact]
    public void A_red_joins_once_in_time_order_and_a_cancelled_run_never_does()
    {
        if (NoNode())
        {
            return;
        }

        const string feed = """
            {"reds": [
              {"run":40,"version":"1.0.0.4","lane":"code1118","sha":"dddddddd","at":"2026-11-18T13:00:00Z","came":"failure","failed":["browser 1 of 2","gate"]},
              {"run":35,"version":"1.0.0.4","lane":null,"sha":"eeeeeeee","at":"2026-11-18T12:00:00Z","came":"cancelled","failed":[]},
              {"run":20,"version":"1.0.0.2","lane":null,"sha":"bbbbbbbb","at":"2026-11-01T12:00:00Z","came":"failure","failed":["node","gate"]}
            ]}
            """;
        var (code, output) = Fold(feed);
        Assert.True(code == 0, output);
        Assert.Equal([40, 30, 20, 10], Runs());
        using var kept = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "data", "gate-reds.json")));
        JsonElement newest = kept.RootElement.GetProperty("rows")[0];
        Assert.Equal(["browser 1 of 2"], newest.GetProperty("failed").EnumerateArray().Select(job => job.GetString()!));
        Assert.Equal("code1118", newest.GetProperty("lane").GetString());
        Assert.Equal("kept", kept.RootElement.GetProperty("about").GetString());

        // The next promote reads a feed that still names run 40: it is not written twice.
        Assert.Equal(0, Fold(feed).Code);
        Assert.Equal([40, 30, 20, 10], Runs());
    }

    /// <summary>A feed it cannot read leaves the list as it was and never fails the promote.</summary>
    [Fact]
    public void A_feed_it_cannot_read_leaves_the_list_alone()
    {
        if (NoNode())
        {
            return;
        }

        string before = File.ReadAllText(Path.Combine(_root, "data", "gate-reds.json"));
        Assert.Equal(0, Fold("not json").Code);
        Assert.Equal(before, File.ReadAllText(Path.Combine(_root, "data", "gate-reds.json")));
    }

    /// <summary>Built with AI's column says how many, between which days in Central time, and the latest with its jobs.</summary>
    [Fact]
    public void The_column_counts_the_reds_and_names_the_latest()
    {
        Assert.Equal("2 times between 14 October and 17 November; the latest, 1.0.0.3, at its cosmos job", GateReds.Caught(_root));
        File.WriteAllText(Path.Combine(_root, "data", "gate-reds.json"), """{"about": "kept", "rows": []}""");
        Assert.Equal("nothing yet: no gate run has stopped a version", GateReds.Caught(_root));
    }

    /// <summary>The real list is newest first, one run each, every row naming a version, and promote keeps it.</summary>
    [Fact]
    public void The_kept_list_is_newest_first_and_promote_writes_it()
    {
        using var kept = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo.Root(), "data", "gate-reds.json")));
        JsonElement[] rows = [.. kept.RootElement.GetProperty("rows").EnumerateArray()];
        Assert.NotEmpty(rows);
        string[] at = [.. rows.Select(row => row.GetProperty("at").GetString()!)];
        Assert.Equal(at.OrderByDescending(time => time, StringComparer.Ordinal), at);
        Assert.Equal(rows.Length, rows.Select(row => row.GetProperty("run").GetInt64()).Distinct().Count());
        Assert.All(rows, row => Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", row.GetProperty("version").GetString()));
        Assert.All(rows, row => Assert.DoesNotContain("gate", row.GetProperty("failed").EnumerateArray().Select(job => job.GetString()!)));

        string promote = File.ReadAllText(Path.Combine(Repo.Root(), "scripts", "promote.sh"));
        int feed = promote.IndexOf("bash scripts/gate_feed.sh", StringComparison.Ordinal);
        int fold = promote.IndexOf("node scripts/gate_reds.mjs \"$raw/feed.json\"", StringComparison.Ordinal);
        Assert.True(feed > 0 && fold > feed, "the reds are folded after the feed reads them");
        Assert.Contains("git add data/gate-reds.json", promote, StringComparison.Ordinal);
        Assert.Contains("data/gate-reds\\.json", promote, StringComparison.Ordinal);
    }

    /// <summary>
    /// A red ready branch names no version (ADR-137, One traffic cop for main), so gate_feed.sh keeps it under the one the
    /// cop gave it, read from the cop's status on its commit ("red gate as &lt;version&gt;"), and leaves out one with no
    /// such status: every kept row names a version. 1.0.5.100 kept the fail proof under its branch's name and main went
    /// red on the case above. Run against a stand-in gh in a throwaway repository, no network (RULE 79).
    /// </summary>
    [Fact]
    public void A_red_ready_branch_is_kept_under_the_version_the_cop_gave_it()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string repo = Path.Combine(_root, "repo");
        string bin = Path.Combine(_root, "bin");
        Directory.CreateDirectory(Path.Combine(repo, "scripts"));
        Directory.CreateDirectory(bin);
        File.Copy(Path.Combine(Repo.Root(), "scripts", "gate_feed.sh"), Path.Combine(repo, "scripts", "gate_feed.sh"));
        string runs = """[{"databaseId":3,"headBranch":"ready/alpha-aaaaaaaa","headSha":"aaaaaaaa11","conclusion":"failure","createdAt":"2026-11-19T15:00:00Z"},{"databaseId":2,"headBranch":"ready/beta-bbbbbbbb","headSha":"bbbbbbbb22","conclusion":"failure","createdAt":"2026-11-19T14:59:00Z"},{"databaseId":1,"headBranch":"gate/1.0.5.98-gamma-cccccccc","headSha":"cccccccc33","conclusion":"failure","createdAt":"2026-11-19T14:58:00Z"}]""";
        File.WriteAllText(Path.Combine(_root, "runs.json"), runs);
        File.WriteAllText(Path.Combine(bin, "gh"), """
            #!/usr/bin/env bash
            case "$*" in
              *"--workflow gate.yml --limit 50"*) cat "$ROOT/runs.json";;
              *"commits/aaaaaaaa11/statuses"*) echo "red gate as 1.0.5.101: xunit; xunit-sqlite.txt: Failed X";;
              *"/statuses"*) echo "";;
              *"--json jobs"*) echo '["dotnet"]';;
              *) echo "";;
            esac

            """);
        File.SetUnixFileMode(Path.Combine(bin, "gh"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var init = new ProcessStartInfo("git", "init -q") { WorkingDirectory = repo };
        using (var git = Process.Start(init)!)
        {
            git.WaitForExit();
        }
        var start = new ProcessStartInfo("bash", $"scripts/gate_feed.sh 99 \"{Path.Combine(_root, "feed.json")}\"") { WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["PATH"] = bin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        start.Environment["ROOT"] = _root;
        start.Environment["GITHUB_REPOSITORY"] = "SteveStout/TheYard";
        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(process.ExitCode == 0, output);
        using var feed = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "feed.json")));
        string[] kept = [.. feed.RootElement.GetProperty("reds").EnumerateArray().Select(red => $"{red.GetProperty("run").GetInt64()} {red.GetProperty("version").GetString()} {red.GetProperty("lane").GetString()}")];
        Assert.Equal(["3 1.0.5.101 alpha", "1 1.0.5.98 gamma"], kept);
        Assert.DoesNotContain("full", File.ReadAllText(Path.Combine(_root, "feed.json")), StringComparison.Ordinal);
    }
}
