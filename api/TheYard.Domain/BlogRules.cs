// The blog's rules (ADR: Every LinkedIn post is also a blog post): where a read came from, what kind of
// device it was on, which events a reader can send and what each may carry, and what makes a slug.
// Pure: no clock, no disk, no network, so the endpoint and the tests read one set of rules.
using System.Globalization;
using System.Text.RegularExpressions;

namespace TheYard.Domain;

/// <summary>The blog's rules for a read and an event.</summary>
public static partial class BlogRules
{
    /// <summary>The event types a reader's page sends.</summary>
    public static readonly IReadOnlySet<string> EventTypes = new HashSet<string>(StringComparer.Ordinal) { "depth", "time", "more", "slide", "click", "copy" };

    /// <summary>The scroll depths a page reports, in percent.</summary>
    public static readonly IReadOnlySet<int> Depths = new HashSet<int> { 25, 50, 75, 100 };

    /// <summary>
    /// Where a read came from: "ref" when it came through a tagged link, "linkedin" when LinkedIn sent it
    /// (its host, its short links, or a utm_source naming it), "search" when a search engine did, and
    /// "direct" for anything else, a typed address and an unknown site alike.
    /// </summary>
    /// <param name="referrerHost">The referring page's host; null for none.</param>
    /// <param name="refCode">The tagged link's code; null for none.</param>
    /// <param name="utmSource">The utm_source; null for none.</param>
    public static string Source(string? referrerHost, string? refCode, string? utmSource)
    {
        if (!string.IsNullOrEmpty(refCode))
        {
            return "ref";
        }
        string host = (referrerHost ?? string.Empty).ToLowerInvariant();
        if (LinkedIn().IsMatch(host) || string.Equals(utmSource, "linkedin", StringComparison.OrdinalIgnoreCase))
        {
            return "linkedin";
        }
        return Search().IsMatch(host) ? "search" : "direct";
    }

    /// <summary>The device class a user agent names: phone, tablet or desk; bot when it looked like one.</summary>
    /// <param name="userAgent">The agent, read and then dropped.</param>
    /// <param name="looksLikeABot">The site's own guess at a bot for the same request.</param>
    public static string Device(string? userAgent, bool looksLikeABot)
    {
        string agent = userAgent ?? string.Empty;
        return looksLikeABot ? "bot" : Tablet().IsMatch(agent) ? "tablet" : Phone().IsMatch(agent) ? "phone" : "desk";
    }

    /// <summary>The one sentence wrong with an event, or null when the rules admit it.</summary>
    /// <param name="type">The event's type.</param>
    /// <param name="value">What it carries.</param>
    public static string? Wrong(string type, string value)
    {
        if (!EventTypes.Contains(type))
        {
            return $"the event type must be one of {string.Join(", ", EventTypes.Order(StringComparer.Ordinal))}";
        }
        string v = value ?? string.Empty;
        bool number = int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out int n);
        return type switch
        {
            "depth" when !number || !Depths.Contains(n) => "a depth is 25, 50, 75 or 100",
            "time" when !number || n is < 1 or > 86_400 => "a time is whole seconds, from 1 to a day",
            "slide" when !number || n is < 1 or > 60 => "a slide is its number, from 1",
            "click" when !Click().IsMatch(v) => "a click names where it went: linkedin, code, or body: and a host",
            "more" or "copy" when v != "1" => "see more and copy carry 1",
            _ => null,
        };
    }

    /// <summary>True when a slug is lower-case words joined by dashes, 3 to 120 characters.</summary>
    /// <param name="slug">The slug.</param>
    public static bool IsSlug(string slug) => slug.Length is >= 3 and <= 120 && SlugShape().IsMatch(slug);

    /// <summary>A slug made from a title: lower case, letters and digits, dashes between words, at most 80 characters.</summary>
    /// <param name="title">The title.</param>
    public static string SlugOf(string title)
    {
        string slug = NotWord().Replace((title ?? string.Empty).ToLowerInvariant(), "-").Trim('-');
        return slug.Length <= 80 ? slug : slug[..80].TrimEnd('-');
    }

    [GeneratedRegex(@"(^|\.)(linkedin\.com|lnkd\.in)$")]
    private static partial Regex LinkedIn();

    [GeneratedRegex(@"(^|\.)(google\.[a-z.]+|bing\.com|duckduckgo\.com|search\.yahoo\.com|ecosia\.org|search\.brave\.com|yandex\.[a-z]+)$")]
    private static partial Regex Search();

    [GeneratedRegex(@"iPad|Tablet|PlayBook|Silk|(Android(?!.*Mobile))", RegexOptions.IgnoreCase)]
    private static partial Regex Tablet();

    [GeneratedRegex(@"Mobi|iPhone|iPod|Android.*Mobile|Windows Phone", RegexOptions.IgnoreCase)]
    private static partial Regex Phone();

    [GeneratedRegex(@"^(linkedin|code|body:[a-z0-9.-]{1,120})$")]
    private static partial Regex Click();

    [GeneratedRegex(@"^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugShape();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NotWord();
}
