using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Gates;

/// <summary>
/// What kind of LinkedIn row a tracker row is, read from its Notes the way outreach-limits.md says to count: the
/// channel column says LinkedIn for almost everything, so the opening words decide. A row reads as a message unless it
/// is an invite, an inbound, a rejection or an acceptance, because counting too many holds a send back and counting
/// too few is how a second touch goes out. Only the opening words are read: a message's body says "applied" as often
/// as not, and reading the whole note for that word hid sent messages from Rule 25.
/// </summary>
public static partial class TrackerKinds
{
    /// <summary>The seven cells of a spool row of six, with the Count blank, so master and spool read alike.</summary>
    /// <param name="cells">A row's cells.</param>
    public static string[] Seven(string[] cells) => cells.Length == 6 ? ["", .. cells] : cells;

    /// <summary>The row's date, or null.</summary>
    /// <param name="row">Seven cells.</param>
    public static DateOnly? Day(string[] row) =>
        row.Length >= 7 && DateOnly.TryParseExact(row[4].Trim(), ["M/d/yyyy", "MM/dd/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    /// <summary>True for a row on a LinkedIn channel.</summary>
    /// <param name="row">Seven cells.</param>
    public static bool IsLinkedIn(string[] row) =>
        row.Length >= 7 && row[3].Contains("LinkedIn", StringComparison.OrdinalIgnoreCase) && !row[3].Contains("Easy Apply", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for a connection request this side sent.</summary>
    /// <param name="row">Seven cells.</param>
    public static bool IsInvite(string[] row) =>
        IsLinkedIn(row) && !Inbound(row) && (row[3].Contains("invite", StringComparison.OrdinalIgnoreCase) || Invite().IsMatch(Head(row)));

    /// <summary>True for a LinkedIn message this side sent.</summary>
    /// <param name="row">Seven cells.</param>
    public static bool IsMessage(string[] row) =>
        IsLinkedIn(row) && !IsInvite(row) && !Inbound(row) && Head(row).Length > 0 && !NoMessage().IsMatch(Head(row));

    private static bool Inbound(string[] row) => NotOurs().IsMatch(Head(row));

    private static string Head(string[] row) => row[6].TrimStart()[..Math.Min(80, row[6].TrimStart().Length)];

    [GeneratedRegex(@"CONNECTION REQUEST|SCOPED INVITE|INVITE SENT|CONNECT NOTE", RegexOptions.IgnoreCase)]
    private static partial Regex Invite();

    [GeneratedRegex(@"^\W*(NOT APPLIED|APPLIED|SUBMITTED|APPLICATION SUBMITTED|SKIPPED|DROPPED)", RegexOptions.IgnoreCase)]
    private static partial Regex NoMessage();

    [GeneratedRegex(@"^\W*(INBOUND|REJECTION|RECEIPT|AUTO-REPLY|CONNECTION ACCEPTED|ACCEPTED|BOUNCE|REPLY RECEIVED)", RegexOptions.IgnoreCase)]
    private static partial Regex NotOurs();
}
