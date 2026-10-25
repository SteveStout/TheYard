using System.Globalization;
using System.Net;
using System.Text;

namespace JobHunt.Application.Features.Campaign;

/// <summary>One approved template: its id (A posted, B cold), its version, and its text as Steve approved it.</summary>
public sealed record CampaignTemplate(string Id, string Version, string Text)
{
    /// <summary>The templates the Function renders, compiled into this ring from Templates/*.md.</summary>
    public static IReadOnlyList<CampaignTemplate> All { get; } = Load();

    /// <summary>
    /// The version the send renders. Templates are code and change only with Steve's yes (10/24 12:22): v2
    /// comes in as its own files and this one constant, the morning after he says yes.
    /// </summary>
    public const string Current = "v1";

    /// <summary>The current template for a track.</summary>
    public static CampaignTemplate For(char track) =>
        All.Single(t => t.Id == (track == 'A' ? "A" : "B") && t.Version == Current);

    private static List<CampaignTemplate> Load()
    {
        var assembly = typeof(CampaignTemplate).Assembly;
        var templates = new List<CampaignTemplate>();
        foreach (string name in assembly.GetManifestResourceNames().Where(n => n.Contains(".Templates.", StringComparison.Ordinal) && n.EndsWith(".md", StringComparison.Ordinal)))
        {
            string file = name[(name.IndexOf(".Templates.", StringComparison.Ordinal) + ".Templates.".Length)..^3];
            string[] parts = file.Split('-');
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            templates.Add(new CampaignTemplate(parts[0], parts[1], reader.ReadToEnd()));
        }
        return templates;
    }
}

/// <summary>A rendered email: the subject and both bodies, or the blanks that stopped it.</summary>
public sealed record RenderedEmail(string? Subject, string? Text, string? Html, IReadOnlyList<string> Blanks)
{
    /// <summary>Whether it rendered.</summary>
    public bool Complete => Subject is not null;
}

/// <summary>
/// The template and the lead's row to a message (the review, F12). Code fills every variable from the row and
/// adds nothing. The plain body drops the Markdown marks and leaves the links bare; the HTML body is Gmail's
/// own shape (Steve's 10/21 rule: a div a line, no p or pre, so no indent breaks), Links in bold (his 10/15
/// rule) and the five links a list of anchors named for what they open, each carrying its ?r= code. A required
/// blank renders nothing and names the blank.
/// </summary>
public static class TemplateRender
{
    /// <summary>The variables that may be blank, each on a line of its own that goes when it is.</summary>
    public static readonly IReadOnlySet<string> Optional = new HashSet<string>(StringComparer.Ordinal) { "LastDotNetPostingTitle", "LastDotNetPostingMonth" };

    /// <summary>Render one template with one row's values.</summary>
    public static RenderedEmail Render(CampaignTemplate template, IReadOnlyDictionary<string, string?> values)
    {
        string text = template.Text.Replace("\r\n", "\n", StringComparison.Ordinal);
        int end = text.IndexOf('\n', StringComparison.Ordinal);
        string subjectLine = text[..end];
        if (!subjectLine.StartsWith("Subject: ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"template {template.Id}-{template.Version} has no Subject line");
        }
        var subject = TemplateFill.Fill(subjectLine["Subject: ".Length..], values, Optional);
        var body = TemplateFill.Fill(text[(end + 1)..].Trim('\n'), values, Optional);
        var blanks = subject.Blanks.Concat(body.Blanks).Distinct(StringComparer.Ordinal).ToList();
        if (blanks.Count > 0)
        {
            return new RenderedEmail(null, null, null, blanks);
        }
        string[] lines = body.Text!.Split('\n');
        return new RenderedEmail(subject.Text!, Plain(lines), Html(lines), []);
    }

    /// <summary>The variables a lead's row gives the templates.</summary>
    public static IReadOnlyDictionary<string, string?> Values(LeadRecord lead, DateTimeOffset sendAtUtc) => new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["FirstName"] = lead.FirstName,
        ["Company"] = lead.CompanyName,
        ["WhatTheyDo"] = lead.WhatTheyDo,
        ["RoleTitle"] = lead.RoleTitle,
        ["AppliedWhen"] = lead.AppliedAt is { } applied ? AppliedWhen.Render(applied, sendAtUtc) : null,
        ["Proof"] = ProofKey.Sentence(lead.ProofKey),
        ["LastDotNetPostingTitle"] = lead.Company.LastDotNetPostingTitle,
        ["LastDotNetPostingMonth"] = lead.Company.LastDotNetPostingAt?.ToString("MMMM", CultureInfo.InvariantCulture),
        ["ResumeLink"] = lead.ResumeLink,
        ["SiteLink"] = lead.SiteLink,
        ["GitHubLink"] = lead.GitHubLink,
        ["LinkedInLink"] = lead.LinkedInLink,
        ["AboutLink"] = lead.AboutLink,
    };

    private static string Plain(string[] lines) =>
        string.Join('\n', lines.Select(line => line.Replace("**", "", StringComparison.Ordinal) is var bare && bare.StartsWith("- ", StringComparison.Ordinal) ? bare[2..] : bare)) + "\n";

    private static string Html(string[] lines)
    {
        var html = new StringBuilder("<div dir=\"ltr\">\n");
        bool list = false;
        foreach (string line in lines)
        {
            if (line.StartsWith("- ", StringComparison.Ordinal) && line.IndexOf(": ", StringComparison.Ordinal) is int colon and > 2)
            {
                if (!list)
                {
                    html.Append("<ul>\n");
                    list = true;
                }
                string name = line[2..colon];
                string href = line[(colon + 2)..].Trim();
                html.Append("<li><a href=\"").Append(WebUtility.HtmlEncode(href)).Append("\">").Append(WebUtility.HtmlEncode(name)).Append("</a></li>\n");
                continue;
            }
            if (list)
            {
                html.Append("</ul>\n");
                list = false;
            }
            string inner = line.Length == 0 ? "<br>"
                : line.StartsWith("**", StringComparison.Ordinal) && line.EndsWith("**", StringComparison.Ordinal) && line.Length > 4 ? "<b>" + WebUtility.HtmlEncode(line[2..^2]) + "</b>"
                : WebUtility.HtmlEncode(line);
            html.Append("<div>").Append(inner).Append("</div>\n");
        }
        if (list)
        {
            html.Append("</ul>\n");
        }
        return html.Append("</div>\n").ToString();
    }
}
