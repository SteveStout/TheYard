using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Jobs;

/// <summary>
/// The location gate under RULE 76 (hybrid and onsite roles are in; remote is negotiated once a human is on the other
/// side): a US role passes whatever its arrangement, one abroad never does, and the picker ranks remote first,
/// St. Louis hybrid second and every other US role third.
/// </summary>
public static partial class JobsGates
{
    /// <summary>
    /// RULE 76 check 1: the location gate's failure, or null. Remote and St. Louis hybrid pass as before; a hybrid,
    /// onsite or unsaid arrangement passes when its place is in the US (a state, a state's code or the country) and
    /// fails "outside the US" when its place ends in anything else; a place that names no state or country keeps the
    /// read as its failure, because a role that cannot be shown to be in the US is not applied to.
    /// </summary>
    /// <param name="remote">What RemoteRead gave.</param>
    /// <param name="location">The location as written.</param>
    public static string? LocationFailure(string remote, string location)
    {
        string where = (location ?? string.Empty).Trim();
        if (remote.StartsWith(NamedStates, StringComparison.Ordinal))
        {
            return remote;
        }
        if (Arrangement(remote) < 2 || InUs(where))
        {
            return null;
        }
        return where.Contains(',', StringComparison.Ordinal) ? "outside the US: " + where
            : remote == CountryOnly ? $"not remote: {where}, and the title and text never say remote"
            : remote;
    }

    /// <summary>RULE 76 check 2's group: 0 fully remote, 1 St. Louis-area hybrid, 2 every other US hybrid or onsite role.</summary>
    /// <param name="remote">What RemoteRead gave.</param>
    public static int Arrangement(string remote) => remote switch
    {
        "remote" => 0,
        "hybrid, days unknown" => 1,
        _ when remote.StartsWith("St. Louis hybrid", StringComparison.Ordinal) => 1,
        _ => 2,
    };

    private static bool InUs(string where) =>
        Country().IsMatch(where) || UsWords().IsMatch(where) || UsStateName().IsMatch(where)
        || (where.Split(',') is { Length: > 1 } parts && States.Contains(Parenthesis().Replace(parts[^1], string.Empty).Trim().ToUpperInvariant()));

    [GeneratedRegex(@"\b(United States|USA|U\.S\.A?\.?)(?![A-Za-z])|\bUS\b|\bBay Area\b|\bNew York City\b|\bNYC\b|\bWashington,? D\.?C\.?", RegexOptions.IgnoreCase)]
    private static partial Regex UsWords();

    [GeneratedRegex(@"\b(Alabama|Alaska|Arizona|Arkansas|California|Colorado|Connecticut|Delaware|Florida|Georgia|Hawaii|Idaho|Illinois|Indiana|Iowa|Kansas|Kentucky|Louisiana|Maine|Maryland|Massachusetts|Michigan|Minnesota|Mississippi|Missouri|Montana|Nebraska|Nevada|New Hampshire|New Jersey|New Mexico|New York|North Carolina|North Dakota|Ohio|Oklahoma|Oregon|Pennsylvania|Rhode Island|South Carolina|South Dakota|Tennessee|Texas|Utah|Vermont|Virginia|Washington|West Virginia|Wisconsin|Wyoming)\b")]
    private static partial Regex UsStateName();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Parenthesis();
}
