// A post's body drawn to HTML once, on save, from the blocks BlogMarkdown reads, so the page shows a post with no
// Markdown reader of its own. Every piece of the writer's text is escaped, and the only element that carries an
// address is a link to http or https, which the Markdown's own pattern already insists on; nothing the writer
// types becomes markup the page did not choose.
using System.Text;

namespace TheYard.Domain.Features.Blog;

/// <summary>Draws a post's body as HTML: h3, p with br, ul with li, strong, code, a span for a hashtag, and a link that opens apart.</summary>
public static class BlogBodyHtml
{
    /// <summary>The body as HTML, each block on a line of its own.</summary>
    /// <param name="body">The post's body as the operator saved it.</param>
    public static string Render(string body)
    {
        var html = new StringBuilder();
        foreach (var block in BlogMarkdown.Blocks(body))
        {
            switch (block.Kind)
            {
                case "heading":
                    html.Append("<h3>");
                    Line(html, block.Lines[0]);
                    html.Append("</h3>\n");
                    break;
                case "list":
                    html.Append("<ul>");
                    foreach (var item in block.Lines)
                    {
                        html.Append("<li>");
                        Line(html, item);
                        html.Append("</li>");
                    }
                    html.Append("</ul>\n");
                    break;
                default:
                    html.Append("<p>");
                    for (int i = 0; i < block.Lines.Count; i++)
                    {
                        if (i > 0)
                        {
                            html.Append("<br>");
                        }
                        Line(html, block.Lines[i]);
                    }
                    html.Append("</p>\n");
                    break;
            }
        }
        return html.ToString();
    }

    private static void Line(StringBuilder html, IReadOnlyList<BodyInline> parts)
    {
        foreach (var part in parts)
        {
            string text = Escape(part.Text);
            html.Append(part.Kind switch
            {
                "link" => $"<a href=\"{Escape(part.Href!)}\" target=\"_blank\" rel=\"noreferrer\">{text}</a>",
                "bold" => $"<strong>{text}</strong>",
                "code" => $"<code>{text}</code>",
                "tag" => $"<span class=\"tag\">{text}</span>",
                _ => text,
            });
        }
    }

    /// <summary>The five characters HTML gives a meaning to, written as entities; every other character as it is.</summary>
    /// <param name="text">The writer's text.</param>
    public static string Escape(string text)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            string? entity = c switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => null,
            };
            if (entity is null)
            {
                escaped.Append(c);
            }
            else
            {
                escaped.Append(entity);
            }
        }
        return escaped.ToString();
    }
}
