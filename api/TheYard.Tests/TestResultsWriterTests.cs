using System.Diagnostics;
using System.Text.Json;

namespace TheYard.Tests;

/// <summary>
/// The writer of <c>data/test-results.json</c>, run as promote.sh runs it, on a planted raw folder. A promote that is a
/// re-run of the Gate's promote job is handed seconds measured from the attempt's own start, which a re-run resets, while
/// checks.result still holds the first attempt's check jobs; the file it writes must still read gateSeconds at least
/// checksSeconds, the rule <see cref="TestResultsTests"/> holds the shipped file to.
/// </summary>
public sealed class TestResultsWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "test-results-" + Guid.NewGuid().ToString("N"));

    public TestResultsWriterTests()
    {
        string raw = Path.Combine(_root, "raw");
        Directory.CreateDirectory(raw);
        Directory.CreateDirectory(Path.Combine(_root, "data"));
        File.WriteAllText(Path.Combine(raw, "vitest.json"), """
            {"testResults":[{"name":"src/lib/a.test.ts","assertionResults":[{"ancestorTitles":["a"],"title":"passes","status":"passed","duration":3}]}]}
            """);
        foreach (string browser in new[] { "browser-sqlite.json", "browser-cosmos.json" })
        {
            File.WriteAllText(Path.Combine(raw, browser), """
                {"suites":[{"title":"a.spec.ts","file":"a.spec.ts","specs":[{"title":"opens","tests":[{"status":"expected","results":[{"duration":5}]}]}]}]}
                """);
        }
        File.WriteAllText(Path.Combine(raw, "node.result"), "vitest=0\nvitest_s=9\n");
        File.WriteAllText(Path.Combine(raw, "checks.result"), "checks_s=454\n");
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

    /// <summary>The script as promote.sh runs it, from the tree's root, with the promote's seconds.</summary>
    private JsonElement Write(string seconds)
    {
        var start = new ProcessStartInfo("node") { WorkingDirectory = _root, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(Repo.Root(), "scripts", "test_results.mjs"));
        start.ArgumentList.Add(Path.Combine(_root, "raw"));
        start.ArgumentList.Add("1.0.0.9");
        start.ArgumentList.Add(seconds);
        using var process = Process.Start(start)!;
        var errors = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, output + errors.GetAwaiter().GetResult());
        using var file = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "data", "test-results.json")));
        return file.RootElement.Clone();
    }

    /// <summary>A re-run promote of 29 seconds beside check jobs of 454 writes their wall clock plus its own: 483.</summary>
    [Fact]
    public void A_rerun_promote_writes_the_checks_wall_clock_plus_its_own_seconds()
    {
        if (NoNode())
        {
            return;
        }

        JsonElement root = Write("29");
        Assert.Equal(454, root.GetProperty("checksSeconds").GetInt32());
        Assert.Equal(483, root.GetProperty("gateSeconds").GetInt32());
    }

    /// <summary>A first-attempt promote, whose seconds already run past the check jobs, is written as it was handed.</summary>
    [Fact]
    public void A_first_attempt_promote_writes_the_seconds_it_was_handed()
    {
        if (NoNode())
        {
            return;
        }

        JsonElement root = Write("480");
        Assert.Equal(480, root.GetProperty("gateSeconds").GetInt32());
    }
}
