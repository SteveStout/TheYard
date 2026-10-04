using System.Net;
using System.Text.Json;

namespace TheYard.Api;

/// <summary>
/// Who built this, on a page of its own at /about, for the reader who searched
/// his name rather than the one who came to see the auction. Served by the API
/// on the diagram pages' pattern (ADR-020): one small HTML document with the
/// palette inline, a viewport line and selectable text, and nothing fetched
/// from another host, not even a font. Every sentence is drawn from the served
/// resume and the README; the page invents nothing about him.
/// </summary>
public static class AboutPage
{
    // #region words
    /// <summary>The resume's own title line, as the page and its structured data state it.</summary>
    public const string JobTitle = "Lead / Staff .NET Engineer";

    /// <summary>The tab's title, which is also the headline a search result shows.</summary>
    public const string Title = "Steven Stout, Lead / Staff .NET Engineer";

    /// <summary>What a search result shows under the title: one sentence, under 160 characters.</summary>
    public const string Description =
        "Steven Stout, Lead / Staff .NET engineer: twelve years full stack .NET, seven fully remote, .NET platforms on Azure, and TheYard as the working proof.";

    /// <summary>The two profiles that are him, for the page's links and the structured data's sameAs.</summary>
    public static readonly string[] Profiles =
    [
        "https://www.linkedin.com/in/stevenwstout/",
        "https://github.com/SteveStout/TheYard",
    ];
    // #endregion words

    // #region page
    /// <summary>
    /// The page for a site reached at <paramref name="siteUrl"/>, which is the
    /// container's Site:Url, or the request's own address where none is set.
    /// Both containers serve it, each under its own name.
    /// </summary>
    public static string Render(string siteUrl)
    {
        string site = siteUrl.TrimEnd('/');
        string canonical = site + "/about";
        string e(string value) => WebUtility.HtmlEncode(value);
        // #region person-json-ld
        string person = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Person",
            ["@id"] = canonical + "#steven-stout",
            ["name"] = "Steven Stout",
            ["jobTitle"] = JobTitle,
            ["url"] = site + "/",
            ["sameAs"] = Profiles,
            ["address"] = new Dictionary<string, string>
            {
                ["@type"] = "PostalAddress",
                ["addressLocality"] = "Saint Charles",
                ["addressRegion"] = "Missouri",
                ["addressCountry"] = "US",
            },
        });
        // #endregion person-json-ld
        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{e(Title)}}</title>
            <meta name="description" content="{{e(Description)}}">
            <meta name="author" content="Steven Stout">
            <link rel="canonical" href="{{e(canonical)}}">
            <link rel="icon" href="{{ApiDocument.Favicon}}">
            <meta name="theme-color" content="#0f4452">
            <meta property="og:type" content="profile">
            <meta property="og:site_name" content="TheYard">
            <meta property="og:url" content="{{e(canonical)}}">
            <meta property="og:title" content="{{e(Title)}}">
            <meta property="og:description" content="{{e(Description)}}">
            <meta property="og:image" content="{{e(site + "/og.png")}}">
            <meta property="og:image:width" content="1200">
            <meta property="og:image:height" content="630">
            <meta property="profile:first_name" content="Steven">
            <meta property="profile:last_name" content="Stout">
            <meta name="twitter:card" content="summary_large_image">
            <meta name="twitter:title" content="{{e(Title)}}">
            <meta name="twitter:description" content="{{e(Description)}}">
            <meta name="twitter:image" content="{{e(site + "/og.png")}}">
            <script type="application/ld+json">{{person}}</script>
            <style>
              html { background: #f3f7f6; }
              html, body { margin: 0; color: #524b48; font-family: 'IBM Plex Sans', 'Segoe UI', system-ui, Arial, sans-serif; font-variant-numeric: tabular-nums; line-height: 1.6; }
              body { min-height: 100vh; background: linear-gradient(90deg, #deeaed 0%, #f3f7f6 45%, #ffffff 100%) fixed; }
              main { max-width: 720px; margin: 0 auto; padding: 32px 16px 48px; }
              .op-glass { position: relative; padding: 24px 24px 20px; background: rgba(255, 255, 255, 0.72); border: 1px solid rgba(16, 73, 90, 0.18); border-top: 3px solid #10495a; border-radius: 10px; box-shadow: 0 8px 24px rgba(16, 73, 90, 0.1), inset 0 1px 0 rgba(255, 255, 255, 0.8); }
              .op-glass::before, .op-glass::after { content: ''; position: absolute; width: 18px; height: 18px; border-color: #10495a; border-style: solid; pointer-events: none; }
              .op-glass::before { left: -1px; top: -3px; border-width: 3px 0 0 2px; border-top-left-radius: 10px; }
              .op-glass::after { right: -1px; bottom: -1px; border-width: 0 2px 2px 0; border-bottom-right-radius: 10px; }
              h1 { display: inline-block; margin: 0; padding-bottom: 4px; font-size: 32px; line-height: 1.2; color: #3f3a37; border-bottom: 3px solid #b8923f; border-image: linear-gradient(90deg, #7d582e 0%, #c9a95c 30%, #e6cb7e 50%, #ad8e57 80%, #7d582e 100%) 1; }
              .title { margin: 8px 0 20px; font-size: 14px; font-weight: 600; letter-spacing: 0.08em; text-transform: uppercase; color: #10495a; }
              p { margin: 0 0 16px; }
              a { color: #12677f; }
              ul.links { display: flex; flex-wrap: wrap; gap: 8px; margin: 24px 0 0; padding: 0; list-style: none; }
              ul.links a { display: inline-flex; align-items: center; min-height: 44px; padding: 0 16px; border: 1.5px solid #12677f; border-radius: 999px; text-decoration: none; font-weight: 600; }
              ul.links a:hover, ul.links a:focus-visible { background: #12677f; color: #ffffff; }
              footer { max-width: 720px; margin: 0 auto; padding: 0 16px 32px; font-size: 13px; color: #4a4e57; }
            </style>
            </head>
            <body>
            <main>
            <article class="op-glass">
            <h1>Steven Stout</h1>
            <p class="title">{{e(JobTitle)}}</p>
            <p>A Lead / Staff .NET engineer who owns platform architecture end to end, from REST API design through the deployment pipeline it ships on. Twelve years full stack .NET and seven fully remote. Most recently the first lead-level engineering hire at Storee, where I set the architecture, built the team, and consolidated four applications onto a single .NET 9 platform on Azure without interrupting production releases.</p>
            <p><a href="/">TheYard</a> is a used-vehicle auction platform I built and run on Azure: 100,000 vehicles, live bidding, and the same build on Azure SQL Database and on Azure Cosmos DB. It is the working proof of how I build: read <a href="/?doc=performance">how it performs</a>, <a href="https://github.com/SteveStout/TheYard/tree/main/docs">the decision records</a> behind each choice, <a href="/api/reference">the API reference</a> and <a href="https://github.com/SteveStout/TheYard/blob/main/data/test-results.json">the test record</a> of the gate every version passes.</p>
            <ul class="links">
              <li><a href="{{e(Profiles[0])}}" rel="me">LinkedIn</a></li>
              <li><a href="{{e(Profiles[1])}}" rel="me">TheYard on GitHub</a></li>
              <li><a href="/api/docs/resume">Resume (PDF)</a></li>
            </ul>
            </article>
            </main>
            <footer><a href="/">TheYard</a> &middot; <a href="/?doc=author">About Steven, the longer version</a></footer>
            </body>
            </html>
            """;
    }
    // #endregion page
}
