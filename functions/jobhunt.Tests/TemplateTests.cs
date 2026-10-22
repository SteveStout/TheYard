using System.Text.RegularExpressions;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// infra/jobhunt.bicep sets the Function's app settings wholesale: a redeploy replaces every setting
/// the app holds with the list in the template. So a switch that is on in Azure and missing or off in
/// the template is a switch the next redeploy turns off without anybody asking. The tracker merger is
/// the one that matters most: the laptop no longer writes the tracker, so MERGER off means no row
/// reaches Job_Hunt_Tracker.csv at all (ADR: The tracker merger runs in Azure).
/// </summary>
public sealed partial class TemplateTests
{
    /// <summary>
    /// Names the Function reads that the template leaves out on purpose: MERGER_ROOT points the merger
    /// at a scratch folder for a test on Azure and is removed after; NIGHTLY_GITHUB_TOKEN is a secret
    /// set out of band and never kept in the repository; NIGHTLY_ANY_HOUR is a hand test of the clock;
    /// the Sheet mirror's Google client, refresh token and Sheet id are secrets or private, set the same way.
    /// </summary>
    private static readonly string[] OutOfBand = ["MERGER_ROOT", "NIGHTLY_GITHUB_TOKEN", "NIGHTLY_ANY_HOUR", "GMAIL_CLIENT_ID", "GMAIL_CLIENT_SECRET", "GMAIL_REFRESH_TOKEN", "TRACKER_SHEET_ID"];

    [Fact]
    public void The_template_turns_the_tracker_merger_on()
    {
        var settings = Settings();
        Assert.True(settings.ContainsKey("MERGER"), "infra/jobhunt.bicep has no MERGER setting: a redeploy would stop the tracker merger, and no row would reach the tracker");
        Assert.Equal("on", settings["MERGER"]);
    }

    [Fact]
    public void The_template_keeps_the_appends_on_as_they_run_in_azure()
    {
        var settings = Settings();
        Assert.True(settings.ContainsKey("APPENDS"), "infra/jobhunt.bicep has no APPENDS setting: a redeploy would stop the .md appends, and no lane's note would reach state.md");
        Assert.Equal("on", settings["APPENDS"]);
    }

    [Fact]
    public void The_template_keeps_both_dispatches_on_as_they_run_in_azure()
    {
        var settings = Settings();
        Assert.Equal("on", settings.GetValueOrDefault("NIGHTLY_DISPATCH"));
        Assert.Equal("on", settings.GetValueOrDefault("BOOKKEEPING_DISPATCH"));
    }

    [Fact]
    public void The_template_keeps_the_sheet_mirror_in_a_mode_it_runs_in()
    {
        var settings = Settings();
        Assert.True(settings.ContainsKey("SHEET_MIRROR"), "infra/jobhunt.bicep has no SHEET_MIRROR setting: a redeploy would stop the Sheet mirror");
        Assert.Contains(settings["SHEET_MIRROR"], new[] { "shadow", "on" });
    }

    [Fact]
    public void The_template_keeps_the_ats_sweep_in_a_mode_it_runs_in()
    {
        var settings = Settings();
        Assert.True(settings.ContainsKey("ATS_SWEEP"), "infra/jobhunt.bicep has no ATS_SWEEP setting: a redeploy would stop the ATS sweep");
        Assert.Contains(settings["ATS_SWEEP"], new[] { "shadow", "on" });
    }

    [Fact]
    public void Every_setting_the_function_reads_is_in_the_template_or_named_as_set_out_of_band()
    {
        var settings = Settings();
        var read = Directory.EnumerateFiles(Path.Combine(Root(), "functions", "jobhunt"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(path => ReadName().Matches(File.ReadAllText(path)).Select(m => m.Groups[1].Value))
            .Distinct()
            .ToList();
        Assert.Contains("MERGER", read);
        var missing = read.Where(name => !settings.ContainsKey(name) && !OutOfBand.Contains(name)).ToList();
        Assert.True(missing.Count == 0, "read by the Function and not in infra/jobhunt.bicep: " + string.Join(", ", missing));
    }

    /// <summary>The app settings the template lists, by name: each a one-line { name: 'X', value: ... } entry.</summary>
    private static Dictionary<string, string> Settings()
    {
        string bicep = File.ReadAllText(Path.Combine(Root(), "infra", "jobhunt.bicep"));
        return Setting().Matches(bicep).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.Ordinal);
    }

    /// <summary>The repository's root: the nearest folder above the test's output that holds infra/jobhunt.bicep.</summary>
    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "infra", "jobhunt.bicep")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("infra/jobhunt.bicep not found above " + AppContext.BaseDirectory);
    }

    [GeneratedRegex(@"\{\s*name:\s*'([A-Za-z0-9_]+)'\s*,\s*value:\s*'?([^'}]*?)'?\s*\}")]
    private static partial Regex Setting();

    [GeneratedRegex(@"GetEnvironmentVariable\(""([A-Za-z0-9_]+)""\)")]
    private static partial Regex ReadName();
}
