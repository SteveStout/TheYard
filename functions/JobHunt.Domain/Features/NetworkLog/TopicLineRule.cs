namespace JobHunt.Domain.Features.NetworkLog;

/// <summary>One row the 4 AM lane wrote: whose, the clause, the file it came from, and the post and code when it named one.</summary>
/// <param name="PersonKey">The row it is for.</param>
/// <param name="TopicLine">The clause, empty when the lane wrote only a post and code.</param>
/// <param name="Source">The share path of the file, which sorts by day and hour.</param>
/// <param name="BlogSlug">The post's slug, when the lane named one.</param>
/// <param name="RefCode">The code make_ref.py minted for it.</param>
public sealed record TopicRow(string PersonKey, string TopicLine, string Source, string? BlogSlug = null, string? RefCode = null);

/// <summary>
/// What a TopicLine must be before a draft is written from it: one plain clause of 12 to 160 characters on one
/// line, with no dash of the model's kind, none of the tells, no question or second sentence, not the subject over
/// again and not a link. A line that fails is logged and skipped, never fixed, and the row waits for a better one.
/// </summary>
public static class TopicLineRule
{
    /// <summary>The shortest clause that says anything.</summary>
    public const int Shortest = 12;

    /// <summary>The longest clause the template's sentence carries.</summary>
    public const int Longest = 160;

    /// <summary>The check the line fails, or null when it may be used.</summary>
    /// <param name="line">The clause as written.</param>
    /// <param name="subject">The thread's subject, which the clause must not repeat.</param>
    public static string? Fails(string line, string? subject)
    {
        string text = (line ?? string.Empty).Trim();
        if (text.Length < Shortest || text.Length > Longest)
        {
            return "length";
        }
        if (text.IndexOfAny(['\r', '\n', '\t']) >= 0)
        {
            return "line break";
        }
        if (text.Contains('\u2014', StringComparison.Ordinal) || text.Contains('\u2013', StringComparison.Ordinal))
        {
            return "dash";
        }
        if (Tells.Find(text).Count > 0)
        {
            return "tell";
        }
        if (text.IndexOfAny(['?', '!', ';']) >= 0 || text.TrimEnd('.').Contains(". ", StringComparison.Ordinal))
        {
            return "not one clause";
        }
        if (text.Contains("http", StringComparison.OrdinalIgnoreCase) || text.Contains("www.", StringComparison.OrdinalIgnoreCase))
        {
            return "link";
        }
        string bare = DraftTemplate.Subject(subject ?? string.Empty)[4..].Trim();
        return bare.Length > 0 && string.Equals(text.TrimEnd('.'), bare, StringComparison.OrdinalIgnoreCase) ? "the subject" : null;
    }

    /// <summary>
    /// The rows of one lane file: PersonKey, a tab, the clause, and optionally a tab, the post's slug, a tab and its
    /// code; the clause may be empty when the slug and code are given. A header or a blank line is skipped.
    /// </summary>
    public static IReadOnlyList<TopicRow> Parse(string text, string source) =>
        [.. (text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .Select(line => line.Split('\t'))
            .Where(f => (f.Length == 2 || f.Length == 4) && f[0].Trim().Length > 0 && f[0].Trim() != "PersonKey")
            .Select(f => f.Length == 4 && f[2].Trim().Length > 0 && f[3].Trim().Length > 0
                ? new TopicRow(f[0].Trim().ToLowerInvariant(), f[1].Trim(), source, f[2].Trim(), f[3].Trim())
                : new TopicRow(f[0].Trim().ToLowerInvariant(), f[1].Trim(), source))
            .Where(t => t.TopicLine.Length > 0 || t.BlogSlug is not null)];
}
