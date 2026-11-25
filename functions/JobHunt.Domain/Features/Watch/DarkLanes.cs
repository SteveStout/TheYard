using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Watch;

/// <summary>One sending lane the alarm counts: its key in state.md and on a hold, and its name in plain words.</summary>
/// <param name="Key">"server6am messages", as check_empty_runs.py names it and a hold's "Lane:" line carries it.</param>
/// <param name="Label">What Steve calls it on the card.</param>
public sealed record SendingLane(string Key, string Label);

/// <summary>Everything the alarm reads, off the share, before it judges.</summary>
/// <param name="State">J:\state.md, whose CLOSED rows are each run's own count.</param>
/// <param name="Notes">The DONE notes the pounce rows name, by their path from the share's root.</param>
/// <param name="Holds">The text of every dispatch/held/*.hold.</param>
/// <param name="EmptyRuns">dispatch/EMPTY-RUNS.md, whose "Top reasons" check_empty_runs.py read off the DONE notes.</param>
/// <param name="Tracker">Job_Hunt_Tracker.csv, whose CAMPAIGN EMAIL SENT rows are the campaign's verified sends.</param>
/// <param name="PassLog">campaign/pass.log, whose 8 o'clock lines say whether the campaign had a lead to send.</param>
/// <param name="Queued">The item names in dispatch/ready and dispatch/running.</param>
/// <param name="Proposed">The item names in dispatch/proposed.</param>
/// <param name="SignIn">dispatch/SIGN-IN.md, the claude CLI's sign-in on WickedAwsome as canary.py and dispatch.py last read it, or empty.</param>
public sealed record DarkReads(
    string State,
    IReadOnlyDictionary<string, string> Notes,
    IReadOnlyList<string> Holds,
    string EmptyRuns,
    string Tracker,
    string PassLog,
    IReadOnlySet<string> Queued,
    IReadOnlySet<string> Proposed,
    string SignIn = "");

/// <summary>
/// The dark-lane alarm: a lane that sends nothing must reach Steve as a failure, not as a routine count. For each
/// sending lane it counts the days since its last run with a verified send, from the CLOSED rows
/// check_empty_runs.py reads (and the DONE note a pounce row names); a held day counts as zero. At three days or more a
/// lane with a hold on it, or with work due over those days ("0 sent verified of 12", "43 left out", its own hold), is
/// DARK: one line, first on NOT-WORKING.md and first on the 07:30 card. A lane whose runs were all quiet (nothing due) is
/// not dark on the days alone. The line says why and what is fixing it, never "Needs you: nothing".
/// </summary>
public static partial class DarkLanes
{
    /// <summary>The days without a verified send that make a lane with work due dark.</summary>
    public const int Days = 3;

    /// <summary>How far back the rows are read; a lane with no send in it reads the days since its first run in it.</summary>
    public const int Window = 21;

    /// <summary>The word every alarm line opens with, which the writer replaces each tick and the card reads.</summary>
    public const string Prefix = "DARK: ";

    /// <summary>The sending lanes, in the order the lines are written.</summary>
    public static readonly IReadOnlyList<SendingLane> Lanes =
    [
        new("server6am messages", "LinkedIn messages (6 AM)"),
        new("server6am invites", "LinkedIn invites (6 AM)"),
        new("server6am apply", "Applications (6 AM)"),
        new("leaders", "Leader emails (6:50 AM)"),
        new("campaign", "Campaign emails (8 AM)"),
        new("pounce", "Pounce applications"),
    ];

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The file canary.py and dispatch.py write the claude CLI's sign-in to.</summary>
    public const string SignInPath = "dispatch/SIGN-IN.md";

    /// <summary>
    /// The DARK lines for today: an expired claude sign-in first, as its own cause (every model lane stops on it, and no
    /// lane's own failure explains it, audit1124 follow-ups 1), then one a dark lane, in <see cref="Lanes"/> order.
    /// </summary>
    /// <param name="reads">What the share holds.</param>
    /// <param name="today">The Central day.</param>
    public static IReadOnlyList<string> Lines(DarkReads reads, DateOnly today) =>
        [.. new[] { SignInLine(reads.SignIn) }.Concat(Lanes.Select(l => Line(l, reads, today))).OfType<string>()];

    /// <summary>The sign-in's line when SIGN-IN.md reads "State: expired", else null.</summary>
    /// <param name="signIn">SIGN-IN.md's text, or empty.</param>
    public static string? SignInLine(string signIn)
    {
        if (Field(signIn ?? string.Empty, "State") != "expired")
        {
            return null;
        }
        string since = Field(signIn!, "Since") is { Length: > 0 } at ? $" since {at}" : string.Empty;
        string seen = Field(signIn!, "Seen") is { Length: > 0 } words ? $" (\"{words}\")" : string.Empty;
        return $"{Prefix}the claude sign-in on WickedAwsome: expired{since}{seen}, so every model lane stops: the 6 AM run, leaders, reply drafts, pounce and the canaries. "
            + "Why: the CLI's sign-in expired, not a lane failing. Fix: double-click \"Sign in to Claude\" on the WickedAwsome desktop and type /login.";
    }

