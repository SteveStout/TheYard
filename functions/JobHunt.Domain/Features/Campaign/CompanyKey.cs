namespace JobHunt.Domain.Features.Campaign;

/// <summary>
/// A company's key for the 30-day rule (the review, F10): the registrable domain of an address or a host,
/// so mail.acme.co.uk keys as acme.co.uk and careers.acme.com as acme.com. Public-suffix aware for the suffixes
/// a hiring manager's address will carry: the suffixes below are the public suffix list's entries for
/// the countries and the US government and education names this search meets, held as a constant because the
/// Domain ring reads no file (ADR-117). A suffix not on the list keys as the last two labels, which over-joins
/// (two companies under one unknown two-label suffix share a key) and so over-holds, the safe direction.
/// </summary>
public static class CompanyKey
{
    /// <summary>Public suffixes of two or three labels: a domain under one of these keeps one label more than the suffix.</summary>
    public static readonly IReadOnlySet<string> MultiLabelSuffixes = new HashSet<string>(StringComparer.Ordinal)
    {
        "co.uk", "org.uk", "ac.uk", "gov.uk", "ltd.uk", "plc.uk", "me.uk",
        "com.au", "net.au", "org.au", "edu.au", "gov.au",
        "co.nz", "org.nz", "co.za", "co.in", "net.in", "org.in", "co.jp", "ne.jp", "or.jp",
        "com.br", "com.mx", "com.ar", "com.sg", "com.hk", "com.tw", "com.cn", "co.kr", "co.il",
        "k12.mo.us", "lib.mo.us", "state.mo.us",
    };

    /// <summary>The key of an address (anything after its @) or a host, lowercased; empty for nothing usable.</summary>
    public static string Of(string addressOrHost)
    {
        string host = (addressOrHost ?? string.Empty).Trim().ToLowerInvariant();
        int at = host.LastIndexOf('@');
        if (at >= 0)
        {
            host = host[(at + 1)..];
        }
        host = host.Trim().TrimEnd('.').TrimEnd('>');
        if (host.StartsWith("www.", StringComparison.Ordinal))
        {
            host = host[4..];
        }
        string[] labels = host.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (labels.Length < 2 || labels.Any(label => !label.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')))
        {
            return string.Empty;
        }
        int keep = 2;
        for (int take = Math.Min(3, labels.Length - 1); take >= 2; take--)
        {
            if (MultiLabelSuffixes.Contains(string.Join('.', labels[^take..])))
            {
                keep = take + 1;
                break;
            }
        }
        return string.Join('.', labels[^Math.Min(keep, labels.Length)..]);
    }
}
