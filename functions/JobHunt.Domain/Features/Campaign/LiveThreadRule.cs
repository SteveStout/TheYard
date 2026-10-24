using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Campaign;

/// <summary>
/// No cold email over a thread that is already alive (the review, F16; Steve's 9/2 rule that the reply thread
/// comes before any draft). Any tracker row for the company (Rule 50's name match) in the last 30 days whose
/// Communication is not an application channel holds the lead: a LinkedIn message, an email with a person, a
/// call. Over-holding by design. Two kinds of row do not hold, because they are the application itself and not a
/// person: an application row (AtsRules.IsApplication, or an ATS named in Communication) and the ATS sweep's RECEIPT
/// row, which the Function writes with Communication "Email" for an automated confirmation. A connection request
/// that nobody has answered does not hold either: it is the LinkedIn preference order's input (LinkedInOrder),
/// which ranks a lead with an invite out above one without, and holding it here would make that order dead code.
/// A rejection does hold: a manager is not cold-emailed about a company that has just said no.
/// </summary>
public static partial class LiveThreadRule
{
    /// <summary>The window in days.</summary>
    public const int WindowDays = 30;

    /// <summary>The Communication values that name an application channel, matched as contained words.</summary>
    public static readonly IReadOnlyList<string> ApplicationChannels =
        ["Greenhouse", "Workday", "Ashby", "Lever", "Easy Apply", "Company Website", "Application",
         "JazzHR", "Rippling", "iCIMS", "Jobvite", "Workable", "Dayforce", "SuccessFactors", "BambooHR", "SmartRecruiters", "Taleo"];

    /// <summary>Allowed, or held naming the newest row that holds it.</summary>
    public static CampaignVerdict Check(IReadOnlyList<string> names, IReadOnlyList<string[]> master, DateOnly today)
    {
        var holding = master
            .Where(row => row.Length >= 7 && names.Any(n => ApplicationCap.Same(n, row[2])) && Holds(row))
            .Select(row => (Row: row, Day: ApplicationCap.Day(row[4])))
            .Where(r => r.Day is { } day && today.DayNumber - day.DayNumber is >= 0 and <= WindowDays)
            .OrderByDescending(r => r.Day)
            .Select(r => r.Row)
            .FirstOrDefault();
        return holding is null ? CampaignVerdict.Pass : CampaignVerdict.Hold($"live thread, Count {holding[0].Trim()}");
    }

    /// <summary>Whether a row is a thread with a person rather than an application or its receipt.</summary>
    public static bool Holds(string[] row)
    {
        string channel = row[3];
        bool application = ApplicationChannels.Any(c => channel.Contains(c, StringComparison.OrdinalIgnoreCase)) || AtsRules.IsApplication(row);
        bool receipt = row[6].TrimStart().StartsWith("RECEIPT", StringComparison.OrdinalIgnoreCase);
        bool invite = channel.Contains("invite", StringComparison.OrdinalIgnoreCase) || Invite().IsMatch(row[6]);
        return !application && !receipt && !invite;
    }

    [GeneratedRegex(@"^\s*(scoped\s+)?(connection request|invite)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Invite();
}
