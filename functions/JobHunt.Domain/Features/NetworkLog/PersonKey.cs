using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.NetworkLog;

/// <summary>
/// The one key a conversation is stored under: the lower-case address for an email row, linkedin:&lt;slug&gt; for a
/// LinkedIn row, so the same person read twice on one channel is one row.
/// </summary>
public static partial class PersonKey
{
    /// <summary>The key of an email row: the address in the From header, lower case.</summary>
    public static string ForEmail(string from) => Mail.Address(from);

    /// <summary>The key of a LinkedIn row from its profile URL or bare slug, or empty when neither is there.</summary>
    public static string ForLinkedIn(string profile)
    {
        string text = (profile ?? string.Empty).Trim().TrimEnd('/');
        var slug = Slug().Match(text);
        string value = (slug.Success ? slug.Groups[1].Value : text).Trim('/').ToLowerInvariant();
        return value.Length == 0 || value.Contains('/', StringComparison.Ordinal) ? string.Empty : "linkedin:" + value;
    }

    [GeneratedRegex(@"linkedin\.com/in/([^/?#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Slug();
}

/// <summary>
/// The key that joins one person across channels: first and last name and the company's token, ASCII and lower
/// case ("pat lee|acme"). An email row's company token is its registrable domain's first label; a LinkedIn row's is
/// the company name without its legal suffix. No company, or no last name, gives no key, so nothing joins on a
/// guess.
/// </summary>
public static partial class MatchKey
{
    private static readonly HashSet<string> NameSuffixes = new(StringComparer.Ordinal) { "jr", "sr", "ii", "iii", "iv", "phd", "mba", "pmp", "cpa", "md", "esq" };

    private static readonly HashSet<string> LegalSuffixes = new(StringComparer.Ordinal) { "inc", "llc", "ltd", "corp", "corporation", "co", "company", "plc", "gmbh", "lp", "llp", "group" };

    /// <summary>The key, or null when the name has no last name or the company gives no token.</summary>
    public static string? Of(string fullName, string companyToken)
    {
        var words = Words(Ordered(fullName)).Where(w => !NameSuffixes.Contains(w)).ToList();
        string company = string.Concat(Words(companyToken).Where(w => !LegalSuffixes.Contains(w)));
        return words.Count < 2 || company.Length == 0 ? null : $"{words[0]} {words[^1]}|{company}";
    }

    /// <summary>The company token of an address: its registrable domain's first label, or empty for a personal domain.</summary>
    public static string TokenOfAddress(string address)
    {
        string key = CompanyKey.Of(address);
        return key.Length == 0 || PersonalDomains.All.Contains(key) ? string.Empty : key.Split('.')[0];
    }

    /// <summary>The name's first word, capitalised as written, for the greeting; empty when there is none.</summary>
    public static string FirstName(string fullName)
    {
        string first = Ordered(fullName).Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return first.Length > 0 && first.All(c => char.IsLetter(c) || c is '-' or '\'') ? first : string.Empty;
    }

    /// <summary>The name in reading order: "Lee, Pat" is a directory's order and reads as "Pat Lee".</summary>
    private static string Ordered(string fullName)
    {
        string name = (fullName ?? string.Empty).Trim().Trim('"');
        int comma = name.IndexOf(',', StringComparison.Ordinal);
        if (comma < 0)
        {
            return name;
        }
        string after = name[(comma + 1)..].Trim();
        // "Pat Lee, PhD" keeps its order; only a single word after the comma is a given name.
        return after.Contains(' ', StringComparison.Ordinal) || NameSuffixes.Contains(after.ToLowerInvariant().Trim('.')) ? name[..comma].Trim() : $"{after} {name[..comma].Trim()}";
    }

    private static IEnumerable<string> Words(string text)
    {
        string flat = new([.. (text ?? string.Empty).Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)]);
        return NonWord().Split(flat.ToLowerInvariant()).Where(w => w.Length > 0 && w.All(c => c < 128));
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonWord();
}
