using System.Globalization;
using System.Text;
using JobHunt.Functions.Merge;

namespace JobHunt.Functions.Sheet;

/// <summary>
/// One pass of the Sheet mirror: the master CSV on the share against Sheet1, the plan SheetPlan makes,
/// written to sheet-mirror/last-plan.tsv every pass and logged to sheet-mirror/mirror.log. In shadow
/// it stops there, so its plan can be set beside the records run's sheet-plan.tsv; on, it appends the
/// planned rows, reads the grid back and checks the count, the numbering and every added cell.
/// It never edits a cell: the only write it can make to the Sheet is ISheetGrid.AppendAsync.
/// </summary>
public sealed class SheetMirror(IMergeStore store, ISheetGrid grid, TimeProvider time)
{
    /// <summary>The mirror's folder on the share.</summary>
    public const string Folder = "sheet-mirror";

    /// <summary>One line a pass.</summary>
    public const string Log = Folder + "/mirror.log";

    /// <summary>The last pass's plan, replaced every pass.</summary>
    public const string LastPlan = Folder + "/last-plan.tsv";

    /// <summary>Runs one pass and returns its log line.</summary>
    /// <param name="mode">Shadow plans only; On appends and checks.</param>
    /// <param name="ct">Stops the pass.</param>
    public async Task<string> RunAsync(SheetMode mode, CancellationToken ct)
    {
        var master = TrackerCsvText.Parse(TrackerCsvText.Decode(await store.ReadAsync(MergePaths.Master, ct)));
        var before = await grid.ReadAsync(ct);
        var plan = SheetPlan.Plan(master, before);
        await store.EnsureFolderAsync(Folder, ct);
        await store.WriteAsync(LastPlan, TrackerCsvText.Encode(Tsv(plan, mode, Now())), ct);
        string line = $"{(mode == SheetMode.On ? "ON" : "SHADOW")} sheet {plan.SheetRows} master {plan.MasterRows} add {plan.Appends.Count}"
            + (plan.Counts.Count > 0 ? $" (Counts {plan.Counts[0]} to {plan.Counts[^1]})" : "")
            + (plan.Reason is null ? "" : $" STOPPED at {plan.StoppedAt?.ToString(CultureInfo.InvariantCulture) ?? "the start"}: {plan.Reason}");
        if (mode == SheetMode.On && plan.Appends.Count > 0)
        {
            await grid.AppendAsync(plan.Appends, ct);
            line += " | " + Check(plan, await grid.ReadAsync(ct));
        }
        await store.AppendAsync(Log, TrackerCsvText.Encode($"{Now():yyyy-MM-dd HH:mm:ss} {line}\r\n"), ct);
        return line;
    }

    /// <summary>
    /// The read-back after an append: the Sheet holds exactly the rows it held plus the added ones,
    /// numbered 1..N without a gap, and each added row reads back cell for cell as it was sent.
    /// </summary>
    /// <param name="plan">The plan that was appended.</param>
    /// <param name="after">Sheet1 read after the append, the header first.</param>
    public static string Check(SheetPlanResult plan, IReadOnlyList<IReadOnlyList<string>> after)
    {
        var rows = after.Skip(1).Where(row => row.Any(cell => !string.IsNullOrWhiteSpace(cell))).ToList();
        int want = plan.SheetRows + plan.Appends.Count;
        if (rows.Count != want)
        {
            return $"MISMATCH: the Sheet reads {rows.Count} rows after the append, {want} expected";
        }
        for (int i = 0; i < rows.Count; i++)
        {
            if (Cell(rows[i], 0) != (i + 1).ToString(CultureInfo.InvariantCulture))
            {
                return $"MISMATCH: display number {Cell(rows[i], 0)} at Sheet row {i + 2}";
            }
        }
        for (int k = 0; k < plan.Appends.Count; k++)
        {
            var sent = plan.Appends[k];
            var read = rows[plan.SheetRows + k];
            for (int c = 0; c < sent.Count; c++)
            {
                if (!string.Equals(Cell(read, c), (sent[c] ?? string.Empty).Trim(), StringComparison.Ordinal))
                {
                    return $"MISMATCH: Count {plan.Counts[k]} column {(char)('A' + c)} reads back differently";
                }
            }
        }
        return $"VERIFIED {want} rows, 1..{want}, the {plan.Appends.Count} added read back cell for cell";
    }

    /// <summary>The plan as a tab-separated file: a header line of facts, then one line a row to add.</summary>
    public static string Tsv(SheetPlanResult plan, SheetMode mode, DateTime local)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"# {local:yyyy-MM-dd HH:mm:ss} Central, mode {mode}, sheet {plan.SheetRows}, master {plan.MasterRows}, add {plan.Appends.Count}");
        text.Append(plan.Reason is null ? "\r\n" : $", stopped: {plan.Reason}\r\n");
        text.Append("Count\tDisplay\tPerson\tCompany\tCommunication\tDate\tPhone/Email\tNotes\r\n");
        for (int k = 0; k < plan.Appends.Count; k++)
        {
            text.Append(plan.Counts[k]).Append('\t').Append(string.Join('\t', plan.Appends[k].Select(Flat))).Append("\r\n");
        }
        return text.ToString();
    }

    private DateTime Now() => CentralClock.Local(time.GetUtcNow());

    private static string Cell(IReadOnlyList<string> row, int at) => at < row.Count ? (row[at] ?? string.Empty).Trim() : string.Empty;

    private static string Flat(string? cell) => (cell ?? string.Empty).Replace('\t', ' ').Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
}

/// <summary>How the mirror runs, from the SHEET_MIRROR setting.</summary>
public enum SheetMode
{
    /// <summary>The mirror does nothing.</summary>
    Off,

    /// <summary>The mirror reads both records and writes its plan, and adds nothing to the Sheet.</summary>
    Shadow,

    /// <summary>The mirror appends its plan and checks the read-back.</summary>
    On,
}
