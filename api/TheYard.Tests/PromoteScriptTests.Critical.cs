namespace TheYard.Tests;

/// <summary>
/// The Critical hold (ADR: CodeRabbit, one rung at a time, the addendum of 1.0.5.21): promote.sh reads the run's review row
/// before anything is written, and a Critical finding on a review that was read refuses the promote, naming the pull
/// request and where each Critical finding sits. A Major or a Minor holds nothing, and a review that never came, a row
/// whose findings were not read, or no row at all ships with its notice as before.
/// </summary>
public sealed partial class PromoteScriptTests
{
    // #region the-critical-hold
    private void Review(string reviewed, int critical, int major = 0, int minor = 0)
    {
        string at = critical > 0 ? """[{"path":"api/Any.cs","line":12,"pr":43}]""" : "[]";
        File.WriteAllText(
            Path.Combine(_root, "raw", "coderabbit.json"),
            $$"""{"version":"1.0.4.106","pr":41,"reviewed":"{{reviewed}}","actionable":{{critical + major + minor}},"findings":{"critical":{{critical}},"major":{{major}},"minor":{{minor}},"trivial":0,"nitpick":0},"critical_at":{{at}}}""");
    }

    /// <summary>A Critical finding on a review that was read holds the promote, names its request and place, and main stays.</summary>
    [Fact]
    public void A_critical_finding_holds_the_promote_and_names_where_it_is()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();
        string before = MainOnOrigin();
        Version("1.0.4.106");
        Review("full", critical: 1);

        var (code, output) = Promote("1.0.4.106", env: new Dictionary<string, string>(StringComparer.Ordinal) { ["FAKE_REVIEW"] = "success" });

        Assert.NotEqual(0, code);
        Assert.Contains("a Critical finding holds the promote", output, StringComparison.Ordinal);
        Assert.Contains("pull request #41", output, StringComparison.Ordinal);
        Assert.Contains("api/Any.cs:12 on #43", output, StringComparison.Ordinal);
        Assert.Equal(before, MainOnOrigin());
    }

    public static TheoryData<string> Advisory => new() { "major", "minor", "never-reviewed", "not-read", "no-row" };

    /// <summary>Everything short of a Critical on a read review ships as at Level 0.</summary>
    [Theory]
    [MemberData(nameof(Advisory))]
    public void A_review_with_no_critical_it_read_ships(string shape)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        Main();
        Version("1.0.4.106");
        switch (shape)
        {
            case "major":
                Review("full", critical: 0, major: 1);
                break;
            case "minor":
                Review("full", critical: 0, minor: 2);
                break;
            case "never-reviewed":
                Review("not reviewed inside the ceiling", critical: 0);
                break;
            case "not-read":
                Review("full, findings not read", critical: 1);
                break;
        }

        var (code, output) = Promote("1.0.4.106", env: new Dictionary<string, string>(StringComparer.Ordinal) { ["FAKE_REVIEW"] = "success" });

        Assert.True(code == 0, output);
        Assert.Equal(Git("rev-parse HEAD"), MainOnOrigin());
    }
    // #endregion the-critical-hold
}
