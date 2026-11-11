using System.Globalization;

namespace JobHunt.Domain.Features.Jobs;

/// <summary>
/// One hiring signal beyond a single posting, from the postings the scans already hold: a company that opened three or
/// more engineering seats in seven days, by when a scan first saw each posting. A seat is a distinct title the pounce's
/// seat rule passes (PounceRules.IsSeat), so the same title listed in five cities is one seat and a sales or product
/// opening is none. The line goes on the share for the TODAY card; it names companies and counts, nothing else.
/// </summary>
public static class HiringSignal
{
    /// <summary>The line on the share, rewritten whole each time.</summary>
    public const string Path = "nightly/hiring-signal.md";

    /// <summary>The fewest seats in the window that make a signal.</summary>
    public const int Seats = 3;

    /// <summary>The window, back from now.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromDays(7);

    /// <summary>The most companies the line names; the rest are counted.</summary>
    public const int MostNamed = 8;

    /// <summary>The companies with three or more distinct engineering titles, most first, then by name.</summary>
    /// <param name="seen">Each posting first seen in the window: its company and title.</param>
    public static IReadOnlyList<(string Company, int Seats)> Surges(IReadOnlyList<(string Company, string Title)> seen) =>
        [.. seen.Where(p => p.Company.Trim().Length > 0 && PounceRules.IsSeat(p.Title))
            .GroupBy(p => p.Company.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => (Company: g.First().Company.Trim(), Seats: g.Select(p => p.Title.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count()))
            .Where(g => g.Seats >= Seats)
            .OrderByDescending(g => g.Seats).ThenBy(g => g.Company, StringComparer.Ordinal)];

    /// <summary>The card's one line.</summary>
    /// <param name="local">When it was read, Central.</param>
    /// <param name="surges">The companies that made the signal.</param>
    public static string Line(DateTime local, IReadOnlyList<(string Company, int Seats)> surges)
    {
        string at = local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        if (surges.Count == 0)
        {
            return $"Hiring signal ({at} Central): no company opened {Seats} or more engineering seats in the last {Window.Days} days. From jobhunt.Postings.";
        }
        string named = string.Join(", ", surges.Take(MostNamed).Select(s => $"{s.Company} ({s.Seats.ToString(CultureInfo.InvariantCulture)})"));
        string more = surges.Count > MostNamed ? $" and {surges.Count - MostNamed} more" : string.Empty;
        return $"Hiring signal ({at} Central): {surges.Count} {(surges.Count == 1 ? "company" : "companies")} opened {Seats} or more engineering seats in the last {Window.Days} days, by when the scan first saw each: {named}{more}. From jobhunt.Postings.";
    }
}
