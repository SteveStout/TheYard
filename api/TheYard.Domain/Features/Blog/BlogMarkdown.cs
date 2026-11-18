// The small Markdown a post is written in (ADR: Every LinkedIn post is also a blog post, the addendum on a post
// that reads as a blog), read into blocks: a heading of one to three hashes, a list of dash or star items, and
// paragraphs that keep their line breaks; inside a line a link to an http or https address, code in backticks,
// bold in double stars and a hashtag. The same reading src/lib/blogBody.ts did on the page, moved here so the
// body is drawn once on save (BlogBodyHtml) and its plain text is what the search and the keywords count.
using System.Text;
using System.Text.RegularExpressions;

namespace TheYard.Domain.Features.Blog;

/// <summary>One run of a line: plain text, bold, code, a hashtag, or a link with its address.</summary>
/// <param name="Kind">text, bold, code, tag or link.</param>
/// <param name="Text">The words the reader sees.</param>
/// <param name="Href">The address, for a link; null otherwise.</param>
public sealed record BodyInline(string Kind, string Text, string? Href = null);

/// <summary>One block of a post: a heading (one line), a paragraph (its lines) or a list (its items).</summary>
/// <param name="Kind">heading, paragraph or list.</param>
/// <param name="Lines">The heading's one line, the paragraph's lines, or the list's items, each a line of runs.</param>
public sealed record BodyBlock(string Kind, IReadOnlyList<IReadOnlyList<BodyInline>> Lines);

/// <summary>Reads a post's body into blocks and runs, and gives back the text a reader reads.</summary>
public static partial class BlogMarkdown
{
    /// <summary>The runs of one line, the marks tried in order at each place: a link, code, bold, a hashtag; anything else stays as written.</summary>
    /// <param name="line">One line of the body.</param>
    public static IReadOnlyList<BodyInline> Inline(string line)
    {
        var parts = new List<BodyInline>();
        int last = 0;
        foreach (Match match in Marks().Matches(line))
        {
            if (match.Index > last)
            {
                parts.Add(new BodyInline("text", line[last..match.Index]));
            }
            parts.Add(match.Groups["href"].Success ? new BodyInline("link", match.Groups["words"].Value, match.Groups["href"].Value)
                : match.Groups["code"].Success ? new BodyInline("code", match.Groups["code"].Value)
                : match.Groups["bold"].Success ? new BodyInline("bold", match.Groups["bold"].Value)
                : new BodyInline("tag", match.Groups["tag"].Value));
            last = match.Index + match.Length;
        }
        if (last < line.Length)
        {
            parts.Add(new BodyInline("text", line[last..]));
        }
        return parts;
    }

    /// <summary>
    /// The blocks of a body, top to bottom. A blank line ends a paragraph or a list; a heading line stands alone;
    /// item lines that follow one another make one list; every other line joins the paragraph under way.
    /// </summary>
    /// <param name="body">The post's body as the operator saved it.</param>
    public static IReadOnlyList<BodyBlock> Blocks(string body)
    {
        var blocks = new List<BodyBlock>();
        List<IReadOnlyList<BodyInline>>? paragraph = null, list = null;
        void Close()
        {
            if (paragraph is not null)
            {
                blocks.Add(new BodyBlock("paragraph", paragraph));
            }
            if (list is not null)
            {
                blocks.Add(new BodyBlock("list", list));
            }
            paragraph = null;
            list = null;
        }
        foreach (string raw in body.Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.Trim().Length == 0)
            {
                Close();
            }
            else if (Heading().Match(line) is { Success: true } heading)
            {
                Close();
                blocks.Add(new BodyBlock("heading", [Inline(heading.Groups[1].Value)]));
            }
            else if (Item().Match(line) is { Success: true } item)
            {
                if (paragraph is not null)
                {
                    Close();
                }
                (list ??= []).Add(Inline(item.Groups[1].Value));
            }
            else
            {
                if (list is not null)
                {
                    Close();
                }
                (paragraph ??= []).Add(Inline(line));
            }
        }
        Close();
        return blocks;
    }

    /// <summary>The words a reader reads, a line each: the marks gone, a link's words kept and its address left out.</summary>
    /// <param name="body">The post's body.</param>
    public static string Plain(string body)
    {
        var text = new StringBuilder();
        foreach (var line in Blocks(body).SelectMany(block => block.Lines))
        {
            text.AppendJoin(string.Empty, line.Select(part => part.Text)).Append('\n');
        }
        return text.ToString();
    }

    [GeneratedRegex(@"\[(?<words>[^\]\n]+)\]\((?<href>https?://[^\s)]+)\)|`(?<code>[^`\n]+)`|\*\*(?<bold>[^*\n]+?)\*\*|(?<tag>#[A-Za-z][A-Za-z0-9_]*)")]
    private static partial Regex Marks();

    [GeneratedRegex(@"^#{1,3}\s+(.+)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^[-*]\s+(.+)$")]
    private static partial Regex Item();
}
