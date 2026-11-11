using System.Text.RegularExpressions;

namespace JobHunt.Server.Domain.Shared.Worklists;

/// <summary>
/// The OUTREACH IS SHORT checks a drafted text must pass before a lane may paste it: no em dash, sixty words or fewer,
/// under 300 characters for an invite note, one tagged link and nothing else linked, one no-oriented question, and no
/// word of a live process, because anything specific to one is held for his yes. A role that went live in the last 21
/// days of a year (the pounce's budget flag) may ask the timeline question in place of the no-oriented one, because a
/// seat opened then is often money that must be spent before the year closes; no other row may. A text that fails is
/// dropped with its reasons; the program never edits one. The [code] and [First name] placeholders count
/// as they stand, which is at least as long as what fills them.
/// </summary>
public static partial class TextCheck
{
    /// <summary>The most words a text may hold.</summary>
    public const int MostWords = 60;

    /// <summary>An invite note must be shorter than this.</summary>
    public const int InviteCap = 300;

    /// <summary>The one question a budget-flagged row may ask in place of the no-oriented one.</summary>
    public const string Timeline = "What does your timeline look like for getting someone started?";

    private const char EmDash = (char)0x2014;

    /// <summary>Every check the text fails, empty when it passes.</summary>
    /// <param name="text">The drafted text.</param>
    /// <param name="invite">True for an invite note.</param>
    /// <param name="budget">True for a row whose role carries the budget flag, which may ask <see cref="Timeline"/>.</param>
    public static IReadOnlyList<string> Problems(string text, bool invite, bool budget = false)
    {
        var problems = new List<string>();
        if (text.Contains(EmDash, StringComparison.Ordinal))
        {
            problems.Add("em dash");
        }
        int words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        if (words > MostWords)
        {
            problems.Add($"{words} words");
        }
        if (invite && text.Length >= InviteCap)
        {
            problems.Add($"{text.Length} characters");
        }
        int tagged = Tagged().Matches(text).Count;
        int bare = Url().Matches(Tagged().Replace(text, string.Empty)).Count;
        if (tagged != 1 || bare != 0)
        {
            problems.Add($"{tagged} tagged links, {bare} other links");
        }
        string prose = Tagged().Replace(text, string.Empty);
        int questions = prose.Count(c => c == '?');
        if (questions != 1 || !(NoOriented().IsMatch(prose) || (budget && prose.Contains(Timeline, StringComparison.Ordinal))))
        {
            problems.Add($"{questions} questions, no no-oriented ask");
        }
        if (LiveProcess().IsMatch(prose) || Meets().IsMatch(prose))
        {
            problems.Add("names a live process");
        }
        return problems;
    }

    /// <summary>The code a text's tagged link carries, or an empty string for a [code] placeholder or no link.</summary>
    /// <param name="text">The text.</param>
    public static string Code(string text)
    {
        Match match = Tagged().Match(text);
        string code = match.Success ? match.Groups["code"].Value : string.Empty;
        return code == "[code]" ? string.Empty : code;
    }

    [GeneratedRegex(@"(?:https?://)?theyard\.stevenstout\.biz/\?r=(?<code>[a-z0-9]{6}|\[code\])", RegexOptions.IgnoreCase)]
    private static partial Regex Tagged();

    [GeneratedRegex(@"https?://|www\.|\b[\w-]+\.(com|biz|io|net|org)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Url();

    [GeneratedRegex(@"\b(in process|interview\w*|phone screen|recruiter screen|our (call|conversation|chat)|a call|schedul(?:e|ed|ing)|looking forward)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LiveProcess();

    [GeneratedRegex(@"\b[Mm]eet(ing)? (with )?[A-Z]")]
    private static partial Regex Meets();

    [GeneratedRegex(@"bad (idea|time)|against|ridiculous|opposed", RegexOptions.IgnoreCase)]
    private static partial Regex NoOriented();
}
