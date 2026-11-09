namespace JobHunt.Domain.Shared.Writing;

/// <summary>
/// A reply that asks to stop (the review, F11): the company is marked do-not-email with the date and the reply run
/// drafts nothing for the thread. Matched as phrases in the reply's own text, above any quoted original, case
/// apart; over-matching stops a company, which is the safe direction.
/// </summary>
public static class StopPhrases
{
    /// <summary>The phrases, lower case.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        "not interested", "do not contact", "don't contact", "unsubscribe", "remove me", "no thanks", "no thank you", "stop emailing", "take me off",
    ];

    /// <summary>The first stop phrase in the reply's own lines, or null.</summary>
    public static string? Find(string reply)
    {
        string own = Own(reply).ToLowerInvariant().Replace('’', '\'');
        return All.FirstOrDefault(phrase => own.Contains(phrase, StringComparison.Ordinal));
    }

    /// <summary>The reply above the quoted original: up to the first "On ... wrote:" line or the first line quoted with "&gt;".</summary>
    public static string Own(string reply)
    {
        var kept = new List<string>();
        foreach (string line in (reply ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith('>') || (trimmed.StartsWith("On ", StringComparison.Ordinal) && trimmed.TrimEnd().EndsWith("wrote:", StringComparison.Ordinal)))
            {
                break;
            }
            kept.Add(line);
        }
        return string.Join('\n', kept);
    }
}
