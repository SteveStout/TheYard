using System.Globalization;
using System.Text.Json;

namespace TheYard.Api.Features.Docs;

/// <summary>
/// The Quality page's first two cards (ADR-130, the addendum on the page in two layers): the measures the lede's four
/// readouts and the slip's caption read, each from the file the build writes and never typed. The tests readout is
/// the landing's own figure, each suite once; the caption tells the slip in words from the history of one number.
/// Its own file so the class stays under the line cap.
/// </summary>
public static partial class QualityReadouts
{
    /// <summary>
    /// The suites the landing counts once (COUNTED_ONCE in src/lib/landingProof.ts): the store passes rerun the same
    /// tests on the other store. QualityLedeTests holds this list equal to the landing's.
    /// </summary>
    public static readonly string[] CountedOnce = ["vitest", "xunit-sqlite", "xunit-live", "browser-sqlite"];

    private static readonly string[] Words = ["zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten"];

    // #region lede
    /// <summary>The tests passed on this version, as the landing prints them: each counted suite's passes, summed.</summary>
    private static string TestsPassed(string repoRoot)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "data", "test-results.json")));
        return Whole(document.RootElement.GetProperty("suites").EnumerateArray()
            .Where(suite => CountedOnce.Contains(suite.GetProperty("id").GetString()))
            .Sum(suite => suite.GetProperty("passed").GetInt32()));
    }

    /// <summary>The newest coverage reading of <paramref name="which"/> as a whole per cent: 94.3 reads 94.</summary>
    private static string CoverageWhole(History history, string which) =>
        Whole((int)Math.Round(history.Measured.Last(r => r.TryGetProperty("coverage", out JsonElement c) && c.ValueKind == JsonValueKind.Object)
            .GetProperty("coverage").GetProperty(which).GetDouble(), MidpointRounding.AwayFromZero));

    /// <summary>The day of CodeRabbit's first logged review, as the page writes a day: "Nov 10".</summary>
    private static string ReviewSince(string repoRoot)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "data", "coderabbit-findings.json")));
        string at = document.RootElement.GetProperty("rows")[0].GetProperty("at").GetString()!;
        return DateOnly.ParseExact(at[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("MMM d", CultureInfo.InvariantCulture);
    }
    // #endregion lede

    // #region slip
    /// <summary>
    /// One word of the slip's caption for <paramref name="key"/>: the worst reading, the days from the first reading to
    /// it in words, the reading on the version the ratchet went in at, the time from the first reading to that day in
    /// words ("two weeks"), and that day ("29 October").
    /// </summary>
    private static string Slip(History history, string kind, string? key)
    {
        if (kind == "quality-ratchet-day")
        {
            return Date(Ratchet(history)).ToString("d MMMM", CultureInfo.InvariantCulture);
        }
        List<JsonElement> readings = [.. history.Rows.Where(r => r.GetProperty("numbers").TryGetProperty(key!, out JsonElement v) && v.ValueKind == JsonValueKind.Number)];
        double Value(JsonElement row) => row.GetProperty("numbers").GetProperty(key!).GetDouble();
        JsonElement worst = Rises(key!) ? readings.MinBy(Value) : readings.MaxBy(Value);
        int Days(JsonElement row) => Date(row).DayNumber - Date(readings[0]).DayNumber;
        return kind switch
        {
            "quality-worst" => Format(Value(worst)),
            "quality-worst-days" => Spelled(Days(worst)),
            "quality-at-ratchet" => Format(Value(Ratchet(history))),
            "quality-slip-span" => Days(Ratchet(history)) % 7 == 0
                ? $"{Spelled(Days(Ratchet(history)) / 7)} week{(Days(Ratchet(history)) == 7 ? "" : "s")}"
                : $"{Spelled(Days(Ratchet(history)))} days",
            _ => $"(no live measure named '{kind}')",
        };
    }

    /// <summary>
    /// The small charts under "Each number over time": every number but the slip's, less those held at zero from their
    /// join to today, which the page names in a sentence instead of a line.
    /// </summary>
    private static string Charted(History history) =>
        Whole(history.Newest.GetProperty("numbers").EnumerateObject()
            .Count(number => number.Name != "async_without_cancellation" && Tile(history, number.Name).Kind != "zero"));

    /// <summary>The row of the version the ratchet went in at.</summary>
    private static JsonElement Ratchet(History history) =>
        history.Measured.First(r => r.GetProperty("version").GetString() == history.FirstRatchet);

    private static DateOnly Date(JsonElement row) =>
        DateOnly.ParseExact(row.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>A small count in words, as the caption says it: "four"; past ten, the figure.</summary>
    private static string Spelled(int value) => value >= 0 && value < Words.Length ? Words[value] : Whole(value);
    // #endregion slip
}
