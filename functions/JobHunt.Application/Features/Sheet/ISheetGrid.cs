namespace JobHunt.Application.Features.Sheet;

/// <summary>
/// The tracker's Google Sheet as the mirror sees it: Sheet1 read whole, and rows added at the end.
/// There is no third method on purpose. The Sheet is append only (BOOKKEEPING-LANE.md): nothing
/// here can edit, clear or delete a cell, and SheetMirrorTests holds this port to its two methods.
/// </summary>
public interface ISheetGrid
{
    /// <summary>Sheet1 columns A to G, the header row first, each row as the grid holds it (short rows not padded).</summary>
    Task<IReadOnlyList<IReadOnlyList<string>>> ReadAsync(CancellationToken ct);

    /// <summary>Adds the rows after the last row of Sheet1, as raw text, inserting rows.</summary>
    Task AppendAsync(IReadOnlyList<IReadOnlyList<string>> rows, CancellationToken ct);
}
