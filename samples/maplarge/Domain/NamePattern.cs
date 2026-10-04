using System.Text.RegularExpressions;

namespace TestProject.Domain;

/// <summary>
/// Decides whether a file or folder name matches a search query. A query containing
/// * or ? is a glob that must match the whole name, so <c>*.md</c> finds every markdown
/// file and <c>report?</c> finds "report1" but not "report10". A query without those
/// characters is a substring, so <c>invoice</c> finds "Invoice-final.pdf". Both
/// ignore case, because a person searching for a file rarely remembers its exact
/// capitalisation.
/// </summary>
public sealed class NamePattern
{
    // A glob becomes a regular expression built from user input, so every match gets a
    // timeout and no query can tie up a thread. The expressions built here are only
    // escaped literal text joined by ".*" and ".", and they run against single file names,
    // so matching is quick in practice. The timeout guarantees that for any query.
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly char[] Wildcards = ['*', '?'];

    private readonly Regex? _glob;

    /// <summary>
    /// The query with spaces at the ends removed, for the search reply to repeat.
    /// </summary>
    public string Query { get; }

    /// <summary>True when the query is empty, in which case the pattern matches nothing.</summary>
    public bool IsEmpty => _glob is null && Query.Length == 0;

    /// <summary>
    /// Builds the pattern from a query. Spaces at the ends are removed first. When the
    /// query holds a wildcard, it is escaped and turned into an anchored regular
    /// expression, with * becoming ".*" and ? becoming ".".
    /// </summary>
    /// <param name="query">What the person typed in the search box.</param>
    public NamePattern(string? query)
    {
        Query = (query ?? string.Empty).Trim();
        if (Query.IndexOfAny(Wildcards) >= 0)
        {
            _glob = new Regex(
                "^" + Regex.Escape(Query).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal) + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                MatchTimeout);
        }
    }

    /// <summary>Returns true when a name matches; always false for an empty query.</summary>
    /// <param name="name">A file or folder name on its own, without the folders above it.</param>
    public bool Matches(string name)
    {
        if (IsEmpty)
        {
            return false;
        }
        return _glob is null
            ? name.Contains(Query, StringComparison.OrdinalIgnoreCase)
            : _glob.IsMatch(name);
    }
}
