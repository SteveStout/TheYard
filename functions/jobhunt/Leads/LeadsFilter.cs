using System.Text.RegularExpressions;
using JobHunt.Functions.Ats;

namespace JobHunt.Functions.Leads;

/// <summary>
/// The rules a lead passes before any model reads it (the automation plan's fourth increment): remote
/// only, not already applied to (the same company with the title in an application row's note), and
/// clear of RULE 50 (ApplicationCap: three in 60 days, the do-not-apply list). One lead per job link.
/// What passes goes to the scoring lane; what fails says which rule, so nothing is dropped unseen.
/// </summary>
public static partial class LeadsFilter
{
    /// <summary>Each lead with the rule it failed, or null when it passes.</summary>
    public static IReadOnlyList<(Lead Lead, string? Failed)> Judge(IEnumerable<Lead> leads, IReadOnlyList<string[]> master, IReadOnlyList<DoNotApply> list, DateOnly today)
    {
        var applications = master.Where(AtsRules.IsApplication).ToList();
        return [.. leads
            .GroupBy(l => l.Link, StringComparer.Ordinal).Select(g => g.First())
            .Select(lead => (lead, Failed(lead, applications, master, list, today)))];
    }

    private static string? Failed(Lead lead, List<string[]> applications, IReadOnlyList<string[]> master, IReadOnlyList<DoNotApply> list, DateOnly today)
    {
        if (!Remote().IsMatch(lead.Location + " " + lead.Title))
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

    [GeneratedRegex(@"\bremote\b", RegexOptions.IgnoreCase)]
    private static partial Regex Remote();
}
