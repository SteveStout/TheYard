using System.Text.RegularExpressions;

namespace JobHunt.Functions.Ats;

/// <summary>Reading a mail address: the address and name out of a From header, and whether a machine sent it.</summary>
public static partial class Mail
{
    /// <summary>
    /// The application tracking systems the records run treats as known senders (BOOKKEEPING-LANE.md
    /// step 3), by the domains their mail comes from.
    /// </summary>
    public static readonly IReadOnlyList<string> AtsDomains =
    [
        "greenhouse.io", "greenhouse-mail.io", "lever.co", "ashbyhq.com", "myworkday.com", "workday.com", "icims.com",
        "jobvite.com", "smartrecruiters.com", "taleo.net", "successfactors.com", "bamboohr.com", "workable.com", "workablemail.com",
    ];

    /// <summary>The Gmail search for the known senders' mail still in the inbox.</summary>
    public static string Query => "in:inbox from:(" + string.Join(" OR ", AtsDomains) + ")";

    /// <summary>The address in a From header, lower case: the part in angle brackets, or the whole header.</summary>
    public static string Address(string from)
    {
        var bracket = Bracket().Match(from ?? string.Empty);
        return (bracket.Success ? bracket.Groups[1].Value : from ?? string.Empty).Trim().Trim('"').ToLowerInvariant();
    }

    /// <summary>The display name in a From header, without quotes, or empty.</summary>
    public static string Name(string from)
    {
        int at = (from ?? string.Empty).IndexOf('<', StringComparison.Ordinal);
        return at <= 0 ? string.Empty : from![..at].Trim().Trim('"').Trim();
    }

    /// <summary>True when the address's domain is a known tracking system's, or a subdomain of one.</summary>
    public static bool IsAts(string address)
    {
        string domain = Domain(address);
        return AtsDomains.Any(ats => domain == ats || domain.EndsWith("." + ats, StringComparison.Ordinal));
    }

    /// <summary>
    /// True when no person sends from the address: a known system's domain, or a local part that names a
    /// mailbox nobody answers (no-reply, notifications, careers and the like). Anything else is a person,
    /// and a thread with a person in it stays in the inbox.
    /// </summary>
    public static bool IsMachine(string address) => IsAts(address) || MachineLocal().IsMatch(Local(address));

    /// <summary>The domain of an address, lower case.</summary>
    public static string Domain(string address)
    {
        int at = address.LastIndexOf('@');
        return at < 0 ? string.Empty : address[(at + 1)..].ToLowerInvariant();
    }

    private static string Local(string address)
    {
        int at = address.IndexOf('@', StringComparison.Ordinal);
        return at < 0 ? address : address[..at];
    }

    [GeneratedRegex(@"<([^>]+)>")]
    private static partial Regex Bracket();

    [GeneratedRegex(@"^(no-?reply|do-?not-?reply|noreply[-.\w]*|notifications?|careers?|talent|recruiting|hiring|applications?|apply|jobs|mailer|system|ats|candidates?)$", RegexOptions.IgnoreCase)]
    private static partial Regex MachineLocal();
}
