using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using TheYard.Application.Shared.Links;

namespace TheYard.Api.Features.Docs;

/// <summary>A hook's one link: its words, which may name a count in braces, and where it goes.</summary>
/// <param name="Label">The link's words, with a count named as {recommendations} and filled when drawn.</param>
/// <param name="Href">The address it opens, never one that opens Admin.</param>
public sealed record PageHookLink(string Label, string Href);

/// <summary>One page's entry in data/hooks.json: what its first screen says before anything else.</summary>
/// <param name="Page">The page's name in the file: landing, about, post, resume, quality or review.</param>
/// <param name="Path">The address the page answers on.</param>
/// <param name="Lines">The words, each copied word for word from its source; empty where the page's own content is the hook.</param>
/// <param name="Source">Where every line was copied from.</param>
/// <param name="Link">The hook's one link, or none.</param>
public sealed record PageHook(string Page, string Path, IReadOnlyList<string> Lines, string Source, PageHookLink? Link);

/// <summary>One "Where next" link (ladder v7): its words, one line of reason, its minutes and where it goes.</summary>
/// <param name="Label">The link's words.</param>
/// <param name="Reason">One line on why a reader would go there.</param>
/// <param name="Minutes">The minutes it takes.</param>
/// <param name="Href">The address it opens, never one that opens Admin.</param>
public sealed record PageNextLink(string Label, string Reason, int Minutes, string Href);

/// <summary>One page's "Where next" footer: at most two links.</summary>
/// <param name="Page">The page's name in the file's next list.</param>
/// <param name="Links">Its links, in the order they are drawn.</param>
public sealed record PageNext(string Page, IReadOnlyList<PageNextLink> Links);

/// <summary>The file as read: one hook a page, and the "Where next" footers.</summary>
/// <param name="Hooks">Every page's entry.</param>
/// <param name="Next">Every page's footer; empty in a file from before ladder v7.</param>
public sealed record PageHooksFile(IReadOnlyList<PageHook> Hooks, IReadOnlyList<PageNext>? Next = null);

/// <summary>
/// The hook on the two pages the API draws in C# (ADR-132, A hook on every page): /about and the resume door read their
/// entry of data/hooks.json, the same file the React build reads for the landing, a blog post and the Quality page, and
/// draw it as the first box on the page. A count a link names is filled from the data it is counted from, never typed;
/// a link whose count cannot be read is left out rather than drawn with a gap.
/// </summary>
public static partial class PageHooks
{
    private static readonly JsonSerializerOptions Format = new() { PropertyNameCaseInsensitive = true };

    /// <summary>The hook file at <paramref name="path"/>, or null when it is not there or cannot be read.</summary>
    /// <param name="path">data/hooks.json, as HostPaths finds it.</param>
    public static PageHooksFile? Of(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<PageHooksFile>(File.ReadAllText(path), Format) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The hook box for <paramref name="page"/>: its lines, one as a sentence and more as a list, and its link with
    /// every count it names filled from <paramref name="counts"/>. Empty when the file has no lines for the page.
    /// </summary>
    /// <param name="file">The hook file, or null when it could not be read.</param>
    /// <param name="page">The page's name in the file.</param>
    /// <param name="counts">The counts a link may name, each read from its own data.</param>
    public static string Html(PageHooksFile? file, string page, IReadOnlyDictionary<string, int> counts)
    {
        var hook = file?.Hooks?.FirstOrDefault(entry => entry.Page == page);
        if (hook?.Lines is not { Count: > 0 } lines)
        {
            return "";
        }

        string e(string value) => WebUtility.HtmlEncode(value);
        string words = lines.Count == 1
            ? $"<p class=\"hook-line\">{e(lines[0])}</p>"
            : $"<ul class=\"hook-lines\">{string.Concat(lines.Select(line => $"<li>{e(line)}</li>"))}</ul>";
        string? label = hook.Link is null ? null : Filled(hook.Link.Label, counts);
        string link = label is null ? "" : $"<a class=\"hook-link\" href=\"{e(hook.Link!.Href)}\">{e(label)}</a>";
        return $"<section class=\"hook\" data-testid=\"hook\" data-hook=\"{e(page)}\" aria-label=\"In brief\">{words}{link}</section>";
    }

    /// <summary>
    /// The "Where next" footer for <paramref name="page"/> (ladder v7, page flow): the same links, reasons and minutes the
    /// React pages draw from the file's next list. Empty when the file has none for the page.
    /// </summary>
    /// <param name="file">The hook file, or null when it could not be read.</param>
    /// <param name="page">The page's name in the next list.</param>
    /// <param name="code">The visit's link code, carried on each link of this site so the click is counted (ladder v7b), or null.</param>
    public static string NextHtml(PageHooksFile? file, string page, string? code = null)
    {
        var links = file?.Next?.FirstOrDefault(entry => entry.Page == page)?.Links;
        if (links is not { Count: > 0 })
        {
            return "";
        }

        string e(string value) => WebUtility.HtmlEncode(value);
        string items = string.Concat(links.Select(link =>
            $"<li><a href=\"{e(Carried(link.Href, code))}\">{e(link.Label)}</a> <span>{e(link.Reason)} {link.Minutes.ToString(CultureInfo.InvariantCulture)} min</span></li>"));
        return $"<nav class=\"where-next\" aria-label=\"Where next\" data-testid=\"where-next\" data-next=\"{e(page)}\"><p>Where next</p><ul>{items}</ul></nav>";
    }

    /// <summary>A link of this site with the visit's code on it, or the link as it was.</summary>
    /// <param name="href">The link as the file has it.</param>
    /// <param name="code">The visit's code, or null.</param>
    public static string Carried(string href, string? code) =>
        LinkCodes.IsOne(code) && href.StartsWith('/')
            ? href + (href.Contains('?', StringComparison.Ordinal) ? "&" : "?") + "r=" + code
            : href;

    /// <summary>The label with each {name} replaced by its count, or null when one of them is not known.</summary>
    /// <param name="label">The link's words as the file has them.</param>
    /// <param name="counts">The counts that are known.</param>
    public static string? Filled(string label, IReadOnlyDictionary<string, int> counts)
    {
        bool missing = false;
        string filled = Named().Replace(label, match =>
        {
            if (counts.TryGetValue(match.Groups[1].Value, out int count))
            {
                return count.ToString(CultureInfo.InvariantCulture);
            }
            missing = true;
            return match.Value;
        });
        return missing ? null : filled;
    }

    [GeneratedRegex(@"\{([a-z_]+)\}")]
    private static partial Regex Named();
}
