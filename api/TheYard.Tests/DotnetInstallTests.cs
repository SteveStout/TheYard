using System.Text.Json;
using System.Text.RegularExpressions;

namespace TheYard.Tests;

/// <summary>
/// How every CI and gate job gets its .NET SDK (ADR-068, the addendum of 1.0.4.72): through the one composite action,
/// which tries the download twice a minute apart and then falls back to the runner image's own SDK, so a bad answer
/// from the download cannot turn a good version red before any check has run, as it did on 25 October. The version is
/// the one global.json names (ADR-098, the addendum of 1.0.5.47), so a runner image that carries a newer SDK cannot
/// build with it, as one carrying 11.0.100 did on 12 November.
/// </summary>
public sealed class DotnetInstallTests
{
    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine([Repo.Root(), .. parts])).ReplaceLineEndings("\n");

    [Theory]
    [InlineData("ci.yml", 6)]
    [InlineData("gate.yml", 5)]
    public void Every_job_that_needs_dotnet_installs_it_through_the_one_action(string workflow, int jobs)
    {
        string text = Read(".github", "workflows", workflow);
        Assert.DoesNotMatch(new Regex(@"^\s*-?\s*uses: actions/setup-dotnet@", RegexOptions.Multiline), text);
        Assert.Equal(jobs, Regex.Matches(text, @"^\s*- uses: \./\.github/actions/dotnet\s*$", RegexOptions.Multiline).Count);
    }

    [Fact]
    public void The_action_tries_twice_then_uses_the_runner_s_own_sdk_and_fails_only_with_none_that_global_json_allows()
    {
        string action = Read(".github", "actions", "dotnet", "action.yml");
        Assert.Contains("using: composite", action, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(action, @"uses: actions/setup-dotnet@v6\n\s+continue-on-error: true\n\s+with:\n\s+global-json-file: global\.json").Count);
        Assert.DoesNotContain("dotnet-version:", action, StringComparison.Ordinal);
        Assert.Contains("if: steps.first.outcome == 'failure'\n      shell: bash\n      run: |\n        echo \"::warning::setup-dotnet failed on its first try; trying again in 60 seconds\"\n        sleep 60", action, StringComparison.Ordinal);
        Assert.Contains("if ! sdk=$(dotnet --version 2>/dev/null); then", action, StringComparison.Ordinal);
        Assert.Contains("::error::no SDK on this runner satisfies global.json", action, StringComparison.Ordinal);
        Assert.Contains("exit 1", action, StringComparison.Ordinal);
    }

    [Fact]
    public void Global_json_names_one_sdk_and_rolls_forward_only_to_its_patches()
    {
        using JsonDocument json = JsonDocument.Parse(Read("global.json"));
        JsonElement sdk = json.RootElement.GetProperty("sdk");
        Assert.Matches(@"^10\.0\.\d{3}$", sdk.GetProperty("version").GetString());
        Assert.Equal("latestPatch", sdk.GetProperty("rollForward").GetString());
    }
}
