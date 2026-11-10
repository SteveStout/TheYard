using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Card;

/// <summary>
/// The funnel's share log and its weekly line (PLAN-AUTOHUNT rung 6): each run appends its run line to
/// nightly/funnel.log behind the Central time and the kind of run, and the TODAY card sums the week from it, Monday
/// to Sunday, the week that holds the day before the card, each day its latest line. A day with no line is named
/// after the counts, never read as a silent zero.
/// </summary>
public static partial class FunnelWeek
{
    /// <summary>The funnel's log on the share.</summary>
    public const string Log = "nightly/funnel.log";

    /// <summary>The line's opening words, which the hand run finds the line in TODAY.md by.</summary>
    public const string Opening = "Funnel, week of ";

    /// <summary>The first calls a week the hunt is measured against.</summary>
    public const string Target = "2 to 3 a week";

    /// <summary>The log line for one run: the Central time, the kind, then the run line.</summary>
    /// <param name="local">When the run ended, Central.</param>
    /// <param name="kind">"timer" or "hand".</param>
    /// <param name="runLine">The run line's text.</param>
    public static string LogLine(DateTime local, string kind, string runLine) =>
        $"{local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)} {kind}: {runLine}";

    /// <summary>The Monday that opens the week holding a day.</summary>
    /// <param name="day">The day.</param>
    public static DateOnly Monday(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>Each day's latest line in the log, by day, read back into counts; a line that cannot be read is skipped.</summary>
    /// <param name="log">nightly/funnel.log.</param>
    public static IReadOnlyDictionary<DateOnly, (FunnelDay Day, bool Partial)> Days(string log)
    {
        var days = new Dictionary<DateOnly, (FunnelDay, bool)>();
        foreach (string line in TodayParts.Lines(log))
        {
            var m = Line().Match(line);
            if (!m.Success || !DateOnly.TryParseExact(m.Groups["day"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                continue;
            }
            long Count(string name) => Regex.Match(line, $@" {name}=(\d+)(?: |$)", RegexOptions.None, TimeSpan.FromMilliseconds(100)) is { Success: true } c
                ? long.Parse(c.Groups[1].Value, CultureInfo.InvariantCulture)
                : 0;
            bool partial = DateOnly.TryParseExact(m.Groups["at"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) && at <= day;
            days[day] = (new FunnelDay(day, Count("scanned"), Count("ready"), Count("applied"), Count("invites"), Count("inmails"), Count("campaign"),
                Count("messages"), Count("emails"), Count("leaders"), Count("replied"), Count("booked")), partial);
        }
        return days;
    }

    /// <summary>
    /// The weekly line for the week holding <paramref name="through"/>, summed over its days from Monday through that
    /// day. A day the log holds no line for is named; a line written on its own day says so.
    /// </summary>
    /// <param name="log">nightly/funnel.log.</param>
    /// <param name="through">The last day the line covers: the day before the card, or the hand run's day.</param>
    public static string Text(string log, DateOnly through)
    {
        var days = Days(log);
        var monday = Monday(through);
        var week = Enumerable.Range(0, through.DayNumber - monday.DayNumber + 1).Select(monday.AddDays).ToList();
        var read = week.Where(days.ContainsKey).Select(d => days[d]).ToList();
        long Sum(Func<FunnelDay, long> pick) => read.Sum(r => pick(r.Day));
        var text = new StringBuilder(Opening);
        text.Append(CultureInfo.InvariantCulture, $"{Short(monday)}, {read.Count} of 7 days read ({Short(monday)} to {Short(through)}");
        if (read.Any(r => r.Partial))
        {
            text.Append(", ").Append(string.Join(", ", read.Where(r => r.Partial).Select(r => Short(r.Day.Day)))).Append(" so far");
        }
        text.Append(CultureInfo.InvariantCulture, $"): {Sum(d => d.Scanned)} postings scanned, {Sum(d => d.Ready)} READY, {Sum(d => d.Applied)} applied, ");
        text.Append(CultureInfo.InvariantCulture, $"{Sum(d => d.Reached)} humans reached ({Sum(d => d.Invites)} invites, {Sum(d => d.InMails)} InMails, {Sum(d => d.Campaign)} campaign, ");
        text.Append(CultureInfo.InvariantCulture, $"{Sum(d => d.Messages)} LinkedIn messages, {Sum(d => d.Emails)} emails; {Sum(d => d.Leaders)} leaders named), ");
        text.Append(CultureInfo.InvariantCulture, $"{Sum(d => d.Replied)} human replies, {Sum(d => d.Booked)} calls booked, against {Target}.");
        var missing = week.Where(d => !days.ContainsKey(d)).ToList();
        if (missing.Count > 0)
        {
            text.Append(" No line for ").Append(string.Join(", ", missing.Select(Short))).Append('.');
        }
        return text.ToString();
    }

    /// <summary>A card with its funnel line put in: the old line replaced where it stands, else the line set under the "Written" line.</summary>
    /// <param name="card">TODAY.md.</param>
    /// <param name="line">The weekly line.</param>
    public static string Splice(string card, string line)
    {
        var lines = (card ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        int at = lines.FindIndex(l => l.StartsWith(Opening, StringComparison.Ordinal));
        if (at >= 0)
        {
            lines[at] = line;
            return string.Join('\n', lines);
        }
        int written = lines.FindIndex(l => l.StartsWith("Written ", StringComparison.Ordinal));
        if (written < 0)
        {
            return line + "\n\n" + card;
        }
        lines.InsertRange(written + 1, ["", line]);
        return string.Join('\n', lines);
    }

    private static string Short(DateOnly day) => day.ToString("ddd M/d", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(?<at>\d{4}-\d{2}-\d{2}) \d{2}:\d{2}:\d{2} \w+: process:Funnel outcome=\w+ .*? detail=(?<day>\d{4}-\d{2}-\d{2})(?: |$)")]
    private static partial Regex Line();
}
