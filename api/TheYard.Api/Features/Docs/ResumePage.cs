using System.Net;
using TheYard.Application.Shared.Links;

namespace TheYard.Api.Features.Docs;

/// <summary>
/// The resume's door, what a browser asking /resume for a page is answered with (ADR: The resume
/// has an address, the addendum on the door): one band that links to the review route, carrying
/// the link's code when it is one, and the same locked PDF embedded below it. Served on the diagram
/// pages' pattern (ADR-020): one small HTML document with the palette inline, a viewport line and
/// the site's own font, nothing fetched from another host. Under 640 pixels the embed is hidden and
/// the PDF's own link is the primary pill, because a phone draws an embedded PDF badly or not at all.
/// </summary>
public static class ResumePage
{
    /// <summary>The query that asks /resume for the PDF itself, whatever the caller accepts.</summary>
    public const string PdfQuery = "pdf";

    /// <summary>The band's opening, shown only where the resume is drawn below it: a phone has no room for the embed.</summary>
    public const string Below = "The resume is below.";

    /// <summary>The band's sentence, true on every screen.</summary>
    public const string Band = "The system it describes is running, and you can check it in ten minutes.";

    /// <summary>The band's link to the review route.</summary>
    public const string RouteLink = "Check my work in ten minutes";

    /// <summary>The PDF's own link, beside the band.</summary>
    public const string PdfLink = "Open the PDF on its own";

    /// <summary>
    /// The door for a visit through <paramref name="door"/> (one of ResumeDoor.Doors, or other), with the route's
    /// link carrying <paramref name="code"/> only when it is a link code, and <paramref name="face"/> as the style
    /// line that declares the site's face from its own file (SiteFace), or none for the fallback face.
    /// </summary>
    /// <param name="door">The door word, never the raw query.</param>
    /// <param name="code">The link's r= value, or null.</param>
    /// <param name="face">The site's face, or empty.</param>
    public static string Html(string door, string? code, string face = "")
    {
        string e(string value) => WebUtility.HtmlEncode(value);
        string route = LinkCodes.IsOne(code) ? $"/review?r={code}" : "/review";
        string pdf = $"/resume?{PdfQuery}=1&src={door}";
        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>Steven Stout, resume</title>
            <meta name="robots" content="noindex">
            <link rel="icon" href="{{ApiDocument.Favicon}}">
            <meta name="theme-color" content="#0f4452">
            {{face}}
            <style>
              html { background: #f3f7f6; }
              html, body { margin: 0; height: 100%; color: #524b48; font-family: 'IBM Plex Sans', 'Segoe UI', system-ui, Arial, sans-serif; line-height: 1.5; }
              body { display: flex; flex-direction: column; background: linear-gradient(90deg, #deeaed 0%, #f3f7f6 45%, #ffffff 100%) fixed; }
              .band { display: flex; flex-wrap: wrap; align-items: center; gap: 12px 16px; padding: 12px 16px; background: rgba(255, 255, 255, 0.82); border-bottom: 3px solid #10495a; }
              .band p { flex: 1 1 320px; margin: 0; font-weight: 600; color: #10495a; }
              .band a { display: inline-flex; align-items: center; min-height: 44px; padding: 0 16px; border: 1.5px solid #12677f; border-radius: 999px; text-decoration: none; font-weight: 600; color: #12677f; }
              .band a.primary { background: #12677f; color: #ffffff; }
              iframe { flex: 1 1 auto; width: 100%; min-height: 70vh; border: 0; }
              @media (max-width: 639px) {
                iframe, .below { display: none; }
                .band a.pdf { background: #12677f; color: #ffffff; }
                .band a.primary { background: transparent; color: #12677f; }
              }
            </style>
            </head>
            <body>
            <header class="band" data-testid="resume-band">
              <p><span class="below">{{e(Below)}} </span>{{e(Band)}}</p>
              <a class="primary" href="{{e(route)}}">{{e(RouteLink)}}</a>
              <a class="pdf" href="{{e(pdf)}}">{{e(PdfLink)}}</a>
            </header>
            <iframe src="{{e(pdf)}}" title="Steven Stout's resume, the PDF"></iframe>
            </body>
            </html>
            """;
    }
}
