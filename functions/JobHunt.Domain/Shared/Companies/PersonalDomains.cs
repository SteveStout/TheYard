namespace JobHunt.Domain.Shared.Companies;

/// <summary>
/// The mail domains people keep for themselves rather than for work. The campaign never writes to one, and the
/// network log reads a conversation from one only when the person is already known to the tracker.
/// </summary>
public static class PersonalDomains
{
    /// <summary>The personal mail domains, keyed the way CompanyKey keys them.</summary>
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "gmail.com", "googlemail.com", "yahoo.com", "ymail.com", "outlook.com", "hotmail.com", "live.com", "msn.com",
        "icloud.com", "me.com", "mac.com", "aol.com", "proton.me", "protonmail.com", "gmx.com", "mail.com",
        "yandex.com", "comcast.net", "att.net", "sbcglobal.net", "verizon.net", "charter.net", "cox.net",
    };
}
