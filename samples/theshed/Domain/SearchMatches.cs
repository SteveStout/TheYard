using TestProject.Data;

namespace TestProject.Domain;

/// <summary>
/// The rule for how many matches a search keeps: the first entries whose name fits the
/// pattern, up to the limit, plus whether the walk had more. It reads exactly one match
/// past the limit, because that is the cheapest proof the list is cut off, and the walk
/// stops there. Nothing here touches a store or the web, so the rule is tested with a
/// plain list of entries.
/// </summary>
public static class SearchMatches
{
    /// <summary>Takes the matches from a walk of entries.</summary>
    /// <param name="walk">Entries handed back one at a time; the walk stops once enough have been read.</param>
    /// <param name="pattern">What an entry's name has to match.</param>
    /// <param name="limit">The most matches to keep, at least 1.</param>
    /// <returns>The kept matches, in walk order, and true when a match past the limit was found.</returns>
    public static (List<StoreEntry> Kept, bool Truncated) Take(IEnumerable<StoreEntry> walk, NamePattern pattern, int limit)
    {
        // The name is matched as a slice of the absolute path, so the entries that do not
        // match cost no string copies, and Take ends the walk at the one extra match.
        List<StoreEntry> kept = walk
            .Where(entry => pattern.Matches(Path.GetFileName(entry.Absolute.AsSpan())))
            .Take(limit + 1)
            .ToList();
        bool truncated = kept.Count > limit;
        if (truncated)
        {
            kept.RemoveAt(limit);
        }
        return (kept, truncated);
    }
}
