using System.Text.RegularExpressions;

namespace JobHunt.Domain.Shared.Tracker;

/// <summary>
/// What a tracker row says, the rules every slice reads a row by: the ATS sweep, the application cap, the leads filter
/// and the campaign's live-thread rule all ask whether a row records an application, so the answer lives here once
/// rather than in one slice that the others reach into.
/// </summary>
public static partial class TrackerRows
{
    /// <summary>True when a master row records an application: submitted or applied in its note, on any channel.</summary>
    /// <param name="row">The row's seven cells.</param>
    public static bool IsApplication(string[] row) =>
        row.Length >= 7 && !row[6].TrimStart().StartsWith("REJECTION", StringComparison.OrdinalIgnoreCase) && Applied().IsMatch(row[6]);

    [GeneratedRegex(@"\b(submitted|applied|application submitted|easy apply)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Applied();
}
