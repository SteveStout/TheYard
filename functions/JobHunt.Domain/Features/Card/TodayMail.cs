using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace JobHunt.Domain.Features.Card;

/// <summary>
/// The card as the mail's HTML part, so it reads on a phone: headings as headings, bullets as a list, a quoted draft
/// as a quote, bold as bold, every character escaped. The plain part is the card's own text.
/// </summary>
public static partial class TodayMail
{
    /// <summary>The address the card goes to, his own.</summary>
    public const string To = "stevenstout11@gmail.com";

    /// <summary>The card's markdown drawn as simple HTML.</summary>
    /// <param name="card">The card's text.</param>
    public static string Html(string card)
    {
        var html = new StringBuilder("<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:15px;line-height:1.45\">\n");
        bool list = false;
        foreach (string raw in TodayParts.Lines(card))
        {
            string line = raw.Trim();
            bool item = raw.StartsWith("- ", StringComparison.Ordinal) || Numbered().IsMatch(raw);
            if (list && (line.Length == 0 || raw.StartsWith('#')))
            {
                html.Append("</ul>\n");
                list = false;
            }
            if (line.Length == 0 || line.StartsWith("> RULES FIRST", StringComparison.Ordinal))
            {
                continue;
            }
            if (raw.StartsWith("# ", StringComparison.Ordinal))
            {
                html.Append("<h1 style=\"font-size:20px\">").Append(Inline(line[2..])).Append("</h1>\n");
            }
            else if (raw.StartsWith("## ", StringComparison.Ordinal) || raw.StartsWith("### ", StringComparison.Ordinal))
            {
                html.Append("<h2 style=\"font-size:17px;margin:18px 0 6px\">").Append(Inline(line.TrimStart('#', ' '))).Append("</h2>\n");
            }
            else if (item)
            {
                if (!list)
                {
                    html.Append("<ul style=\"padding-left:20px\">\n");
                    list = true;
                }
                html.Append("<li>").Append(Inline(raw.StartsWith("- ", StringComparison.Ordinal) ? line[2..] : line)).Append("</li>\n");
            }
            else if (line.StartsWith("> ", StringComparison.Ordinal) || line == ">")
            {
                html.Append("<blockquote style=\"margin:2px 0 2px 24px;padding-left:8px;border-left:3px solid #999\">").Append(Inline(line.TrimStart('>').Trim())).Append("</blockquote>\n");
            }
            else
            {
                html.Append("<p style=\"margin:4px 0\">").Append(Inline(line)).Append("</p>\n");
            }
        }
        if (list)
        {
            html.Append("</ul>\n");
        }
        return html.Append("</div>\n").ToString();
    }

    private static string Inline(string text) => Bold().Replace(WebUtility.HtmlEncode(text), "<b>$1</b>");

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex Bold();

    [GeneratedRegex(@"^\d+\. ")]
    private static partial Regex Numbered();
}
