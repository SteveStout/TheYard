using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Sheet;

/// <summary>
/// Which master rows the Sheet lacks and may take now (sheet-procedure.md, "SHEET COUNT IS
/// DISPLAY-ONLY"): Sheet row N+1 holds the master's Nth data row in Count order, column A is the
/// display number 1..N, and the next rows are appended numbered from N+1. The rules are the ones the
/// records run applied by hand. The Sheet must still line up with the master at its tail; a row the
/// tracker gate bans, or whose Communication is not a channel name, stops the append there, because
/// the Sheet is in Count order and every later row waits behind it.
/// </summary>
public static partial class SheetPlan
{
    /// <summary>How many of the Sheet's last rows are compared with the master before anything is added.</summary>
    public const int TailChecked = 5;

    /// <summary>
    /// The plan for one pass.
    /// </summary>
    /// <param name="master">The master's data rows (no header), each Count, Person, Company, Communication, Date, Phone/Email, Notes.</param>
    /// <param name="grid">Sheet1 as read, the header row first.</param>
    public static SheetPlanResult Plan(IReadOnlyList<string[]> master, IReadOnlyList<IReadOnlyList<string>> grid)
    {
        var ordered = master
            .Where(row => row.Length >= 7 && int.TryParse(row[0].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .OrderBy(row => int.Parse(row[0].Trim(), CultureInfo.InvariantCulture))
            .ToList();
        var sheet = grid.Skip(1).Where(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell))).ToList();
        int n = sheet.Count;
        if (n > ordered.Count)
        {
            return SheetPlanResult.Stop(n, ordered.Count, null, $"the Sheet holds {n} rows and the master {ordered.Count}: the Sheet is ahead of the master, so nothing is added");
        }
        for (int i = 0; i < n; i++)
        {
            if (Cell(sheet[i], 0) != (i + 1).ToString(CultureInfo.InvariantCulture))
            {
                return SheetPlanResult.Stop(n, ordered.Count, null, $"Sheet row {i + 2} has display number '{Cell(sheet[i], 0)}' where {i + 1} belongs: the numbering is not 1..N, so nothing is added");
            }
        }
        for (int i = Math.Max(0, n - TailChecked); i < n; i++)
        {
            var row = ordered[i];
            if (!Same(Cell(sheet[i], 1), row[1]) || !Same(Cell(sheet[i], 2), row[2]) || !Same(Cell(sheet[i], 4), row[4]))
            {
                return SheetPlanResult.Stop(n, ordered.Count, null, $"Sheet display {i + 1} ({Cell(sheet[i], 1)}, {Cell(sheet[i], 2)}, {Cell(sheet[i], 4)}) is not master Count {row[0]} ({row[1]}, {row[2]}, {row[4]}): the two records have drifted, so nothing is added");
            }
        }
        var appends = new List<IReadOnlyList<string>>();
        var counts = new List<string>();
        for (int i = n; i < ordered.Count; i++)
        {
            var row = ordered[i];
            string? why = TrackerGate.Banned(row) is { } banned ? $"Count {row[0]} is banned by the tracker gate: {banned}"
                : !IsChannel(row[3]) ? $"Count {row[0]}'s Communication is not a channel name: '{Short(row[3])}'"
                : null;
            if (why is not null)
            {
                return new SheetPlanResult(n, ordered.Count, appends, counts, int.Parse(row[0].Trim(), CultureInfo.InvariantCulture), why);
            }
            appends.Add([(i + 1).ToString(CultureInfo.InvariantCulture), row[1], row[2], row[3], row[4], row[5], row[6]]);
            counts.Add(row[0].Trim());
        }
        return new SheetPlanResult(n, ordered.Count, appends, counts, null, null);
    }

    /// <summary>
    /// True when a Communication cell names a channel (LinkedIn, Email, Phone, an ATS and the like) and
    /// is not a sentence: it opens with a channel word, holds no colon and is at most forty characters.
    /// Anything else stops the append for a person or a lane to fix, never for the mirror to guess.
    /// </summary>
    /// <param name="cell">The Communication cell.</param>
    public static bool IsChannel(string? cell)
    {
        string value = (cell ?? string.Empty).Trim();
        return value.Length is > 0 and <= 40 && !value.Contains(':', StringComparison.Ordinal) && Channel().IsMatch(value) && !Narrative().IsMatch(value);
    }

    /// <summary>The cell at a column, or empty when the grid's row is shorter.</summary>
    private static string Cell(IReadOnlyList<string> row, int at) => at < row.Count ? (row[at] ?? string.Empty).Trim() : string.Empty;

    /// <summary>Two cells read as the same text: trimmed, case kept.</summary>
    private static bool Same(string sheet, string? master) => string.Equals(sheet.Trim(), (master ?? string.Empty).Trim(), StringComparison.Ordinal);

    private static string Short(string? cell) => (cell ?? string.Empty).Length <= 50 ? cell ?? string.Empty : cell![..50] + "...";

    [GeneratedRegex(@"^(LinkedIn|LinkedId|Email|E-mail|Gmail|Company (Website|Email)|Phone|Video|Interview|Screening|Calendly|Application|Web|Greenhouse|Ashby|Lever|Workday|Workable|JazzHR|Rippling|iCIMS|Jobvite|Dayforce|SuccessFactors|Eightfold|Breezy|Paylocity|Avature|Gem|Dice|Oracle|ADP|SmartRecruiters|BambooHR|Teamtailor|Recruitee|Wellfound|Indeed|ZipRecruiter|Taleo|UKG|Paycom|Pinpoint|Comeet|Personio|Jobscore|Hired|Otta|Built In)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Channel();

    [GeneratedRegex(@"\b(sent|received|drafted|sweep|page \d|replied|by (steve|claude))\b", RegexOptions.IgnoreCase)]
    private static partial Regex Narrative();
}

/// <summary>What one pass would add, and where it stopped if it stopped.</summary>
/// <param name="SheetRows">The Sheet's data rows when read.</param>
/// <param name="MasterRows">The master's data rows when read.</param>
/// <param name="Appends">The rows to add, each display number then Person, Company, Communication, Date, Phone/Email, Notes.</param>
/// <param name="Counts">The master Count of each row to add, in the same order.</param>
/// <param name="StoppedAt">The Count the append stopped at, or null.</param>
/// <param name="Reason">Why it stopped, in a sentence, or null.</param>
public sealed record SheetPlanResult(int SheetRows, int MasterRows, IReadOnlyList<IReadOnlyList<string>> Appends, IReadOnlyList<string> Counts, int? StoppedAt, string? Reason)
{
    /// <summary>A plan that adds nothing, with the reason.</summary>
    public static SheetPlanResult Stop(int sheet, int master, int? at, string reason) => new(sheet, master, [], [], at, reason);

    /// <summary>True when nothing stopped the plan and nothing is missing.</summary>
    public bool InStep => Reason is null && SheetRows + Appends.Count == MasterRows;
}
