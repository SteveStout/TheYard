using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.NetworkLog;

/// <summary>
/// Who counts as a conversation on a personal mailbox: a person writing from a company's domain, or a person the
/// job hunt already knows (an address or a name in the tracker or the responders list). A personal address nobody
/// in the job hunt knows is family, a neighbour or a vendor, and is counted on the pass line, never logged.
/// </summary>
public static partial class WarmContactRule
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

    /// <summary>
    /// The people the job hunt knows, lower case: every address in the tracker's rows and the responders list, and
    /// every tracker Person of two words or more.
    /// </summary>
    public static IReadOnlySet<string> Known(string trackerCsv, string responders)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (string[] row in TrackerCsvText.Parse(trackerCsv ?? string.Empty).Skip(1))
        {
            if (row.Length > 1 && row[1].Trim().ToLowerInvariant() is { } person && person.Contains(' ', StringComparison.Ordinal) && person.Length <= 60)
            {
                known.Add(person);
            }
            foreach (string field in row)
            {
                known.UnionWith(Address().Matches(field).Select(m => m.Value.ToLowerInvariant()));
            }
        }
        known.UnionWith(Address().Matches(responders ?? string.Empty).Select(m => m.Value.ToLowerInvariant()));
        known.Remove(HumanSenderRule.Steve);
        return known;
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+'-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex Address();
}
