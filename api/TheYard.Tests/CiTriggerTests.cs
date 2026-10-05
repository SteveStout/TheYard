namespace TheYard.Tests;

/// <summary>
/// When CI runs (ADR: The five-minute gate, the addendum of 5 October). The gate
/// on the build machine decides whether anything is pushed; CI runs the same
/// suites again on a clean GitHub runner for every push to main, so each commit
/// carries a result anybody can read and the coverage floor, which only CI
/// measures, goes red on the commit that crosses it. A push that touches only
/// the sample has its own workflow and skips this one, as the deploy does.
/// </summary>
public class CiTriggerTests
{
    /// <summary>
    /// The workflow's <c>on:</c> block, from its line to the next line that
    /// starts in the first column, which is where YAML's next top-level key is.
    /// </summary>
    private static string TriggersOf(string workflow)
    {
        string text = File.ReadAllText(Path.Combine(Repo.Root(), ".github", "workflows", workflow)).ReplaceLineEndings("\n");
        int start = text.IndexOf("\non:\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{workflow} should have an on: block");
        string[] lines = text[(start + 1)..].Split('\n');
        var block = lines.Skip(1).TakeWhile(line => line.Length == 0 || line[0] == ' ' || line[0] == '#');
        return string.Join("\n", block);
    }

    [Fact]
    public void Ci_runs_on_every_push_to_main_on_a_pull_request_and_by_hand()
    {
        string triggers = TriggersOf("ci.yml");

        Assert.Contains("  push:\n    branches: [main]\n", triggers, StringComparison.Ordinal);
        Assert.Contains("  pull_request:", triggers, StringComparison.Ordinal);
        Assert.Contains("  workflow_dispatch:", triggers, StringComparison.Ordinal);
    }

    [Fact]
    public void A_push_that_touches_only_the_sample_skips_ci_as_it_skips_the_deploy()
    {
        string ci = TriggersOf("ci.yml");
        string deploy = TriggersOf("deploy.yml");

        foreach (string ignored in new[] { "\"samples/**\"", "\".github/workflows/deploy-shed.yml\"" })
        {
            Assert.Contains(ignored, ci, StringComparison.Ordinal);
            Assert.Contains(ignored, deploy, StringComparison.Ordinal);
        }
    }
}
