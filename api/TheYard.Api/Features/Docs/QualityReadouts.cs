using System.Globalization;
using System.Text.Json;

namespace TheYard.Api.Features.Docs;

/// <summary>
/// The Quality page's readouts and tiles (ADR-130, the addendum on the page as its picture): the four readouts over
/// the slip, one tile for each of the fifteen numbers, and CodeRabbit's four counts. Every one is read, at request
/// time, from data/quality-history.json or data/coderabbit-findings.json, as <see cref="QualityCounts"/> reads the
/// rest of the page. A number joins the ratchet at the first row whose limits name it, so each number's join is its
/// own, read from the history and never written here.
/// </summary>
public static partial class QualityReadouts
{
    /// <summary>The measures this class answers, by the first word of the placeholder; the lede's and the slip's are in QualityReadouts.Lede.cs.</summary>
    public static readonly string[] Measures = ["quality-held", "quality-count", "quality-version", "quality-span", "quality-join", "quality-tile", "coderabbit-reviewed", "coderabbit-logged", "coderabbit-first", "coderabbit-findings", "coderabbit-fixed", "coderabbit-wrong", "coderabbit-unread", "tests-passed", "coverage-whole", "coderabbit-since", "quality-worst", "quality-worst-days", "quality-at-ratchet", "quality-slip-span", "quality-ratchet-day", "quality-charted"];

    /// <summary>One measure, as text; <paramref name="argument"/> is the number's key where the measure needs one.</summary>
    public static string Measure(string kind, string? argument, string repoRoot)
    {
        try
        {
            return kind switch
            {
                "quality-held" => Whole(Held(History.Read(repoRoot))),
                "quality-count" => Whole(History.Read(repoRoot).Newest.GetProperty("numbers").EnumerateObject().Count()),
                "quality-version" => History.Read(repoRoot).Newest.GetProperty("version").GetString() ?? "",
                "quality-span" => Span(History.Read(repoRoot)),
                "quality-join" when argument is not null => Format(History.Read(repoRoot).Join(argument).Value),
                "quality-tile" when argument is not null => Tile(History.Read(repoRoot), argument).Line,
                "tests-passed" => TestsPassed(repoRoot),
                "coverage-whole" when argument is "lines" or "branches" => CoverageWhole(History.Read(repoRoot), argument),
                "coderabbit-since" => ReviewSince(repoRoot),
                "quality-worst" or "quality-worst-days" or "quality-at-ratchet" or "quality-slip-span" when argument is not null => Slip(History.Read(repoRoot), kind, argument),
                "quality-ratchet-day" => Slip(History.Read(repoRoot), kind, null),
                "quality-charted" => Charted(History.Read(repoRoot)),
                _ when kind.StartsWith("coderabbit-", StringComparison.Ordinal) => Review(repoRoot, kind),
                _ => $"(no live measure named '{kind}{(argument is null ? "" : " " + argument)}')",
            };
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException or JsonException)
        {
            return $"(the data for '{kind} {argument}' is not in this build)";
        }
    }

    /// <summary>A number that may only rise, by the suffix the scanner names it with.</summary>
    private static bool Rises(string key) => key.EndsWith("higher_is_better", StringComparison.Ordinal);

    /// <summary>How many of the newest row's numbers sit at or under their limit (at or over, for one that may only rise).</summary>
    internal static int Held(History history)
    {
        JsonElement newest = history.Newest;
        JsonElement limits = newest.GetProperty("limits");
        return newest.GetProperty("numbers").EnumerateObject().Count(number =>
            limits.TryGetProperty(number.Name, out JsonElement limit) && limit.ValueKind == JsonValueKind.Number
            && (Rises(number.Name) ? number.Value.GetDouble() >= limit.GetDouble() : number.Value.GetDouble() <= limit.GetDouble()));
    }

    /// <summary>The first and last day the scanner measured, as the page writes a day: "Oct 15 to Nov 20".</summary>
    private static string Span(History history) => $"{Day(history.Measured[0])} to {Day(history.Newest)}";

    /// <summary>A row's day as the page writes one: "Nov 20".</summary>
    private static string Day(JsonElement row) =>
        DateOnly.ParseExact(row.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture).ToString("MMM d", CultureInfo.InvariantCulture);

