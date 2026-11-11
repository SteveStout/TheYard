using System.Text.RegularExpressions;

namespace JobHunt.Domain.Shared.Postings;

/// <summary>
/// The level a posting's title names, read by code from the title alone: Principal, Staff, Director, Architect,
/// Manager, Lead, Senior, Mid or Unknown, the strongest word winning ("Senior Staff" is Staff, "Senior Manager" is
/// Manager, "Lead Architect" is Architect). "Management" is a level only as "engineering management": a field named
/// with it ("Identity and Access Management", "Product Management") is the seat's subject, not its rung. The board scan stores it on every posting and the night's picker gates
/// on it, so both read one rule. A rank puts the levels on one ladder, so a re-post one rung down is found by
/// arithmetic and not by a list of pairs.
/// </summary>
public static partial class PostingLevel
{
    /// <summary>Principal, Distinguished or Fellow.</summary>
    public const string Principal = "Principal";
    /// <summary>Staff, Senior Staff included.</summary>
    public const string Staff = "Staff";
    /// <summary>Director, Head of or VP.</summary>
    public const string Director = "Director";
    /// <summary>Architect.</summary>
    public const string Architect = "Architect";
    /// <summary>Manager.</summary>
    public const string Manager = "Manager";
    /// <summary>Lead.</summary>
    public const string Lead = "Lead";
    /// <summary>Senior or Sr.</summary>
    public const string Senior = "Senior";
    /// <summary>Mid, II or III, or an engineer or developer title with no level word.</summary>
    public const string Mid = "Mid";
    /// <summary>No level word and no engineering word.</summary>
    public const string Unknown = "Unknown";

    /// <summary>The level a title names.</summary>
    public static string Of(string? title)
    {
        string t = title ?? string.Empty;
        if (Word(t, "principal|distinguished|fellow")) { return Principal; }
        if (Word(t, "staff")) { return Staff; }
        if (Word(t, "director|head of|vp|vice president")) { return Director; }
        if (Word(t, "architect")) { return Architect; }
        if (Word(t, "manager|engineering manager|engineering management")) { return Manager; }
        if (Word(t, "lead|tech lead|team lead")) { return Lead; }
        if (Word(t, "senior|sr|snr")) { return Senior; }
        if (Word(t, "mid|intermediate|ii|iii|2|3") || Word(t, "engineer|developer")) { return Mid; }
        return Unknown;
    }

    /// <summary>
    /// A level's rung: Mid 1, Senior 2, Staff, Lead, Architect and Manager 3, Principal and Director 4, Unknown 0. Staff
    /// and Senior are one rung apart, which is the down-level a re-post is watched for.
    /// </summary>
    public static int Rank(string level) => level switch
    {
        Mid => 1,
        Senior => 2,
        Staff or Lead or Architect or Manager => 3,
        Principal or Director => 4,
        _ => 0,
    };

    /// <summary>The title with its level words, numerals and punctuation taken out, lower case: what a re-post keeps.</summary>
    public static string Base(string? title) =>
        string.Join(' ', Levels().Replace((title ?? string.Empty).ToLowerInvariant(), " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => word.Any(char.IsLetterOrDigit)));

    private static bool Word(string title, string words) =>
        Regex.IsMatch(title, $@"(?<![A-Za-z])({words})(?![A-Za-z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    [GeneratedRegex(@"\b(principal|distinguished|staff|senior|sr|snr|lead|director|head of|manager|mid|intermediate|i{1,3}|iv|[1-4])\b|[^a-z0-9#+. ]", RegexOptions.CultureInvariant)]
    private static partial Regex Levels();
}
