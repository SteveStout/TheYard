namespace JobHunt.Domain.Features.Campaign;

/// <summary>
/// Who may receive a campaign email (the review, section 3 check 2 and 8.1): an address on a business domain
/// whose MX records were checked and found. A personal mailbox is never a hiring manager's work address, and
/// mail to one is how a personal Gmail gets flagged; a domain with no MX bounces, and two bounces drop the
/// campaign to shadow. The MX lookup itself is Infrastructure's; this rule reads its stored answer.
/// </summary>
public static class RecipientDomainRule
{
    /// <summary>The personal mail domains, keyed the way CompanyKey keys them.</summary>
    public static readonly IReadOnlySet<string> Personal = new HashSet<string>(StringComparer.Ordinal)
    {
        "gmail.com", "googlemail.com", "yahoo.com", "ymail.com", "outlook.com", "hotmail.com", "live.com", "msn.com",
        "icloud.com", "me.com", "mac.com", "aol.com", "proton.me", "protonmail.com", "gmx.com", "mail.com",
        "yandex.com", "comcast.net", "att.net", "sbcglobal.net", "verizon.net", "charter.net", "cox.net",
    };

    /// <summary>Allowed, or held with the reason: no address, a malformed one, a personal domain, MX not read or not found.</summary>
    public static CampaignVerdict Check(string? address, bool? mxFound)
    {
        string to = (address ?? string.Empty).Trim();
        int at = to.IndexOf('@', StringComparison.Ordinal);
        if (to.Length == 0)
        {
            return CampaignVerdict.Hold("no address");
        }
        if (at <= 0 || at != to.LastIndexOf('@') || to.Any(char.IsWhiteSpace))
        {
            return CampaignVerdict.Hold("not an address");
        }
        string key = CompanyKey.Of(to);
        if (key.Length == 0)
        {
            return CampaignVerdict.Hold("not an address");
        }
        if (Personal.Contains(key))
        {
            return CampaignVerdict.Hold($"personal domain ({key})");
        }
        return mxFound switch
        {
            null => CampaignVerdict.Hold("MX not checked"),
            false => CampaignVerdict.Hold($"no MX for {key}"),
            true => CampaignVerdict.Pass,
        };
    }
}
