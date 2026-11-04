namespace JobHunt.Domain.Features.NetworkLog;

/// <summary>
/// Who counts as a conversation on a personal mailbox: a person writing from a company's domain, or a person the
/// job hunt already knows (an address or a name in the tracker or the responders list). A personal address nobody
/// in the job hunt knows is family, a neighbour or a vendor, and is counted on the pass line, never logged.
/// </summary>
public static class WarmContactRule
{
    /// <summary>True when the conversation is logged.</summary>
    /// <param name="address">The person's address, lower case.</param>
    /// <param name="fullName">The person's name as their From header carried it.</param>
    /// <param name="known">The addresses and names, lower case, that the tracker and the responders list hold.</param>
    public static bool Counts(string address, string fullName, IReadOnlySet<string> known)
    {
        string key = CompanyKey.Of(address);
        if (key.Length > 0 && !PersonalDomains.All.Contains(key))
        {
            return true;
        }
        string name = (fullName ?? string.Empty).Trim().ToLowerInvariant();
        return known.Contains(address) || (name.Contains(' ', StringComparison.Ordinal) && known.Contains(name));
    }
}