    /// <summary>One lane's line, or null when it is not dark.</summary>
    /// <param name="lane">The lane.</param>
    /// <param name="reads">What the share holds.</param>
    /// <param name="today">The Central day.</param>
    public static string? Line(SendingLane lane, DarkReads reads, DateOnly today)
    {
        var runs = lane.Key == "campaign" ? Campaign(reads, today) : Runs(reads.State, reads.Notes, today).Where(r => r.Lane == lane.Key).ToList();
        string? hold = reads.Holds.FirstOrDefault(h => Field(h, "Lane") == lane.Key);
        if (runs.Count == 0 && hold is null)
        {
            return null;
        }
        DateOnly? sent = runs.Where(r => r.Verdict == "done").Select(r => (DateOnly?)r.Day).Max();
        DateOnly from = sent ?? runs.Select(r => r.Day).DefaultIfEmpty(today.AddDays(-Window)).Min();
        int days = today.DayNumber - from.DayNumber;
        var due = runs.Where(r => r.Verdict == "empty" && (sent is null || r.Day > sent)).ToList();
        if (days < Days || (hold is null && due.Count == 0))
        {
            return null;
        }
        return $"{Prefix}{lane.Label}: 0 sent in {days} days. Why: {Why(lane, hold, reads.EmptyRuns, due)}. Fix: {Fix(lane, hold, reads)}.";
    }

    private static List<Run> Campaign(DarkReads reads, DateOnly today)
    {
        var runs = new List<Run>();
        DateOnly start = today.AddDays(-Window);
        foreach (string line in reads.Tracker.Split('\n'))
        {
            var m = CampaignSent().Match(line);
            if (m.Success && DateOnly.TryParseExact(m.Groups[1].Value, "M/d/yyyy", Inv, DateTimeStyles.None, out var day) && day >= start && day <= today)
            {
                runs.Add(new Run(day, "campaign", "done", "CAMPAIGN EMAIL SENT"));
            }
        }
        foreach (string line in reads.PassLog.Split('\n'))
        {
            var m = PassLine().Match(line);
            if (m.Success && DateOnly.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd", Inv, DateTimeStyles.None, out var day) && day >= start && day <= today)
            {
                int eligible = int.Parse(m.Groups[2].Value, Inv), sent = int.Parse(m.Groups[3].Value, Inv);
                runs.Add(new Run(day, "campaign", sent > 0 ? "done" : eligible > 0 ? "empty" : "quiet", $"{eligible} eligible, {sent} sent"));
            }
        }
        return runs;
    }

    private static string Why(SendingLane lane, string? hold, string emptyRuns, IReadOnlyList<Run> due)
    {
        if (hold is not null && Field(hold, "Reason") is { Length: > 0 } reason)
        {
            return $"held since {Field(hold, "Held")}, {reason}".TrimEnd('.');
        }
        string? top = emptyRuns.Split('\n').Select(l => l.Trim())
            .FirstOrDefault(l => l.StartsWith($"EMPTY RUN: {lane.Key},", StringComparison.Ordinal) && l.Contains("Top reasons: ", StringComparison.Ordinal));
        if (top is not null)
        {
            return "top reasons " + top[(top.IndexOf("Top reasons: ", StringComparison.Ordinal) + 13)..].TrimEnd('.');
        }
        return due.Count > 0 ? $"the last run read \"{due[^1].Words.TrimEnd('.', ' ', '|')}\"" : "no reason read";
    }

    private static string Fix(SendingLane lane, string? hold, DarkReads reads)
    {
        string? heal = hold is null ? null : Field(hold, "Heal");
        if (heal is { Length: > 0 } && !heal.StartsWith("none", StringComparison.Ordinal))
        {
            if (reads.Queued.Contains(heal))
            {
                return $"{heal} is queued";
            }
            if (reads.Proposed.Contains(heal))
            {
                return $"{heal} is proposed, waiting on the triage pass";
            }
        }
        string proposal = $"emptyrun-{HealLadder.Key(lane.Key)}.md";
        return reads.Proposed.Contains(proposal) ? $"{proposal} is proposed, waiting on the triage pass" : "not yet found";
    }

    private static string? Field(string text, string name) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).FirstOrDefault(l => l.StartsWith(name + ": ", StringComparison.Ordinal))?[(name.Length + 2)..].Trim();

    [GeneratedRegex(@"^\d+,[^\n]*?,Email,(\d{1,2}/\d{1,2}/\d{4}),[^\n]*?,""?CAMPAIGN EMAIL SENT\b")]
    private static partial Regex CampaignSent();

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2}) 08:\d{2}:\d{2} .*?\b(\d+) eligible, (\d+) sent of\b")]
    private static partial Regex PassLine();
}
