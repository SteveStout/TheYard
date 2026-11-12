using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Leads;

/// <summary>
/// The rules a lead passes before any model reads it (the automation plan's fourth increment): remote
/// only (the word remote anywhere in the location or title, or a location that names the country alone, "United
/// States", "US", "USA" or "Anywhere in the US", which is how LinkedIn's alert prints a role it shows as
/// "United States (Remote)"), not already applied to (the same company with the title in an application row's note), and
/// clear of RULE 50 (ApplicationCap: three in 60 days, the do-not-apply list), and, ahead of every other rule, not a
/// company or a posting BLACKLIST.md blocks. One lead per job link.
/// What passes goes to the scoring lane; what fails says which rule, so nothing is dropped unseen.
/// </summary>
public static partial class LeadsFilter
{
    /// <summary>Each lead with the rule it failed, or null when it passes.</summary>
    /// <param name="leads">The alerts' leads.</param>
    /// <param name="master">The tracker's rows.</param>
    /// <param name="list">RULE 50's do-not-apply list.</param>
    /// <param name="today">The Central day.</param>
    /// <param name="blacklist">BLACKLIST.md's rows, or null for none.</param>
    public static IReadOnlyList<(Lead Lead, string? Failed)> Judge(IEnumerable<Lead> leads, IReadOnlyList<string[]> master, IReadOnlyList<DoNotApply> list, DateOnly today, Blacklist? blacklist = null)
    {
        var applications = master.Where(TrackerRows.IsApplication).ToList();
        return [.. leads
            .GroupBy(l => l.Link, StringComparer.Ordinal).Select(g => g.First())
            .Select(lead => (lead, blacklist?.Hit(lead.Company, url: lead.Link)?.Reason ?? Failed(lead, applications, master, list, today)))];
    }

    private static string? Failed(Lead lead, List<string[]> applications, IReadOnlyList<string[]> master, IReadOnlyList<DoNotApply> list, DateOnly today)
    {
        if (!IsRemote(lead))
        {
            return "not remote: " + lead.Location;
        }
        if (BoardAlerts.TopOfBand(lead.Band) is { } top && top < Floor)
        {
            return $"under the floor: the band tops at ${top}K";
        }
        var applied = applications.FirstOrDefault(row => ApplicationCap.Same(row[2], lead.Company) && row[6].Contains(lead.Title, StringComparison.OrdinalIgnoreCase));
        if (applied is not null)
        {
            return $"applied already, Count {applied[0]}";
        }
        var cap = ApplicationCap.Check(lead.Company, master, list, today);
        return cap.Allowed ? null : "RULE 50: " + cap.Why;
    }

    /// <summary>The base floor in thousands (targeting-criteria.md: $155K base for a permanent role); a band printed under it fails.</summary>
    public const int Floor = 155;

    /// <summary>True when the lead reads as remote: the word anywhere, or a location that is the country alone.</summary>
    public static bool IsRemote(Lead lead) => Remote().IsMatch(lead.Location + " " + lead.Title) || Country().IsMatch(lead.Location.Trim());

    [GeneratedRegex(@"\bremote\b", RegexOptions.IgnoreCase)]
    private static partial Regex Remote();

    [GeneratedRegex(@"^(United States( of America)?|US|USA|U\.S\.|Anywhere in (the )?(US|USA|United States))$", RegexOptions.IgnoreCase)]
    private static partial Regex Country();
}
