using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Records;

/// <summary>
/// Whether a run folder is settled, the two tests the lane's crash hold and the archive job both apply: a DONE note that
/// carries a defect line or an unwritten tracker row may be a send with no record, and a progress file whose run has no
/// DONE note, or a note that says HELD, is a run a person has to settle. Neither runs out on its own.
/// </summary>
public static partial class RunSettlement
{
    /// <summary>True when a DONE note carries a defect line or an unwritten tracker row.</summary>
    /// <param name="note">The DONE note's text.</param>
    public static bool Unsettled(string note) => Defect().IsMatch(note);

    /// <summary>True when a DONE note says the run was held.</summary>
    /// <param name="note">The DONE note's text.</param>
    public static bool Held(string note) => note.Contains("\nHELD: ", StringComparison.Ordinal);

    [GeneratedRegex(@"DEFECT:|TRACKER ROW NOT WRITTEN")]
    private static partial Regex Defect();
}
