using System.Globalization;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Card;

/// <summary>
/// How long a reply has been in front of him (autohunt1109-4 follow-on 3): the replies run oldest first, each with one
/// line of history. The first day is the earliest of four reads: the queue's <c>Note: since: </c> line, the last card's
/// own "On the card since M/D" for the same person, the last card's date when it named the person under Replies with
/// no such words, and the item's own "M/D queue" dates (a carried item names the queue it came from). With none, the
/// item is new today. The history is the queue's <c>Note: history: </c> line, else what the card can say without one.
/// No earlier card is kept on the share, so the card's own words on the last card are its only record of the cards
/// before it.
/// </summary>
public static partial class TodayHistory
{
    /// <summary>The words the card writes after each reply's history, which the next card reads back.</summary>
    public const string Since = "On the card since";

    /// <summary>Section A, oldest first by <see cref="First"/>, queue order kept within a day.</summary>
    /// <param name="inputs">The card's inputs.</param>
    /// <param name="replies">Section A's items still shown.</param>
    public static IReadOnlyList<TodayItem> Oldest(TodayInputs inputs, IReadOnlyList<TodayItem> replies) =>
        [.. replies.OrderBy(r => First(inputs, r))];

    /// <summary>The first day the item was in front of him, by the earliest of the four reads; the card's day when none says.</summary>
    /// <param name="inputs">The card's inputs.</param>
    /// <param name="item">The reply.</param>
    public static DateOnly First(TodayInputs inputs, TodayItem item)
    {
        var today = DateOnly.FromDateTime(inputs.At);
        var days = new List<DateOnly> { today };
        foreach (string note in item.Notes.Where(n => n.StartsWith("since: ", StringComparison.Ordinal)))
        {
            string value = note["since: ".Length..].Trim();
            if (DateOnly.TryParseExact(value.Length >= 10 ? value[..10] : value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var since))
            {
                days.Add(since);
            }
        }
        if (Last(inputs.Previous, item) is { } block)
        {
            var marker = Marker().Match(block);
            var heading = Heading().Match(inputs.Previous);
            if (marker.Success)
            {
                days.Add(Day(marker, today));
            }
            else if (heading.Success)
            {
                days.Add(Day(heading, today));
            }
        }
        foreach (Match m in QueueDate().Matches(string.Join(" ", item.Notes.Prepend(item.Held))))
        {
            days.Add(Day(m, today));
        }
        return days.Min();
    }

    /// <summary>The reply's one line of history, ending in the words the next card reads back.</summary>
    /// <param name="inputs">The card's inputs.</param>
    /// <param name="item">The reply.</param>
    public static string Line(TodayInputs inputs, TodayItem item)
    {
        var first = First(inputs, item);
        string? note = item.Notes.Where(n => n.StartsWith("history: ", StringComparison.Ordinal)).Select(n => n["history: ".Length..].Trim().TrimEnd('.')).FirstOrDefault();
        string history = note ?? (first < DateOnly.FromDateTime(inputs.At) ? "carried from an earlier card; the queue gives no history line" : "new in this run's queue");
        return $"History: {history}. {Since} {first:M/d}.";
    }

    /// <summary>The last card's block under Replies for the same person, or null when it did not name them there.</summary>
    /// <param name="previous">TODAY.md before this card.</param>
    /// <param name="item">The reply.</param>
    public static string? Last(string previous, TodayItem item)
    {
        var lines = TodayParts.Lines(previous).ToList();
        int at = lines.FindIndex(l => l.StartsWith("## Replies", StringComparison.Ordinal));
        if (at < 0)
        {
            return null;
        }
        var under = lines.Skip(at + 1).TakeWhile(l => !l.StartsWith("## ", StringComparison.Ordinal));
        return TodayParts.Blocks(under).FirstOrDefault(b => Bold().Match(b) is { Success: true } m && Names(m.Groups[1].Value, item));
    }

    private static bool Names(string bold, TodayItem item) =>
        bold == item.Who || (item.Person.Length > 0 && (bold == item.Person || bold.StartsWith(item.Person + " (", StringComparison.Ordinal)));

    // A month and day in the card's year, or the year before when that would put it after the card.
    private static DateOnly Day(Match m, DateOnly today)
    {
        int month = int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture);
        int day = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture);
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(today.Year, month))
        {
            return today;
        }
        var when = new DateOnly(today.Year, month, day);
        return when > today ? when.AddYears(-1) : when;
    }

    [GeneratedRegex(@"On the card since (?<m>\d{1,2})/(?<d>\d{1,2})")]
    private static partial Regex Marker();

    [GeneratedRegex(@"(?m)^# TODAY, \w+ (?<m>\d{1,2})/(?<d>\d{1,2})")]
    private static partial Regex Heading();

    [GeneratedRegex(@"\b(?<m>\d{1,2})/(?<d>\d{1,2}) queue\b")]
    private static partial Regex QueueDate();

    [GeneratedRegex(@"^- \*\*(.+?)\*\*")]
    private static partial Regex Bold();
}
