using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Card;

/// <summary>One Central day of the job hunt's funnel: the six counts the Admin tab's card reads and the breakdown the TODAY.md line prints.</summary>
/// <param name="Day">The Central day the counts belong to.</param>
/// <param name="Scanned">Postings the 01:00 board scan read that day.</param>
/// <param name="Ready">READY rows in that day's JOBS file.</param>
/// <param name="Applied">Applications submitted.</param>
/// <param name="Invites">People sent a connection request.</param>
/// <param name="InMails">People sent an InMail.</param>
/// <param name="Campaign">People the campaign mailed.</param>
/// <param name="Messages">People sent any other LinkedIn message.</param>
/// <param name="Emails">People sent any other email.</param>
/// <param name="Leaders">Leaders the leaders lane named, not yet touched.</param>
/// <param name="Replied">People who wrote back.</param>
/// <param name="Booked">Calls put on the calendar.</param>
public sealed record FunnelDay(DateOnly Day, long Scanned, long Ready, long Applied, long Invites, long InMails, long Campaign, long Messages, long Emails, long Leaders, long Replied, long Booked)
{
    /// <summary>Every person touched that day: the five channels, each person once.</summary>
    public long Reached => Invites + InMails + Campaign + Messages + Emails;

    /// <summary>The counts by the names the run line carries them under.</summary>
    public IReadOnlyList<(string Name, long Value)> Named =>
    [
        ("scanned", Scanned), ("ready", Ready), ("applied", Applied), ("reached", Reached), ("replied", Replied), ("booked", Booked),
        ("invites", Invites), ("inmails", InMails), ("campaign", Campaign), ("messages", Messages), ("emails", Emails), ("leaders", Leaders),
    ];
}

/// <summary>What one day's funnel is counted from, each file's text as the share holds it, empty when absent.</summary>
/// <param name="Tracker">Job_Hunt_Tracker.csv.</param>
/// <param name="PassLog">campaign/pass.log.</param>
/// <param name="Jobs">The day's JOBS file.</param>
/// <param name="Leaders">The day's OUTREACH-DRAFTS.md from the leaders lane.</param>
/// <param name="LinkedIn">Each LinkedIn spool file's text, waiting or done.</param>
public sealed record FunnelInputs(string Tracker, string PassLog, string Jobs, string Leaders, IReadOnlyList<string> LinkedIn);

/// <summary>
/// Counts one Central day of the funnel (PLAN-AUTOHUNT rung 6, J:\mentor\FUNNEL-LINE-SHAPE): postings from the board
/// scan's pass.log line, READY from the JOBS file's supply line, and applications, touches, replies and calls from the
/// opening phrase of each tracker row's Notes, the type every lane writes first. A person counts once a day.
/// </summary>
public static partial class FunnelCount
{
    /// <summary>The run line's process name, which the Admin tab's card reads.</summary>
    public const string Process = "Funnel";

    /// <summary>The day's counts.</summary>
    /// <param name="day">The Central day.</param>
    /// <param name="inputs">The files.</param>
    public static FunnelDay Of(DateOnly day, FunnelInputs inputs)
    {
        long applied = 0, booked = 0;
        var touched = new Dictionary<string, string>(StringComparer.Ordinal);
        var replied = new HashSet<string>(StringComparer.Ordinal);
        foreach (string[] row in TrackerCsvText.Parse(inputs.Tracker))
        {
            if (row.Length < 7 || Date(row[4]) != day)
            {
                continue;
            }
            string notes = row[6].TrimStart();
            string person = Key(row[1]);
            if (Applied().IsMatch(notes))
            {
                applied++;
            }
            if (Booked().IsMatch(notes))
            {
                booked++;
            }
            if (person.Length == 0)
            {
                continue;
            }
            if (TrackerRows.Channel(notes, row[3]) is { } channel)
            {
                touched.TryAdd(person, channel);
            }
            if (Inbound().IsMatch(notes) && !SystemMail().IsMatch(Opening(notes)))
            {
                replied.Add(person);
            }
        }
        foreach (string file in inputs.LinkedIn)
        {
            foreach (string person in LinkedInReplies(file, day))
            {
                replied.Add(person);
            }
        }
        long Touched(string channel) => touched.Values.Count(c => c == channel);
        return new FunnelDay(day, Scanned(inputs.PassLog, day), Ready(inputs.Jobs), applied, Touched("invites"), Touched("inmails"), Touched("campaign"),
            Touched("messages"), Touched("emails"), TodayParts.Lines(inputs.Leaders).Count(l => l.StartsWith("## ", StringComparison.Ordinal)), replied.Count, booked);
    }

