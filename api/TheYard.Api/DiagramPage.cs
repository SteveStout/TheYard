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
    /// <summary>The page around one SVG. The palette is repeated here on purpose: no bundle loads on this page.</summary>
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
            <link rel="preconnect" href="https://fonts.googleapis.com">
            <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>
            <link rel="stylesheet" href="https://fonts.googleapis.com/css2?family=Poppins:wght@400;500;600;700&display=swap">
            <style>
              html, body { margin: 0; background: #e9e6e7; color: #5e5653; font-family: Poppins, 'Segoe UI', system-ui, Arial, sans-serif; }
              header { display: flex; flex-wrap: wrap; gap: 4px 16px; align-items: baseline; padding: 12px 16px 8px; }
              h1 { margin: 0; font-size: 16px; color: #3f3a37; }
              header p { margin: 0; font-size: 13px; color: #62666f; }
              a { color: #536786; }
              main { padding: 0 16px 16px; }
              main svg { display: block; width: 100%; height: auto; max-width: 1600px; margin: 0 auto; border: 1px solid #d8d3d4; border-radius: 12px; }
            </style>
            </head>
            <body>
            <header>
              <h1>{{safeTitle}}</h1>
              <p>Pinch or Ctrl+scroll to zoom; the text is selectable. <a href="https://github.com/SteveStout/TheYard/blob/main/{{sourcePath}}">Source</a> &middot; <a href="/">TheYard</a></p>
            </header>
            <main>
            {{inline}}
            </main>
            </body>
            </html>
            """;
    }
    // #endregion page
}