    /// <summary>
    /// One tile, as the fence line the page draws it from: the value now, its kind (fall, rise or zero), the bar's
    /// share in per cent and the note. A falling number's bar is now against its value at the join; a rising one's is
    /// the value at the join against now, the rest of the bar its gain; a number zero at the join and today is held.
    /// </summary>
    internal static (string Kind, int Share, string Note, string Line) Tile(History history, string key)
    {
        double now = history.Newest.GetProperty("numbers").GetProperty(key).GetDouble();
        var (version, joined) = history.Join(key);
        string at = version == history.FirstRatchet ? "at the ratchet" : $"when it joined at {version}";
        (string kind, int share, string note) = (Rises(key), joined.GetDouble(), now) switch
        {
            (true, double then, _) => ("rise", now <= 0 ? 0 : (int)Math.Round(100 * then / now), $"{Format(joined)} {at}, may only rise"),
            (false, 0, 0) when history.Peak(key) is var peak && peak > 0 => ("zero", 0, $"peaked at {Format(peak)} before the ratchet; zero since"),
            (false, 0, 0) => ("zero", 0, $"zero {at}, zero today"),
            (false, double then, _) => ("fall", now <= 0 ? 0 : Math.Max(2, (int)Math.Round(100 * now / then)), $"{Format(joined)} {at}, may only fall"),
        };
        string value = Format(history.Newest.GetProperty("numbers").GetProperty(key));
        return (kind, share, note, $"{value} | {kind} | {share.ToString(CultureInfo.InvariantCulture)} | {note}");
    }

    // #region coderabbit
    /// <summary>
    /// CodeRabbit's four counts, from the same log its table is drawn from: versions reviewed in full of versions
    /// logged, findings across those reviews, fixed and answered as wrong. An empty cell is a version not read yet:
    /// it adds nothing to a count and is counted on its own (coderabbit-unread), never as a zero.
    /// </summary>
    private static string Review(string repoRoot, string kind)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "data", "coderabbit-findings.json")));
        var rows = document.RootElement.GetProperty("rows").EnumerateArray().ToList();
        return kind switch
        {
            "coderabbit-reviewed" => Whole(rows.Count(r => r.GetProperty("reviewed").GetString() == "full")),
            "coderabbit-logged" => Whole(rows.Count),
            "coderabbit-first" => rows[0].GetProperty("version").GetString() ?? "",
            "coderabbit-findings" => Whole(rows.Sum(r => r.GetProperty("findings").EnumerateObject().Sum(f => f.Value.GetInt32()))),
            "coderabbit-fixed" => Whole(rows.Sum(r => Read(r, "fixed") ?? 0)),
            "coderabbit-wrong" => Whole(rows.Sum(r => Read(r, "answered_as_wrong") ?? 0)),
            "coderabbit-unread" => Whole(rows.Count(r => Read(r, "fixed") is null || Read(r, "answered_as_wrong") is null)),
            _ => $"(no live measure named '{kind}')",
        };
    }

    /// <summary>A disposition the log has read, or null for a cell not read yet.</summary>
    private static int? Read(JsonElement row, string key) =>
        row.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
    // #endregion coderabbit

    /// <summary>A whole count as the site writes one: 4,555.</summary>
    private static string Whole(int value) => value.ToString("#,0", CultureInfo.InvariantCulture);

    /// <summary>A reading as the page writes it: a whole count with its comma, a ratio to two places at most.</summary>
    private static string Format(JsonElement value) =>
        value.TryGetInt32(out int whole) ? Whole(whole) : value.GetDouble().ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>A reading held as a double, written the same way.</summary>
    private static string Format(double value) =>
        value == Math.Floor(value) ? Whole((int)value) : value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>The quality history, read once for a measure: its scanned rows, the newest and the first ratchet's version.</summary>
    internal sealed class History(List<JsonElement> rows, string firstRatchet)
    {
        /// <summary>Every row, pre-scan rows first, in the order the file keeps them.</summary>
        public List<JsonElement> Rows { get; } = rows;

        /// <summary>The version the ratchet went in at, as the file names it.</summary>
        public string FirstRatchet { get; } = firstRatchet;

        /// <summary>The rows the scanner measured, one per shipped version.</summary>
        public List<JsonElement> Measured => [.. Rows.Where(r => r.GetProperty("source").GetString() == "quality.py")];

        /// <summary>The newest measured row.</summary>
        public JsonElement Newest => Measured[^1];

        /// <summary>Reads data/quality-history.json under <paramref name="repoRoot"/>.</summary>
        public static History Read(string repoRoot)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repoRoot, "data", "quality-history.json")));
            return new([.. document.RootElement.GetProperty("rows").EnumerateArray().Select(r => r.Clone())],
                document.RootElement.GetProperty("first_ratchet_version").GetString() ?? "");
        }

        /// <summary>The version a number joined the ratchet at, the first row whose limits name it, and its value there.</summary>
        public (string Version, JsonElement Value) Join(string key)
        {
            JsonElement row = Measured.First(r => r.GetProperty("limits").ValueKind == JsonValueKind.Object && r.GetProperty("limits").TryGetProperty(key, out _));
            return (row.GetProperty("version").GetString() ?? "", row.GetProperty("numbers").GetProperty(key));
        }

        /// <summary>The highest reading a number ever had, on any row.</summary>
        public double Peak(string key) =>
            Rows.Select(r => r.GetProperty("numbers")).Where(n => n.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Number)
                .Max(n => n.GetProperty(key).GetDouble());
    }
}
