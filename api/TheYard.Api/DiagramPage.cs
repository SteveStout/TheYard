using System.Net;

namespace TheYard.Api;

/// <summary>
/// A diagram on its own page (ADR-020): the SVG from the repository inlined in
/// a small HTML document with the title in the tab, the palette, and a viewport
/// line, so a phone can pinch to zoom and a laptop can Ctrl+scroll, with the
/// text left selectable. DocsCatalog.Diagrams names the drawings.
/// </summary>
public static class DiagramPage
{
    // #region page
    /// <summary>
    /// The page around one SVG. The palette is repeated here on purpose: no bundle loads on this page. So is the
    /// operator's look (ADR: The glass look, the addendum on the operator's look): the drawing sits on the one panel
    /// every page wears, the glass with the dark green rule and two corner brackets, at the token sheet's values, over
    /// the site's own ground, the green grey to white gradient, with the site's teal for a link (a flat grey and the
    /// old slate blue until 1.0.3.20). The tab carries the site's own icon, the one index.html and the reference page
    /// carry, so a browser does not go looking for /favicon.ico and log a 404 (read in Chrome at 390, 1400 and 1406).
    /// </summary>
    public static string Render(string title, string svg, string sourcePath)
    {
        // A standalone SVG file may open with an XML prolog, which HTML must not carry.
        int start = svg.IndexOf("<svg", StringComparison.OrdinalIgnoreCase);
        string inline = start > 0 ? svg[start..] : svg;
        string safeTitle = WebUtility.HtmlEncode(title);
        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{safeTitle}}</title>
            <link rel="icon" href="{{ApiDocument.Favicon}}">
            <link rel="preconnect" href="https://fonts.googleapis.com">
            <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
            <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=IBM+Plex+Sans:wght@400;500;600;700&display=swap">
            <style>
              html { background: #f3f7f6; }
              html, body { margin: 0; color: #5e5653; font-family: 'IBM Plex Sans', 'Segoe UI', system-ui, Arial, sans-serif; font-variant-numeric: tabular-nums; }
              body { min-height: 100vh; background: linear-gradient(90deg, #dcebe7 0%, #f3f7f6 45%, #ffffff 100%) fixed; }
              header { display: flex; flex-wrap: wrap; gap: 4px 16px; align-items: baseline; padding: 12px 16px 8px; }
              h1 { margin: 0; font-size: 16px; color: #3f3a37; }
              header p { margin: 0; font-size: 13px; color: #5f636c; }
              a { color: #006360; }
              main { padding: 0 16px 16px; }
              .op-glass { position: relative; max-width: 1600px; margin: 0 auto; padding: 8px; background: rgba(255, 255, 255, 0.42); -webkit-backdrop-filter: blur(20px) saturate(1.5); backdrop-filter: blur(20px) saturate(1.5); border: 1px solid rgba(2, 67, 69, 0.18); border-top: 3px solid #024345; border-radius: 10px; box-shadow: 0 8px 24px rgba(2, 67, 69, 0.1), inset 0 1px 0 rgba(255, 255, 255, 0.8), inset 0 0 0 1px rgba(255, 255, 255, 0.35); }
              .op-glass::before, .op-glass::after { content: ''; position: absolute; width: 18px; height: 18px; border-color: #024345; border-style: solid; pointer-events: none; }
              .op-glass::before { left: -1px; top: -3px; border-width: 3px 0 0 2px; border-top-left-radius: 10px; }
              .op-glass::after { right: -1px; bottom: -1px; border-width: 0 2px 2px 0; border-bottom-right-radius: 10px; }
              main svg { display: block; width: 100%; height: auto; }
            </style>
            </head>
            <body>
            <header>
              <h1>{{safeTitle}}</h1>
              <p>Pinch or Ctrl+scroll to zoom; the text is selectable. <a href="https://github.com/SteveStout/TheYard/blob/main/{{sourcePath}}">Source</a> &middot; <a href="/">TheYard</a></p>
            </header>
            <main>
            <figure class="op-glass">
            {{inline}}
            </figure>
            </main>
            </body>
            </html>
            """;
    }
    // #endregion page
}
