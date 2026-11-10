using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using TheYard.Api.Features.Docs;

namespace TheYard.Tests;

/// <summary>
/// The quality history and the page that draws it (ADR-130, Quality you can watch).
///
/// <para>A page that says quality held is only worth what its numbers are worth. So the history is held to the
/// scanner: it starts on 15 October, before the ratchet went in, so the slip is measured the same way as the turn,
/// runs in version order with every number the ratchet names, holds no limit before the ratchet existed, and its newest row is what <c>.github/quality.py</c> reads at HEAD once that version has its row. And the
/// page is held to the data: every live number on it expands from a file the build carries, and the prose types no
/// number of its own but the dates, the file cap and the coverage floor, each named below with why.</para>
/// </summary>
public sealed partial class QualityHistoryTests
{
    private static JsonElement History()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Repo.DataFile("quality-history.json")));
        return document.RootElement.Clone();
    }

    private static List<JsonElement> Measured() =>
        [.. History().GetProperty("rows").EnumerateArray().Where(r => r.GetProperty("source").GetString() == "quality.py")];

    private static string[] ChangelogVersions() =>
        [.. ChangelogVersion().Matches(File.ReadAllText(Path.Combine(Repo.Root(), "docs", "CHANGELOG.md"))).Select(m => m.Groups[1].Value).Take(2)];

    // #region the-history
    [Fact]
    public void The_history_starts_before_the_ratchet_and_runs_in_version_order_with_every_number()
    {
        JsonElement history = History();
        Assert.Equal("1.0.4.115", history.GetProperty("first_ratchet_version").GetString());
        var measured = Measured();
        Assert.Equal(history.GetProperty("rows").GetArrayLength(), measured.Count);
        Assert.Equal("2026-10-15", measured[0].GetProperty("date").GetString());
        int first = measured.FindIndex(r => r.GetProperty("version").GetString() == "1.0.4.115");
        Assert.True(first > 0, "the history should hold the days before the ratchet and then 1.0.4.115");
        Assert.All(measured.Take(first), r => Assert.Equal(JsonValueKind.Null, r.GetProperty("limits").ValueKind));
        var versions = measured.Select(r => Version.Parse(r.GetProperty("version").GetString()!)).ToList();
        Assert.Equal(versions.Order(), versions);
        Assert.Equal(versions.Count, versions.Distinct().Count());
        using var ratchet = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo.Root(), ".github", "quality-ratchet.json")));
        string[] keys = [.. ratchet.RootElement.EnumerateObject().Select(p => p.Name)];
        Assert.All(measured, r => Assert.All(keys, key => Assert.True(r.GetProperty("numbers").TryGetProperty(key, out _), $"{r.GetProperty("version")} has no {key}")));
    }

    /// <summary>
    /// The newest row is what the scanner reads at HEAD. A version's row is written by promote.sh as it ships, so on
    /// the gate branch the newest row is an earlier version's; from main on, it is this one, and equal. A row the
    /// promote failed to write is a gap in the history, never a red gate for the version after it, so an earlier
    /// newest row is allowed and a later one is not.
    /// </summary>
    [Fact]
    public void The_newest_row_is_what_the_scanner_reads_at_head()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        string[] top = ChangelogVersions();
        JsonElement newest = Measured()[^1];
        string version = newest.GetProperty("version").GetString()!;
        Assert.True(Version.Parse(version) <= Version.Parse(top[0]), $"the history's newest row, {version}, is ahead of the changelog's top line, {top[0]}");
        if (version != top[0])
        {
            return;
        }
        var start = new ProcessStartInfo("python3", $".github/quality.py \"{Repo.Root()}\"") { WorkingDirectory = Repo.Root(), RedirectStandardOutput = true, RedirectStandardError = true };
        using var process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        using var report = JsonDocument.Parse(output);
        foreach (JsonProperty number in report.RootElement.GetProperty("ratchet").EnumerateObject())
        {
            Assert.True(newest.GetProperty("numbers").GetProperty(number.Name).GetDouble() == number.Value.GetDouble(), $"{number.Name}: the history says {newest.GetProperty("numbers").GetProperty(number.Name)} for {version}, the scanner reads {number.Value} at HEAD");
        }
    }
    // #endregion the-history

    // #region the-page
    [Fact]
    public void Every_live_number_on_the_quality_page_expands_from_the_data()
    {
        string page = File.ReadAllText(Path.Combine(Repo.Root(), "docs", "quality", "QUALITY.md"));
        Assert.True(Placeholder().Matches(page).Count >= 30, "the Quality page should read its numbers live");
        string expanded = LiveCounts.Expand(page, Repo.Root());
        Assert.DoesNotContain("(no live measure", expanded, StringComparison.Ordinal);
        Assert.DoesNotContain("is not in this build", expanded, StringComparison.Ordinal);
        Assert.DoesNotContain("not measured", expanded, StringComparison.Ordinal);
        int rows = RulesRow().Matches(expanded).Count;
        int ownSuite = Regex.Matches(expanded, @"\| in its own suite, not this file \|$", RegexOptions.Multiline).Count;
        Assert.True(rows > 100 && ownSuite * 5 < rows, $"the rules table should carry ADR-075's rows with most of them read from this build's results: {rows} rows, {ownSuite} not in this file");
    }

    /// <summary>
    /// The prose types no number. Left out of the check: fences and live placeholders (the data's), link targets,
    /// record numbers and versions (names, not measurements), written dates (when, not how much), the 300-line cap
    /// (a rule, held by FileShapeTests) and the 90 and 75 per cent coverage floor (held by QualityRatchetTests).
    /// </summary>
    [Fact]
    public void The_quality_page_types_no_number_of_its_own()
    {
        string page = File.ReadAllText(Path.Combine(Repo.Root(), "docs", "quality", "QUALITY.md"));
        string prose = Fence().Replace(page, "");
        foreach (Regex named in new[] { Placeholder(), LinkTarget(), RecordOrVersion(), WrittenDate(), Allowed() })
        {
            prose = named.Replace(prose, "");
        }
        Assert.Empty(Digits().Matches(prose).Select(m => m.Value));
    }
    // #endregion the-page

    [GeneratedRegex(@"^- \*\*(\d+\.\d+\.\d+\.\d+)\*\*", RegexOptions.Multiline)]
    private static partial Regex ChangelogVersion();

    [GeneratedRegex(@"\{\{live:[^}]+\}\}")]
    private static partial Regex Placeholder();

    [GeneratedRegex(@"^```[\s\S]*?^```", RegexOptions.Multiline)]
    private static partial Regex Fence();

    [GeneratedRegex(@"\]\([^)]*\)")]
    private static partial Regex LinkTarget();

    [GeneratedRegex(@"ADR-\d+|\d+\.\d+\.\d+\.\d+")]
    private static partial Regex RecordOrVersion();

    [GeneratedRegex(@"\b\d{1,2} (?:October|November)\b")]
    private static partial Regex WrittenDate();

    [GeneratedRegex(@"\b300-line\b|\b90 per cent\b|\b75 per cent\b")]
    private static partial Regex Allowed();

    [GeneratedRegex(@"\| (?:\d+ of \d+ passed|in its own suite, not this file) \|$", RegexOptions.Multiline)]
    private static partial Regex RulesRow();

    [GeneratedRegex(@"\d+")]
    private static partial Regex Digits();
}