    /// <summary>A tracker Date cell as a day: M/d/yyyy, MM/dd/yyyy or yyyy-MM-dd; null for anything else.</summary>
    /// <param name="cell">The cell.</param>
    public static DateOnly? Date(string cell) =>
        DateOnly.TryParseExact(cell.Trim(), ["M/d/yyyy", "MM/dd/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

    /// <summary>The postings the day's scan lines in pass.log read, summed.</summary>
    /// <param name="passLog">campaign/pass.log.</param>
    /// <param name="day">The Central day.</param>
    public static long Scanned(string passLog, DateOnly day)
    {
        string stamp = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " ";
        return TodayParts.Lines(passLog)
            .Where(l => l.StartsWith(stamp, StringComparison.Ordinal))
            .Select(l => ScanLine().Match(l))
            .Where(m => m.Success)
            .Sum(m => long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>The JOBS file's READY count: its supply line, else the rows of its READY table.</summary>
    /// <param name="jobs">The JOBS file.</param>
    public static long Ready(string jobs)
    {
        var lines = TodayParts.Lines(jobs).ToList();
        if (lines.Select(l => Supply().Match(l)).LastOrDefault(m => m.Success) is { } supply)
        {
            return long.Parse(supply.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        int at = lines.FindIndex(l => l.Trim().StartsWith("## READY", StringComparison.Ordinal));
        return at < 0 ? 0 : lines.Skip(at + 1).TakeWhile(l => !l.StartsWith("## ", StringComparison.Ordinal))
            .Count(l => TableRow().IsMatch(l));
    }

    private static IEnumerable<string> LinkedInReplies(string file, DateOnly day)
    {
        var lines = TodayParts.Lines(file).ToList();
        if (lines.Count == 0 || !lines[0].StartsWith("Person\t", StringComparison.Ordinal))
        {
            yield break;
        }
        foreach (string[] f in lines.Skip(1).Select(l => l.Split('\t')).Where(f => f.Length == 7))
        {
            if (DateTimeOffset.TryParse(f[4].Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                && DateOnly.FromDateTime(CentralClock.Local(at)) == day && Key(f[0]) is { Length: > 0 } person)
            {
                yield return person;
            }
        }
    }

    private static string Key(string person)
    {
        string letters = TrackerCsvText.Letters(person).ToLowerInvariant();
        return letters is "na" or "none" ? string.Empty : letters;
    }

    private static string Opening(string notes) => notes.Split((char[])['(', ':', '.'], 2)[0];

    [GeneratedRegex(@"^(?:APPLICATION SUBMITTED|SUBMITTED|APPLIED)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Applied();

    [GeneratedRegex(@"^(?:INTERVIEW BOOKED|CALL BOOKED|SCREEN BOOKED|BOOKED|(?:ROUND \d+ )?INTERVIEW SCHEDULED)\b")]
    private static partial Regex Booked();

    [GeneratedRegex(@"^INBOUND\b")]
    private static partial Regex Inbound();

    [GeneratedRegex(@"\b(?:REJECTION|RECEIPT|AUTO-REPLY|AUTOMATED)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SystemMail();

    [GeneratedRegex(@"\bscan: \d+ boards, \d+ answered, (\d+) postings\b")]
    private static partial Regex ScanLine();

    [GeneratedRegex(@"^Supply: .*\b(\d+) ready\.\s*$")]
    private static partial Regex Supply();

    [GeneratedRegex(@"^\|\s*\d+\s*\|")]
    private static partial Regex TableRow();
}
