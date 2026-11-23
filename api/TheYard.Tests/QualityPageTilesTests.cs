using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TheYard.Api.Features.Docs;

namespace TheYard.Tests;

/// <summary>
/// The Quality page as its picture draws it (ADR-130, the addendum on the page as its picture), held to the data the
/// build carries: the page served and expanded, read back the way a reader sees it. Every one of the fifteen numbers
/// has a tile, each tile's value is the newest row's, its note names the value where that number joined the ratchet
/// (its own join, the first row whose limits name it), the held pill counts what the newest limits hold, and
/// CodeRabbit's four readouts add up to the log the folded table is drawn from. Reads the checked-in data files and
/// writes nothing (RULE 79).
/// </summary>
public sealed partial class QualityPageTilesTests
{
    private static readonly string Page = LiveCounts.Expand(File.ReadAllText(Path.Combine(Repo.Root(), "docs", "quality", "QUALITY.md")), Repo.Root());

    private static JsonElement HistoryFile()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Repo.DataFile("quality-history.json")));
        return document.RootElement.Clone();
    }

    private static List<JsonElement> Measured() =>
        [.. HistoryFile().GetProperty("rows").EnumerateArray().Where(r => r.GetProperty("source").GetString() == "quality.py")];

    /// <summary>A fence of the expanded page by its name, its lines after the opening one.</summary>
    private static string[] FenceLines(string name)
    {
        // A fence may carry a head after its name: its lines start on the next line.
        int start = Page.IndexOf($"```{name}", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the page has a {name} fence");
        int body = Page.IndexOf('\n', start) + 1;
        return Page[body..].Split("\n```", 2)[0].Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string Written(JsonElement value) =>
        value.TryGetInt32(out int whole) ? whole.ToString("#,0", CultureInfo.InvariantCulture) : value.GetDouble().ToString("0.##", CultureInfo.InvariantCulture);

    [Fact]
    public void Every_number_has_one_tile_with_today_s_value_and_its_own_join_in_its_note()
    {
        string[] lines = FenceLines("quality-tiles");
        JsonElement newest = Measured()[^1];
        string firstRatchet = HistoryFile().GetProperty("first_ratchet_version").GetString()!;
        var numbers = newest.GetProperty("numbers").EnumerateObject().ToList();
        Assert.Equal(numbers.Count + 1, lines.Length);

        // Each tile by its number's key, read from the placeholder the page's source names it with.
        string source = File.ReadAllText(Path.Combine(Repo.Root(), "docs", "quality", "QUALITY.md"));
        var names = TileName().Matches(source).ToDictionary(m => m.Groups["key"].Value, m => m.Groups["name"].Value);
        var tiles = lines.Skip(1).Select(line => line.Split(" | ")).ToDictionary(cells => cells[0]);
        foreach (JsonProperty number in numbers)
        {
            string[] tile = tiles[names[number.Name]];
            JsonElement joinRow = Measured().First(r => r.GetProperty("limits").ValueKind == JsonValueKind.Object && r.GetProperty("limits").TryGetProperty(number.Name, out _));
            string joinVersion = joinRow.GetProperty("version").GetString()!;
            string atJoin = Written(joinRow.GetProperty("numbers").GetProperty(number.Name));
            Assert.Equal(Written(number.Value), tile[1]);
            if (tile[2] == "zero")
            {
                Assert.Equal("0", atJoin);
                continue;
            }
            string where = joinVersion == firstRatchet ? "at the ratchet" : $"when it joined at {joinVersion}";
            string way = number.Name.EndsWith("higher_is_better", StringComparison.Ordinal) ? "rise" : "fall";
            Assert.Equal(way, tile[2]);
            Assert.Equal($"{atJoin} {where}, may only {way}", tile[4]);
        }
    }

    [Fact]
    public void The_React_headers_note_their_own_join_and_not_the_ratchet_s()
    {
        string tile = FenceLines("quality-tiles").Single(line => line.StartsWith("React files with no header", StringComparison.Ordinal));
        Assert.Matches(@"\| fall \| \d+ \| [\d,]+ when it joined at 1\.0\.5\.1, may only fall$", tile);
    }

    [Fact]
    public void The_held_pill_counts_what_the_newest_limits_hold_on_the_newest_version()
    {
        JsonElement newest = Measured()[^1];
        JsonElement limits = newest.GetProperty("limits");
        int held = newest.GetProperty("numbers").EnumerateObject().Count(n =>
            n.Name.EndsWith("higher_is_better", StringComparison.Ordinal)
                ? n.Value.GetDouble() >= limits.GetProperty(n.Name).GetDouble()
                : n.Value.GetDouble() <= limits.GetProperty(n.Name).GetDouble());
        string count = newest.GetProperty("numbers").EnumerateObject().Count().ToString(CultureInfo.InvariantCulture);
        Assert.Equal($"{held} of {count} at or under their limit on {newest.GetProperty("version").GetString()}", FenceLines("quality-tiles")[0]);
        Assert.StartsWith($"quality numbers held | {held} | of {count} |", FenceLines("quality-lede")[0], StringComparison.Ordinal);
    }

    [Fact]
    public void CodeRabbit_s_readouts_add_up_to_the_table_folded_under_them()
    {
        string[] readouts = FenceLines("quality-readouts");
        string[] rows = [.. Regex.Matches(Page, @"^\| (\d+\.\d+\.\d+\.\d+) \|.*$", RegexOptions.Multiline).Select(m => m.Value)];
        int findings = rows.Sum(row => Regex.Matches(row.Split(" | ")[3], @"(\d+) \w+").Sum(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)));
        Assert.StartsWith($"Versions reviewed | {rows.Count(row => row.Split(" | ")[2] == "full")} | of {rows.Length} |", readouts[0], StringComparison.Ordinal);
        Assert.StartsWith($"Findings | {findings.ToString("#,0", CultureInfo.InvariantCulture)} |", readouts[1], StringComparison.Ordinal);
        int fixedCount = rows.Sum(row => int.TryParse(row.Split(" | ")[4], out int n) ? n : 0);
        int unread = rows.Count(row => row.Split(" | ")[4].Trim() == "" || row.Split(" | ")[5].Trim() == "");
        Assert.StartsWith($"Fixed | {fixedCount} | | in that version or the next, {unread} not read yet", readouts[2], StringComparison.Ordinal);
    }

    [Fact]
    public void The_rulebook_s_six_cards_are_one_fence_line_each_with_numbers_that_add_up()
    {
        string[] groups = FenceLines("quality-groups");
        Assert.Equal(6, groups.Length);
        foreach (string[] cells in groups.Select(line => line.Split(" | ")))
        {
            Assert.Equal(7, cells.Length);
            int[] counts = [.. cells[3..].Select(c => int.Parse(c, CultureInfo.InvariantCulture))];
            Assert.InRange(counts[0], 0, counts[1]);
            Assert.InRange(counts[2], 0, counts[3]);
        }
    }

    [GeneratedRegex(@"^(?<name>[^\n|]+) \| \{\{live:quality-tile (?<key>\w+)\}\}$", RegexOptions.Multiline)]
    private static partial Regex TileName();
}
