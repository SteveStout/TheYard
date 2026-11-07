using System.Net;

namespace TheYard.Api.Features.Docs;

/// <summary>
/// The list /about draws of what colleagues said (ADR: The recommendations have a page): every recommendation in the
/// file, its writer's name and title and its one pull sentence, each linking to its card on the recommendations page,
/// under a heading whose count is worked out by RecommendationFacts, never typed. Server-drawn like the rest of the
/// page, from the same file the API serves.
/// </summary>
public static class AboutRecommendations
{
    /// <summary>The section's HTML, or none when there is nothing in shape to list.</summary>
    /// <param name="reply">The file as RecommendationsFile read it, or null when it could not be read.</param>
    public static string Html(RecommendationsReply? reply)
    {
        if (reply is null || RecommendationsFile.Problems(reply).Count > 0)
        {
            return "";
        }
        string e(string value) => WebUtility.HtmlEncode(value);
        var facts = reply.Facts();
        var items = reply.Recommendations.Select(entry =>
            $"<li><blockquote><p>{e(entry.Pull)}</p></blockquote><p class=\"who\">{e(entry.Name)}, {e(entry.Title)} &middot; <a href=\"{e(CardAddress(entry.Name))}\">Read the whole thing</a></p></li>");
        return $"""
            <section class="vouches" aria-labelledby="who-vouches">
            <h2 id="who-vouches">{facts.Count} people who worked with me</h2>
            <ul class="vouch-list">{string.Concat(items)}</ul>
            </section>
            """;
    }

    /// <summary>The address of a person's card on the recommendations page; the anchor the page gives the same name (recommendationsPage.ts).</summary>
    /// <param name="name">The writer's name.</param>
    public static string CardAddress(string name)
    {
        string plain = new string(name.Normalize(System.Text.NormalizationForm.FormKD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray()).ToLowerInvariant();
        string anchor = string.Join('-', System.Text.RegularExpressions.Regex.Split(plain, "[^a-z0-9]+").Where(part => part.Length > 0));
        return $"/?view=recommendations#{anchor}";
    }
}
