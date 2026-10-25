using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// How every CI and gate job gets its .NET SDK (ADR-068, the addendum of 1.0.4.72): through the one composite action,
/// which tries the download twice a minute apart and then falls back to the runner image's own .NET 10 SDK, so a bad
/// answer from the download cannot turn a good version red before any check has run, as it did on 25 October.
/// </summary>
public sealed class DotnetInstallTests
{
    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([Repo.Root(), .. parts])).ReplaceLineEndings("\n");

    [Theory]
    [InlineData("ci.yml", 5)]
    [InlineData("gate.yml", 2)]
    public void Every_job_that_needs_dotnet_installs_it_through_the_one_action(string workflow, int jobs)
    {
        string text = Read(".github", "workflows", workflow);
        Assert.DoesNotMatch(new Regex(@"^\s*-?\s*uses: actions/setup-dotnet@", RegexOptions.Multiline), text);
        Assert.Equal(jobs, Regex.Matches(text, @"^\s*- uses: \./\.github/actions/dotnet\s*$", RegexOptions.Multiline).Count);
    }

    [Fact]
    public void The_action_tries_twice_then_uses_the_runner_s_own_sdk_and_fails_only_with_none()
    {
        string action = Read(".github", "actions", "dotnet", "action.yml");
        Assert.Contains("using: composite", action, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(action, @"uses: actions/setup-dotnet@v6\n\s+continue-on-error: true\n\s+with:\n\s+dotnet-version: 10\.0\.x").Count);
        Assert.Contains("if: steps.first.outcome == 'failure'\n      shell: bash\n      run: |\n        echo \"::warning::setup-dotnet failed on its first try; trying again in 60 seconds\"\n        sleep 60", action, StringComparison.Ordinal);
        Assert.Contains("if: steps.first.outcome == 'failure' && steps.second.outcome == 'failure'", action, StringComparison.Ordinal);
        Assert.Contains("dotnet --list-sdks", action, StringComparison.Ordinal);
        Assert.Contains("grep -E '^10\\.'", action, StringComparison.Ordinal);
        Assert.Contains("exit 1", action, StringComparison.Ordinal);
    }
}
