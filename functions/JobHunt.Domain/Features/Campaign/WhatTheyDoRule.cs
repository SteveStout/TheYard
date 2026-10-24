using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Campaign;

/// <summary>
/// The {WhatTheyDo} clause the lead run writes, held to the sentence it lands in: "I saw that {Company}
/// {WhatTheyDo} and is hiring" (the review, F15). One plain clause: a lowercase third-person verb from the list,
/// 20 to 110 characters, no end punctuation, no comma clause longer than the phrase before it, no dash, quote
/// marks, braces or address, not the company's own name, and no tell. A row that fails stays in Leads with
/// Blocked = "WhatTheyDo: rule" and never sends; the lead run runs the same check before it writes.
/// </summary>
public static partial class WhatTheyDoRule
{
    /// <summary>The verbs a clause may open with.</summary>
    public static readonly IReadOnlyList<string> Verbs =
    [
        "builds", "makes", "runs", "sells", "provides", "develops", "operates", "offers",
        "serves", "insures", "lends", "manages", "processes", "hosts", "ships",
    ];

    /// <summary>The shortest clause, in characters.</summary>
    public const int Shortest = 20;

    /// <summary>The longest clause, in characters.</summary>
    public const int Longest = 110;

    /// <summary>Null when the clause passes, otherwise "WhatTheyDo: " and the first rule it breaks.</summary>
    public static string? Check(string? clause, IReadOnlyList<string> companyNames)
    {
        string text = clause ?? string.Empty;
        string? broken = Broken(text, companyNames);
        return broken is null ? null : "WhatTheyDo: " + broken;
    }

    private static string? Broken(string text, IReadOnlyList<string> companyNames)
    {
        if (text.Trim().Length == 0)
        {
            return "empty";
        }
        if (text != text.Trim())
        {
            return "spaces at an end";
        }
        string first = text.Split(' ')[0];
        if (!Verbs.Contains(first, StringComparer.Ordinal))
        {
            return $"opens with \"{first}\", not a verb from the list";
        }
        if (text.Length < Shortest || text.Length > Longest)
        {
            return $"{text.Length} characters, not {Shortest} to {Longest}";
        }
        if (".!?;:,".Contains(text[^1], StringComparison.Ordinal))
        {
            return "ends in punctuation";
        }
        if (text.IndexOfAny(['\u2014', '\u2013']) >= 0 || text.Contains(" - ", StringComparison.Ordinal) || text.Contains("--", StringComparison.Ordinal))
        {
            return "a dash";
        }
        if (text.IndexOfAny(['"', '\u201C', '\u201D', '\u2018', '\u2019', '`']) >= 0)
        {
            return "quote marks";
        }
        if (text.IndexOfAny(['{', '}']) >= 0)
        {
            return "braces";
        }
        if (Address().IsMatch(text))
        {
            return "an address";
        }
        int comma = text.IndexOf(',', StringComparison.Ordinal);
        if (comma >= 0 && text.Length - comma - 1 > comma)
        {
            return "a comma clause longer than the phrase before it";
        }
        string? named = companyNames.FirstOrDefault(name => name.Trim().Length > 0 && Words(text).Contains(Words(name), StringComparison.Ordinal));
        if (named is not null)
        {
            return $"names the company ({named})";
        }
        var tells = Tells.Find(text);
        return tells.Count > 0 ? $"a tell ({string.Join(", ", tells)})" : null;
    }

    private static string Words(string text) =>
        " " + Space().Replace(NotWord().Replace(text.ToLowerInvariant(), " "), " ").Trim() + " ";

    [GeneratedRegex(@"https?://|www\.|\b[\w-]+\.(com|io|net|org|ai|co|dev|app)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Address();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Space();
}
