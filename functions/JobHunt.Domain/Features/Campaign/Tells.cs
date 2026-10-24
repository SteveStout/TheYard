using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Campaign;

/// <summary>
/// The model's voice, as words and phrases: S1_WORDS and S1_PHRASES of ai_tells_five_signs.py in the job-hunt
/// folder, ported to one constant (the review, F15). TellsTests reads the checked-in copy of the Python file and
/// fails when the two lists differ by count or content, so the Python gate and this one cannot drift. A word
/// matches whole and case-insensitive (the Python's \b word \b over lowercased text); a phrase matches anywhere.
/// </summary>
public static partial class Tells
{
    /// <summary>S1_WORDS, in the Python file's order.</summary>
    public static readonly IReadOnlyList<string> Words =
    [
        "leverage", "leveraged", "leverages", "leveraging",
        "pivoted", "pivoting", "spearhead", "spearheaded", "spearheading",
        "orchestrate", "orchestrated", "orchestrating",
        "streamline", "streamlined", "streamlining",
        "synergy", "synergies", "synergistic", "robust", "delve", "delved", "delving",
        "foster", "fostered", "fostering", "facilitate", "facilitated", "facilitating",
        "myriad", "plethora", "tapestry", "realm", "landscape",
        "testament", "embark", "embarked", "vibrant", "crucial", "pivotal",
        "cultivate", "cultivated", "cultivating",
        "holistic", "seamless", "seamlessly", "cutting-edge", "best-in-class",
        "results-driven", "meticulous", "meticulously", "utilize", "utilized", "utilizing",
        "passionate", "innovative", "empower", "empowered", "empowering",
        "elevate", "elevated",
    ];

    /// <summary>S1_PHRASES, in the Python file's order.</summary>
    public static readonly IReadOnlyList<string> Phrases =
    [
        "leverage synergies", "optimize outcomes", "proven track record",
        "deep dive", "drive impact", "drive value", "drive results",
        "wide range of", "fast-paced environment", "wearing many hats",
    ];

    private static readonly Regex WordPattern = new(
        @"\b(" + string.Join('|', Words.Select(Regex.Escape)) + @")\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Every tell in the text, words then phrases, lowercased, in the order found.</summary>
    public static IReadOnlyList<string> Find(string text)
    {
        string low = (text ?? string.Empty).ToLowerInvariant();
        var hits = WordPattern.Matches(low).Select(m => m.Value).ToList();
        hits.AddRange(Phrases.Where(phrase => low.Contains(phrase, StringComparison.Ordinal)));
        return hits;
    }
}
