using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Jobs;

/// <summary>
/// The resume's headline as build_tailored_resume.py on the share makes it, so the picker knows at pick time which
/// rows the 06:00 build will refuse. The title loses en and em dashes for hyphens; a bracket that holds a location or a
/// requisition code goes, any other bracket stays as words; only the first segment before a spaced dash, a bar, a
/// semicolon or a middle dot is kept (the second when the first is a code), and only its first comma part; trailing
/// code tokens go; the rest is in capitals. The builder stops on an empty headline, on a character outside ASCII and
/// on one longer than <see cref="MaxChars"/>, and never shortens a title, so each of those holds the row for Steve.
/// The two patterns are the builder's LOCATION and REQ, character for character.
/// </summary>
public static partial class ResumeHeadline
{
    /// <summary>The builder's MAX_CHARS: past this the headline band cannot hold the line at 16 pt.</summary>
    public const int MaxChars = 48;

    /// <summary>The headline the builder makes of a posting title, or empty when nothing is left.</summary>
    public static string Clean(string title)
    {
        string t = Spaces().Replace((title ?? string.Empty).Replace('–', '-').Replace('—', '-'), " ").Trim();
        t = Bracket().Replace(t, m =>
        {
            string inner = m.Groups[1].Value.Trim();
            return inner.Length == 0 || Location().IsMatch(inner) || Req().IsMatch(inner) ? " " : " (" + inner + ") ";
        });
        var parts = Segment().Split(t).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        string? kept = parts.Count == 0 ? null : !Req().IsMatch(parts[0]) ? parts[0] : parts.Count > 1 ? parts[1] : null;
        string? head = kept?.Split(',').Select(b => b.Trim()).FirstOrDefault(b => b.Length > 0);
        if (head is null)
        {
            return string.Empty;
        }
        var words = head.Split(' ').ToList();
        while (words.Count > 1 && Req().IsMatch(words[^1]))
        {
            words.RemoveAt(words.Count - 1);
        }
        string headline = Spaces().Replace(string.Join(' ', words), " ").Replace("( ", "(", StringComparison.Ordinal).Replace(" )", ")", StringComparison.Ordinal).Trim(' ', '-', '/', ',');
        return headline.ToUpperInvariant();
    }

    /// <summary>Why the builder will refuse the title's headline, in the words a NEEDS STEVE row carries, or null when it fits.</summary>
    public static string? Hold(string title)
    {
        string headline = Clean(title);
        return headline.Length == 0 ? "headline: nothing left once the codes and locations are out"
            : headline.Any(ch => ch > '\u007f') ? "headline: a character outside ASCII"
            : headline.Length > MaxChars ? $"headline: {headline.Length} characters"
            : null;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"[\(\[]([^\)\]]*)[\)\]]")]
    private static partial Regex Bracket();

    [GeneratedRegex(@"\s+-\s*|-\s+|\||;|·")]
    private static partial Regex Segment();

    [GeneratedRegex(@"^(?:(?:100%\s*)?remote|hybrid|on-?site|in[- ]office|us|usa|u\.s\.(?:a\.)?|united states|north america|anywhere|nationwide|emea|latam|apac|canada|uk|(?:remote|hybrid)\s*[,/(-].*|.*\b(?:remote|hybrid|on-?site)\b.*|[a-z .'-]+,\s*(?:AL|AK|AZ|AR|CA|CO|CT|DE|FL|GA|HI|ID|IL|IN|IA|KS|KY|LA|ME|MD|MA|MI|MN|MS|MO|MT|NE|NV|NH|NJ|NM|NY|NC|ND|OH|OK|OR|PA|RI|SC|SD|TN|TX|UT|VT|VA|WA|WV|WI|WY|DC)\b.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Location();

    [GeneratedRegex(@"^(?:#?\s*\d{3,}|[a-z]{0,4}[-_ ]?\d{2,}[a-z0-9-]*|req(?:uisition)?\.?\s*(?:id|#|no\.?)?\s*[:#]?\s*[a-z0-9-]+|job\s*(?:id|#|code)\s*[:#]?\s*[a-z0-9-]+|r\d+|jr\d+|p\d|l\d|ic\d|e\d)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Req();
}
