using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Worklists;

/// <summary>One comment of Steve's on someone's post, as the comment-line lane read it off his activity page.</summary>
/// <param name="CommentUrl">The comment's own address when the page gives one, else empty: the edit finds the comment on the activity page by its author and its words.</param>
/// <param name="PostAuthor">The post's author as the post shows the name.</param>
/// <param name="Company">The author's company, empty when the post does not show one.</param>
/// <param name="Topic">The post's topic in a few words.</param>
/// <param name="Text">Steve's comment, word for word, "see more" expanded.</param>
/// <param name="Age">The comment's age as LinkedIn shows it: 5m, 3h, 1d, 2w.</param>
/// <param name="OwnPost">True when the post is Steve's own.</param>
/// <param name="Hardship">True when the post is about a layoff, a death, an illness or another hardship.</param>
/// <param name="Fit">Which of his areas the post is about, or none.</param>
/// <param name="Tie">The clause that finishes "I've got a portfolio that", true to the post's topic.</param>
public sealed record OwnComment(
    [property: JsonPropertyName("comment_url")] string? CommentUrl,
    [property: JsonPropertyName("post_author")] string? PostAuthor,
    [property: JsonPropertyName("author_company")] string? Company,
    [property: JsonPropertyName("post_topic")] string? Topic,
    [property: JsonPropertyName("comment_text")] string? Text,
    [property: JsonPropertyName("age")] string? Age,
    [property: JsonPropertyName("own_post")] bool? OwnPost,
    [property: JsonPropertyName("hardship")] bool? Hardship,
    [property: JsonPropertyName("fit")] string? Fit,
    [property: JsonPropertyName("tie")] string? Tie);

/// <summary>
/// The skip rules of the comment portfolio line (CONVENTIONS, 20261119-141000Z): never on his own post, never twice on one
/// comment, never on a post about a layoff, a death, an illness or a hardship, never under a pure reaction, and only where
/// his work honestly ties in. The model's flags count, and so do the words: a hardship word in the post or the comment skips
/// it whatever the model said, and a fit the model claims needs a word of that area in the post or the comment.
/// </summary>
public static partial class CommentSkips
{
    /// <summary>The areas his work ties to, as the model names them in fit.</summary>
    public static readonly IReadOnlyList<string> Areas = [".NET", "C#", "SQL", "Azure", "architecture", "AI-assisted code quality"];

    /// <summary>Why the comment gets no line, or null when it may be rolled.</summary>
    /// <param name="comment">The comment.</param>
    /// <param name="hours">How far back the run reads.</param>
    public static string? Why(OwnComment comment, int hours)
    {
        string text = comment.Text ?? string.Empty, words = $"{comment.Topic} {text}";
        return text.Trim().Length == 0 || (comment.PostAuthor ?? string.Empty).Trim().Length == 0 ? "needs his words and the post's author, to find it again"
            : Hours(comment.Age) is not { } age ? $"age \"{comment.Age}\" does not read as LinkedIn's 5m, 3h, 1d or 2w"
            : age > hours ? $"older than {hours} hours ({comment.Age})"
            : comment.OwnPost != false || Steve().IsMatch(comment.PostAuthor ?? string.Empty) ? "a comment on his own post"
            : text.Contains("theyard.stevenstout.biz", StringComparison.OrdinalIgnoreCase) ? "already holds the Yard link"
            : comment.Hardship != false || Hardship().IsMatch(words) ? "a post about a layoff, a death, an illness or a hardship"
            : Reaction(text) ? "a pure reaction"
            : !Areas.Contains(comment.Fit ?? string.Empty, StringComparer.OrdinalIgnoreCase) || !Fits().IsMatch(words) ? "his work does not tie in"
            : CommentPicks.Clause(comment.Tie) is null ? "the tie is not one plain clause"
            : null;
    }

    /// <summary>True for a pure reaction: nothing but emoji, a reach comment, or a few words of congratulation or agreement.</summary>
    /// <param name="text">The comment.</param>
    public static bool Reaction(string text)
    {
        string plain = NotWords().Replace(text, " ").Trim();
        if (plain.Length == 0 || Reach().IsMatch(plain))
        {
            return true;
        }
        int count = plain.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return count <= 6 && Cheer().IsMatch(plain);
    }

    /// <summary>The age in hours, or null when it is not one LinkedIn shows: now, 5m, 3h, 1d, 2w, 1mo, 1yr.</summary>
    /// <param name="age">The age as read.</param>
    public static int? Hours(string? age)
    {
        string trimmed = (age ?? string.Empty).Trim().ToLowerInvariant();
        if (trimmed is "now" or "just now")
        {
            return 0;
        }
        Match match = Age().Match(trimmed);
        if (!match.Success)
        {
            return null;
        }
        int n = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return match.Groups[2].Value switch
        {
            "m" => 0,
            "h" => n,
            "d" => n * 24,
            "w" => n * 24 * 7,
            "mo" => n * 24 * 30,
            _ => n * 24 * 365,
        };
    }

    [GeneratedRegex(@"^(\d{1,3})\s*(m|h|d|w|mo|yr|y)$")]
    private static partial Regex Age();

    [GeneratedRegex(@"^\s*stev(e|en)\s+(w\.?\s+)?stout\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Steve();

    [GeneratedRegex(@"\b(lay\s?offs?|laid off|redundan\w*|rif\b|passed away|death|died|funeral|memorial|rest in peace|rip\b|condolences?|grie\w*|cancer|illness|ill\b|diagnos\w*|hospital\w*|surgery|chemo\w*|hardships?|lost my job|lost his job|lost her job|lost their job)", RegexOptions.IgnoreCase)]
    private static partial Regex Hardship();

    [GeneratedRegex(@"(\.net\b|dotnet|asp\.net|c#|csharp|\bsql\b|t-sql|postgres\w*|database\w*|\bindex(es|ing)?\b|quer(y|ies)|ef core|entity framework|dapper|azure|architect\w*|monolith\w*|microservices?|cqrs|event sourc\w*|domain-driven|\bddd\b|system design|distributed systems?|code quality|code review\w*|ai-assisted|ai assisted|ai coding|ai-generated code|copilot|claude|cursor|vibe cod\w*|\bllms?\b|technical debt|refactor\w*)", RegexOptions.IgnoreCase)]
    private static partial Regex Fits();

    [GeneratedRegex(@"[^\p{L}\p{N}#'\s]")]
    private static partial Regex NotWords();

    [GeneratedRegex(@"\b(commenting for (better )?(reach|visibility)|for (better )?reach|for visibility|boosting this)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Reach();

    [GeneratedRegex(@"\b(congrat\w*|well deserved|lol|lmao|haha\w*|love (this|it)|great (post|news|share|stuff)|awesome|amazing|so true|agreed|exactly|thanks for sharing|thank you for sharing|insightful|happy for you|bravo|kudos|wow|nice|100)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Cheer();
}
