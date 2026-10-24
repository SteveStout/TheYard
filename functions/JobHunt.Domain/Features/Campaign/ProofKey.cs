namespace JobHunt.Domain.Features.Campaign;

/// <summary>
/// One proof per posting (the review, 8.2 item 4): the lead run picks a key from the posting's words and code
/// holds the words, so no model writes a line of the email. Each is a clause that follows "in .NET architecture
/// or technical management," the way the approved templates' AI clause does, and each fact is a line of the
/// 2026-10-24 resume (Conway: 200+ releases on a live financial platform, sole engineer for the last three
/// years; Storee: four applications onto one .NET 9 platform on Azure, release cycle cut 50 %; a four-person team
/// hired and led). A key that is missing or unknown renders the AI clause, which is the one both v1 templates
/// already carry. Neither template has a {Proof} line yet: putting one in is a template change, so it waits for
/// Steve's yes, and until then these are rendered by no email.
/// </summary>
public static class ProofKey
{
    /// <summary>The Conway record.</summary>
    public const string Reliability = "reliability";

    /// <summary>The Storee platform.</summary>
    public const string Platform = "platform";

    /// <summary>The team he built.</summary>
    public const string Leadership = "leadership";

    /// <summary>The AI automation in his own work; the default.</summary>
    public const string Ai = "ai";

    /// <summary>Each key's clause.</summary>
    public static readonly IReadOnlyDictionary<string, string> Sentences = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Reliability] = "along with more than 200 releases I carried on a live financial platform, the last three years as its sole engineer",
        [Platform] = "along with the four applications I consolidated onto one .NET platform on Azure, which cut the release cycle in half",
        [Leadership] = "along with the four-person engineering team I hired and led",
        [Ai] = "along with the AI automation I have been building into my own work",
    };

    /// <summary>The keys, in the order the lead run's prompt lists them.</summary>
    public static IReadOnlyList<string> Keys { get; } = [Reliability, Platform, Leadership, Ai];

    /// <summary>The clause for a key; the AI clause for a missing or unknown one.</summary>
    public static string Sentence(string? key) =>
        key is not null && Sentences.TryGetValue(key.Trim().ToLowerInvariant(), out string? sentence) ? sentence : Sentences[Ai];
}
