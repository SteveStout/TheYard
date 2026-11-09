using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Gates;

/// <summary>
/// The LinkedIn ceilings, read from the files that own them on every run, so a changed number takes effect with no
/// build: the daily table in outreach-limits.md and the invite pause in CONVENTIONS.md. A number that cannot be read
/// throws, and the run treats that as a closed gate: no ceiling, no send. The table's 14 is read, not the later
/// section's 20, because the lanes run at the table and the lower number is the safe reading of a file that says both.
/// </summary>
/// <param name="InvitesPerDay">Connection requests in one day.</param>
/// <param name="MessagesPerDay">Messages in one day.</param>
/// <param name="ActionsPerDay">Invites, messages and replies in one day.</param>
/// <param name="InvitesPerWeek">Connection requests in a rolling seven days.</param>
/// <param name="InvitePause">The rolling count at which no invite goes.</param>
public sealed partial record Ceilings(int InvitesPerDay, int MessagesPerDay, int ActionsPerDay, int InvitesPerWeek, int InvitePause)
{
    /// <summary>The ceilings out of the two files.</summary>
    /// <param name="limits">outreach-limits.md.</param>
    /// <param name="conventions">CONVENTIONS.md.</param>
    public static Ceilings Read(string limits, string conventions)
    {
        Match invites = Invites().Match(limits);
        Match messages = Messages().Match(limits);
        Match actions = Actions().Match(limits);
        Match pause = Pause().Match(conventions);
        if (!invites.Success || !messages.Success || !actions.Success || !pause.Success)
        {
            throw new FormatException("a ceiling could not be read from outreach-limits.md or CONVENTIONS.md");
        }
        return new Ceilings(
            int.Parse(invites.Groups[1].Value),
            int.Parse(messages.Groups[1].Value),
            int.Parse(actions.Groups[1].Value),
            int.Parse(invites.Groups[2].Value),
            int.Parse(pause.Groups[1].Value));
    }

    [GeneratedRegex(@"connection requests\*\*\s*\|\s*\*\*(\d+) per day\*\*, and \*\*(\d+) per rolling 7 days\*\*")]
    private static partial Regex Invites();

    [GeneratedRegex(@"messages to existing connections\*\*\s*\|\s*\*\*(\d+) per day\*\*")]
    private static partial Regex Messages();

    [GeneratedRegex(@"ALL LinkedIn actions, combined\*\*\s*\|\s*\*\*(\d+) per day\*\*")]
    private static partial Regex Actions();

    [GeneratedRegex(@"At or over (\d+), send NO invite")]
    private static partial Regex Pause();
}

/// <summary>The day's LinkedIn counts from the tracker: the floor, because the live count on the sent page governs.</summary>
/// <param name="InvitesWeek">Invites in the rolling seven days.</param>
/// <param name="InvitesToday">Invites today.</param>
/// <param name="MessagesToday">Messages today.</param>
/// <param name="ActionsToday">Every LinkedIn action today.</param>
public sealed record OutreachCounts(int InvitesWeek, int InvitesToday, int MessagesToday, int ActionsToday)
{
    /// <summary>The counts over the master rows and any spool row not merged yet.</summary>
    /// <param name="rows">Seven-cell tracker rows.</param>
    /// <param name="today">The Central day.</param>
    public static OutreachCounts From(IEnumerable<string[]> rows, DateOnly today)
    {
        int week = 0, invites = 0, messages = 0;
        foreach (string[] row in rows)
        {
            if (TrackerKinds.Day(row) is not { } day)
            {
                continue;
            }
            int age = today.DayNumber - day.DayNumber;
            bool invite = TrackerKinds.IsInvite(row);
            if (invite && age is >= 0 and < 7)
            {
                week++;
            }
            if (age == 0)
            {
                invites += invite ? 1 : 0;
                messages += TrackerKinds.IsMessage(row) ? 1 : 0;
            }
        }
        return new OutreachCounts(week, invites, messages, invites + messages);
    }

    /// <summary>How many more invites may go today: the day's ceiling, the combined ceiling and one under the pause.</summary>
    /// <param name="ceilings">The ceilings.</param>
    public int InviteRoom(Ceilings ceilings) =>
        Math.Max(0, Min(ceilings.InvitesPerDay - InvitesToday, ceilings.ActionsPerDay - ActionsToday, ceilings.InvitePause - 1 - InvitesWeek));

    /// <summary>How many more messages may go today, after the invites already on the day's worklist.</summary>
    /// <param name="ceilings">The ceilings.</param>
    /// <param name="plannedInvites">Invites this run has used or holds on its worklist.</param>
    public int MessageRoom(Ceilings ceilings, int plannedInvites) =>
        Math.Max(0, Math.Min(ceilings.MessagesPerDay - MessagesToday, ceilings.ActionsPerDay - ActionsToday - plannedInvites));

    private static int Min(int a, int b, int c) => Math.Min(a, Math.Min(b, c));
}
