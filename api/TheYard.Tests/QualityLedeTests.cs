using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TheYard.Api.Features.Docs;

namespace TheYard.Tests;

/// <summary>
/// The Quality page's first two cards (ADR-130, the addendum on the page in two layers), held to the data the build
/// carries: one test number on the page, the landing's own, summed over the same suites the landing counts; the lede's
/// four readouts in the picture's order, each with its key; the slip's caption true of the history it is read from;
/// and the "over time" fold's count equal to the charts it holds. Reads the checked-in files and writes nothing (RULE 79).
/// </summary>
public sealed partial class QualityLedeTests
{
    private static readonly string Page = LiveCounts.Expand(File.ReadAllText(Path.Combine(Repo.Root(), "docs", "quality", "QUALITY.md")), Repo.Root());

    [Fact]
    public void The_tests_readout_sums_the_suites_the_landing_counts_once()
    {
        string landing = File.ReadAllText(Path.Combine(Repo.Root(), "src", "lib", "landingProof.ts"));
        Match list = CountedOnceList().Match(landing);
        Assert.True(list.Success, "landingProof.ts names its COUNTED_ONCE list");
        string[] suites = [.. Regex.Matches(list.Groups[1].Value, "'([^']+)'").Select(m => m.Groups[1].Value)];
        Assert.Equal(suites, QualityReadouts.CountedOnce);

        using var results = JsonDocument.Parse(File.ReadAllText(Repo.DataFile("test-results.json")));
        int passed = results.RootElement.GetProperty("suites").EnumerateArray()
            .Where(suite => suites.Contains(suite.GetProperty("id").GetString()))
            .Sum(suite => suite.GetProperty("passed").GetInt32());
        Assert.Equal(passed.ToString("#,0", CultureInfo.InvariantCulture), QualityReadouts.Measure("tests-passed", null, Repo.Root()));
    }

    [Fact]
    public void The_lede_carries_four_readouts_in_the_picture_s_order_each_with_its_key_and_one_test_number()
    {
        string[] rows = Fence("quality-lede");
        Assert.Equal(["held", "tests", "coverage", "review"], rows.Select(row => row.Split(" | ")[^1]));
        Assert.Equal(["quality numbers held", "tests passed", "of lines covered", "versions AI-reviewed"], rows.Select(row => row.Split(" | ")[0]));
        Assert.Matches(@"^tests passed \| [\d,]+ \| \| on the version you are reading \| tests$", rows[1]);
        Assert.Matches(@"^of lines covered \| \d+% \|", rows[2]);
        Assert.Matches(@"by CodeRabbit, since [A-Z][a-z]{2} \d+ \| review$", rows[3]);
        // The xUnit count is a tile in the numbers fold, named for what it counts, and not a readout.
        Assert.DoesNotContain(rows, row => row.Contains("xUnit", StringComparison.Ordinal));
    }

    [Fact]
    public void The_slip_s_caption_is_true_of_the_history_it_is_read_from()
    {
        Match caption = Caption().Match(Page);
        Assert.True(caption.Success, "the slip card carries its caption in the picture's shape");
        using var file = JsonDocument.Parse(File.ReadAllText(Repo.DataFile("quality-history.json")));
        string ratchet = file.RootElement.GetProperty("first_ratchet_version").GetString()!;
        var readings = file.RootElement.GetProperty("rows").EnumerateArray()
            .Where(r => r.GetProperty("numbers").TryGetProperty("async_without_cancellation", out JsonElement v) && v.ValueKind == JsonValueKind.Number)
            .Select(r => (Date: r.GetProperty("date").GetString()!, Version: r.GetProperty("version").GetString() ?? "", Value: r.GetProperty("numbers").GetProperty("async_without_cancellation").GetInt32()))
            .ToList();
        int at = readings.FindIndex(r => r.Version == ratchet);
        int today = readings[^1].Value;
        // It reached today's value on the ratchet's own day and has never risen since.
        int low = readings.FindIndex(at, r => r.Value == today);
        Assert.Equal(readings[at].Date, readings[low].Date);
        Assert.Equal(today, readings.Skip(low).Max(r => r.Value));
        Assert.Equal(readings[0].Value.ToString(CultureInfo.InvariantCulture), caption.Groups["first"].Value);
        Assert.Equal(readings.Max(r => r.Value).ToString(CultureInfo.InvariantCulture), caption.Groups["worst"].Value);
        Assert.Equal(readings[at].Value.ToString(CultureInfo.InvariantCulture), caption.Groups["still"].Value);
        Assert.Equal(today.ToString(CultureInfo.InvariantCulture), caption.Groups["today"].Value);
    }

    [Fact]
    public void The_over_time_fold_counts_the_charts_it_holds()
    {
        string fold = Page[Page.IndexOf("data-key=\"history\"", StringComparison.Ordinal)..];
        fold = fold[..fold.IndexOf("</details>", StringComparison.Ordinal)];
        int charts = Regex.Matches(fold, "^```quality-trend", RegexOptions.Multiline).Count;
        Assert.Contains($">{charts} charts, ", fold, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_fold_starts_closed_and_no_section_of_the_reading_layer_names_a_version()
    {
        Assert.Equal(7, Regex.Matches(Page, "<details data-mock=\"fold\" data-key=\"[a-z]+\">").Count);
        Assert.DoesNotContain("<details open", Page, StringComparison.Ordinal);
        string reading = Page[..Page.IndexOf("## For engineers", StringComparison.Ordinal)];
        string words = Regex.Replace(reading, "^```quality-trend[\\s\\S]*?^```", "", RegexOptions.Multiline);
        Assert.DoesNotMatch(@"\d+\.\d+\.\d+\.\d+", words);
    }

    private static string[] Fence(string name)
    {
        int start = Page.IndexOf($"```{name}", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the page has a {name} fence");
        int body = Page.IndexOf('\n', start) + 1;
        return Page[body..].Split("\n```", 2)[0].Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    [GeneratedRegex(@"const COUNTED_ONCE = \[([^\]]*)\]")]
    private static partial Regex CountedOnceList();

    [GeneratedRegex(@"It went from (?<first>\d+) to (?<worst>\d+) in \w+ days and was still at (?<still>\d+) two weeks later\. The ratchet went in on \d+ \w+, the count fell to (?<today>\d+) that same day")]
    private static partial Regex Caption();
}
