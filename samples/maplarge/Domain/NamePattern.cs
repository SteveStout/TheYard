using System.Text.RegularExpressions;

namespace TestProject.Domain;

/// <summary>
/// What a search matches a name against (ADR-004). Two shapes, chosen by whether
/// the query carries a wildcard: <c>*.md</c> or <c>report?</c> is a glob over the
/// whole name, and <c>invoice</c> is a substring. Both ignore case, because a
/// person searching a folder is looking for a thing, not a spelling.
/// </summary>
public sealed class NamePattern
{
    // A regular expression built from user input gets a timeout, so a query can
    // never hold a thread; the globs this class builds are linear anyway, and the
    // timeout is the proof rather than the fix.
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private static readonly char[] Wildcards = ['*', '?'];

    private readonly Regex? _glob;
    private readonly string _substring;

    /// <summary>The query exactly as it was given, for the reply to echo.</summary>
    public string Query { get; }

    /// <summary>True when the pattern can match something; an empty query matches nothing.</summary>
    public bool IsEmpty => _glob is null && _substring.Length == 0;

    /// <summary>Builds the pattern from a query; whitespace at the ends is not part of it.</summary>
    /// <param name="query">What the person typed.</param>
    public NamePattern(string? query)
    {
        Query = (query ?? string.Empty).Trim();
        _substring = Query;
        if (Query.IndexOfAny(Wildcards) >= 0)
        {
            _glob = new Regex(
                "^" + Regex.Escape(Query).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal) + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                MatchTimeout);
        }
    }

    /// <summary>True when a name matches.</summary>
    /// <param name="name">A file or folder name, without its path.</param>
    public bool Matches(string name)
    {
        if (IsEmpty)
        {
            return false;
        }
        return _glob is null
            ? name.Contains(_substring, StringComparison.OrdinalIgnoreCase)
            : _glob.IsMatch(name);
    }
}
