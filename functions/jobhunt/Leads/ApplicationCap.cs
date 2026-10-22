using System.Globalization;
using System.Text.RegularExpressions;
using JobHunt.Functions.Ats;

namespace JobHunt.Functions.Leads;

/// <summary>
/// RULE 50 as code (systemRules.md in the job-hunt folder, Steve 2026-10-22): no more than three
/// applications to one company in any rolling 60 days, and no application at all to a company on the
/// do-not-apply list before its date. One check, read by the card today and by the leads filter next,
/// so the number lives in one place (the devil's review of the executable-process plan: build one cap
/// check, not two). Company names match case-insensitively with their known variants, and a name that
/// starts with another ("Upstart Network", "Upstart") counts as the same company: over-counting holds an
/// application back, under-counting is how Upstart happened.
/// </summary>
public static partial class ApplicationCap
{
    /// <summary>Applications allowed to one company in the window.</summary>
    public const int Allowed = 3;

    /// <summary>The window, in days.</summary>
    public const int WindowDays = 60;

    /// <summary>
    /// The do-not-apply list out of systemRules.md: each table row under the DO-NOT-APPLY LIST heading,
    /// its company with any variants named in quotes, and the date before which nothing goes in.
    /// </summary>
    public static IReadOnlyList<DoNotApply> DoNotApplyList(string rules)
    {
        int at = rules.IndexOf("DO-NOT-APPLY LIST", StringComparison.Ordinal);
        if (at < 0)
        {
            return [];
        }
        var list = new List<DoNotApply>();
        foreach (string line in rules[at..].Split('\n').Skip(1).Select(l => l.Trim()).SkipWhile(l => !l.StartsWith('|')).TakeWhile(l => l.StartsWith('|')))
        {
            string[] cells = line.Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length < 2 || !DateOnly.TryParseExact(cells[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var until))
            {
                continue;
            }
            var names = new List<string> { Paren().Replace(cells[0], "").Trim() };
            names.AddRange(Quoted().Matches(cells[0]).Select(m => m.Groups[1].Value.Trim()));
            list.Add(new DoNotApply(names, until));
        }
        return list;
    }

    /// <summary>The application rows for a company inside the window ending on <paramref name="today"/>.</summary>
    public static IReadOnlyList<(string Count, DateOnly Day)> Applications(string company, IReadOnlyList<string[]> master, DateOnly today) =>
        [.. master
            .Where(row => AtsRules.IsApplication(row) && Same(row[2], company) && Day(row[4]) is { } day && today.DayNumber - day.DayNumber is >= 0 and < WindowDays)
            .Select(row => (row[0].Trim(), Day(row[4])!.Value))
            .OrderBy(a => a.Item2)];

    /// <summary>Whether one more application to the company may go in today, and if not, from when.</summary>
    public static CapVerdict Check(string company, IReadOnlyList<string[]> master, IReadOnlyList<DoNotApply> list, DateOnly today)
    {
        var listed = list.FirstOrDefault(d => d.Names.Any(n => Same(n, company)) && today < d.Until);
        if (listed is not null)
        {
            return new CapVerdict(company, false, listed.Until, $"{company} is on the do-not-apply list until {listed.Until:yyyy-MM-dd}");
        }
        var apps = Applications(company, master, today);
        if (apps.Count < Allowed)
        {
            return new CapVerdict(company, true, null, $"{company}: {apps.Count} of {Allowed} in {WindowDays} days");
        }
        // The window opens again the day after the application that would bring the count under the cap ages out.
        var opens = apps[apps.Count - Allowed].Day.AddDays(WindowDays);
        return new CapVerdict(company, false, opens, $"{company}: {apps.Count} applications in {WindowDays} days (Counts {string.Join(", ", apps.Select(a => a.Count))}), next on {opens:yyyy-MM-dd}");
    }

    /// <summary>Every company at or over the cap today, or on the list, for the card.</summary>
    public static IReadOnlyList<CapVerdict> Held(IReadOnlyList<string[]> master, IReadOnlyList<DoNotApply> list, DateOnly today) =>
        [.. master.Where(AtsRules.IsApplication).Select(row => row[2].Trim()).Where(c => c.Length > 0 && !c.Equals("NA", StringComparison.OrdinalIgnoreCase))
            .Concat(list.Where(d => today < d.Until).Select(d => d.Names[0]))
            .GroupBy(Normal, StringComparer.Ordinal).Select(g => g.First())
            .Select(company => Check(company, master, list, today))
            .Where(verdict => !verdict.Allowed)
            .OrderBy(verdict => verdict.Company, StringComparer.OrdinalIgnoreCase)];

    /// <summary>Two company names for the same company: equal once normalized, or one the other's first words.</summary>
    public static bool Same(string a, string b)
    {
        string x = Normal(a), y = Normal(b);
        return x.Length > 0 && y.Length > 0 && (x == y || x.StartsWith(y + " ", StringComparison.Ordinal) || y.StartsWith(x + " ", StringComparison.Ordinal));
    }

    private static string Normal(string name) =>
        Space().Replace(Punctuation().Replace((name ?? string.Empty).ToLowerInvariant(), " "), " ").Trim();

    private static DateOnly? Day(string cell) =>
        DateOnly.TryParseExact((cell ?? string.Empty).Trim(), ["M/d/yyyy", "MM/dd/yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Paren();

    [GeneratedRegex("\"([^\"]+)\"")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex Punctuation();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();
}

/// <summary>One company on the do-not-apply list: its names and the first day an application may go in.</summary>
public sealed record DoNotApply(IReadOnlyList<string> Names, DateOnly Until);

/// <summary>Whether an application may go in, and why.</summary>
public sealed record CapVerdict(string Company, bool Allowed, DateOnly? OpensOn, string Why);
