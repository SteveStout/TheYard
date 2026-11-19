using System.Globalization;

namespace JobHunt.Domain.Shared.Mail;

/// <summary>
/// The Central days no email goes out under Steve's name except to his own address (selfheal1120 version 2; the
/// year-end plan's Thanksgiving and holiday hold). J:\dispatch\NO-EMAIL-DAYS.txt holds one date (`2026-11-26`) or one
/// range (`2026-12-24..2027-01-01`) a line, and # starts a comment; yearend1120 writes the dates. A line that reads as
/// neither is kept in <see cref="Bad"/> and lists nothing, so a typo never holds a day nobody named.
/// </summary>
/// <param name="Spans">The listed days, each a first and a last day.</param>
/// <param name="Read">Whether the file was there and read; a missing or unreadable file lists nothing.</param>
/// <param name="Bad">The lines that read as neither a date nor a range.</param>
public sealed record NoEmailDays(IReadOnlyList<(DateOnly From, DateOnly To)> Spans, bool Read, IReadOnlyList<string> Bad)
{
    /// <summary>The file on the share, under the job-hunt folder.</summary>
    public const string File = "dispatch/NO-EMAIL-DAYS.txt";

    /// <summary>No file, nothing listed.</summary>
    public static readonly NoEmailDays None = new([], false, []);

    /// <summary>Whether a Central day is listed.</summary>
    public bool Listed(DateOnly day) => Spans.Any(s => s.From <= day && day <= s.To);

    /// <summary>The file's text as its days.</summary>
    public static NoEmailDays Parse(string text)
    {
        var spans = new List<(DateOnly, DateOnly)>();
        var bad = new List<string>();
        foreach (string raw in (text ?? string.Empty).Split('\n'))
        {
            string line = raw.Split('#')[0].Trim();
            if (line.Length == 0)
            {
                continue;
            }
            string[] ends = line.Split("..", StringSplitOptions.TrimEntries);
            if (ends.Length is 1 or 2 && Day(ends[0]) is { } from && Day(ends[^1]) is { } to && from <= to)
            {
                spans.Add((from, to));
            }
            else
            {
                bad.Add(line);
            }
        }
        return new NoEmailDays(spans, true, bad);
    }

    private static DateOnly? Day(string text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
}
