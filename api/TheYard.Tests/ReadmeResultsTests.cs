using System.Text.Json;
using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// The README quotes the gate's own results: how many Vitest tests ran, how many test runs the
/// gate made in all, and the version they were measured on. Those figures come from
/// data/test-results.json, which the gate rewrites for every version it ships, and a script in
/// the ship writes the README from it in the same commit. This holds the two equal, so the
/// README can never quote a gate older than the results file beside it.
/// (ADR: The five-minute gate)
/// </summary>
public sealed partial class ReadmeResultsTests
{
    [Fact]
    public void The_readme_quotes_the_results_file_for_the_version_it_names()
    {
        string root = Repo.Root();
        using JsonDocument results = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "data", "test-results.json")));
        string version = results.RootElement.GetProperty("version").GetString()!;
        int total = 0;
        int vitest = 0;
        foreach (JsonElement suite in results.RootElement.GetProperty("suites").EnumerateArray())
        {
            int count = suite.GetProperty("passed").GetInt32() + suite.GetProperty("failed").GetInt32();
            total += count;
            if (suite.GetProperty("id").GetString() == "vitest")
            {
                vitest = count;
            }
        }

        string readme = File.ReadAllText(Path.Combine(root, "README.md"));
        Match runs = TotalRuns().Match(readme);
        Assert.True(runs.Success, "README.md should say how many test runs the gate's results hold");
        Assert.Equal(version, runs.Groups["version"].Value);
        Assert.Equal(total, int.Parse(runs.Groups["total"].Value.Replace(",", string.Empty, StringComparison.Ordinal), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Contains($"{vitest} Vitest tests at {version}", readme, StringComparison.Ordinal);
    }

    /// <summary>The README's sentence naming the gate's version and its total runs.</summary>
    [GeneratedRegex(@"the gate's results for (?<version>\d+\.\d+\.\d+\.\d+) hold (?<total>[\d,]+) test runs")]
    private static partial Regex TotalRuns();
}
