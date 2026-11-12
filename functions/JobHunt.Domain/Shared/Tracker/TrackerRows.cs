using System.Text.RegularExpressions;

namespace JobHunt.Domain.Shared.Tracker;

/// <summary>
/// What a tracker row says, the rules every slice reads a row by: the ATS sweep, the application cap, the leads filter
/// and the campaign's live-thread rule all ask whether a row records an application, and the funnel line and the
/// pounce's clock both ask whether a row reached a person, so each answer lives here once rather than in one slice
/// that the others reach into.
/// </summary>
public static partial class TrackerRows
{
    /// <summary>How far into a note its NOT APPLIED or NOT SUBMITTED is read: the status a lane writes first.</summary>
    public const int OpeningLength = 80;

    /// <summary>
    /// True when a master row records an application: submitted or applied in its note, on any channel, unless the
    /// note opens by saying it was not ("NOT APPLIED, BLOCKER", "NOT APPLIED YET", "NOT SUBMITTED", the leaders lane's
    /// NOT_APPLIED). Only the opening is read for that, because a real application's long note can mention an earlier
    /// draft that sat NOT SUBMITTED, and holding such a row out would under-count RULE 50's cap.
    /// </summary>
    /// <param name="row">The row's seven cells.</param>
    public static bool IsApplication(string[] row)
    {
        if (row.Length < 7)
        {
            return false;
        }
        string note = row[6].TrimStart();
        return !note.StartsWith("REJECTION", StringComparison.OrdinalIgnoreCase)
            && !NotApplied().IsMatch(note[..Math.Min(OpeningLength, note.Length)])
            && Applied().IsMatch(note);
    }

    /// <summary>
    /// The channel a row's opening phrase sent on (invites, inmails, campaign, messages or emails), or null when the row
    /// is not a send to a person: the type every lane writes first in Notes, read up to its SENT, with a NO, NOT or
    /// DECLINE in it read as no send at all.
    /// </summary>
    /// <param name="notes">The Notes cell, leading space removed.</param>
    /// <param name="communication">The row's Communication cell.</param>
    public static string? Channel(string notes, string communication)
    {
        if (Sent().Match(notes) is not { Success: true } sent || Refused().IsMatch(sent.Value))
        {
            return null;
        }
        string phrase = sent.Value;
        return phrase.Contains("CONNECTION REQUEST", StringComparison.Ordinal) ? "invites"
            : phrase.Contains("INMAIL", StringComparison.Ordinal) ? "inmails"
            : phrase.Contains("CAMPAIGN", StringComparison.Ordinal) ? "campaign"
            : communication.Trim().StartsWith("LinkedIn", StringComparison.OrdinalIgnoreCase) ? "messages"
            : "emails";
    }

    [GeneratedRegex(@"\b(submitted|applied|application submitted|easy apply)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Applied();

    [GeneratedRegex(@"\bNOT (APPLIED|SUBMITTED)\b", RegexOptions.IgnoreCase)]
    private static partial Regex NotApplied();

    [GeneratedRegex(@"^(?:[A-Z][A-Z\-]*[ ,]+){0,8}?SENT\b")]
    private static partial Regex Sent();

    [GeneratedRegex(@"\b(?:NO|NOT|DECLINE)\b")]
    private static partial Regex Refused();
}
