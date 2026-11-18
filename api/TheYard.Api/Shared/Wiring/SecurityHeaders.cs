// The headers every answer from this host carries (ADR: Every LinkedIn post is also a blog post, the addendum on the
// headers): what a browser may run, load and frame, what it may say about where a reader came from, which of its
// powers the site never asks for, and that it is HTTPS from now on. One policy for every answer, the page, the API and
// the pictures alike, with two exceptions said here and nowhere else: the API reference runs its own inline script,
// and the resume's PDF is framed by the site's own resume door. The rendering service sends the same set for the pages
// it draws (render/security.ts), and SecurityHeadersTests holds the two to one policy.
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace TheYard.Api.Shared.Wiring;

/// <summary>The security headers, their values, and the step that writes them on every answer.</summary>
public static partial class SecurityHeaders
{
    // #region the-headers
    /// <summary>HTTPS for a year, subdomains too; never preloaded, which is hard to undo and the owner said no to.</summary>
    public const string Hsts = "max-age=31536000; includeSubDomains";

    /// <summary>The origin and no more to another site, the whole address within this one.</summary>
    public const string ReferrerPolicy = "strict-origin-when-cross-origin";

    /// <summary>The browser's powers the site never uses, each turned off.</summary>
    public const string PermissionsPolicy = "accelerometer=(), autoplay=(), camera=(), display-capture=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), midi=(), payment=(), usb=(), browsing-topics=()";

    /// <summary>A page this site opens is in a browsing group of its own, so no other site's window can reach it.</summary>
    public const string OpenerPolicy = "same-origin";

    /// <summary>
    /// Every source but scripts: the site's own files only; styles may be inline, because React writes a style attribute
    /// (a meter's width) and the page's first script adds one rule; pictures may be data: addresses or the repository's own
    /// on GitHub, which the documents show (the README's build badge, a record's screenshots); nothing framed but
    /// the site's own, nothing ever as an object, and no other site may frame this one.
    /// </summary>
    public const string EveryOtherSource = "style-src 'self' 'unsafe-inline'; img-src 'self' data: blob: https://github.com https://raw.githubusercontent.com; font-src 'self' data:; connect-src 'self'; frame-src 'self'; object-src 'none'; base-uri 'self'; form-action 'self'; manifest-src 'self'; worker-src 'self' blob:";
    // #endregion the-headers

    /// <summary>The policy for a page whose inline scripts have these hashes: those run, files from this site run, nothing else does.</summary>
    /// <param name="scriptHashes">Each inline script's 'sha256-...' source.</param>
    public static string PagePolicy(IEnumerable<string> scriptHashes) =>
        $"default-src 'self'; script-src {string.Join(' ', new[] { "'self'" }.Concat(scriptHashes))}; {EveryOtherSource}; frame-ancestors 'none'";

    /// <summary>The API reference's policy: its own inline script runs too, and nothing else is wider.</summary>
    public static readonly string ReferencePolicy = $"default-src 'self'; script-src 'self' 'unsafe-inline'; {EveryOtherSource}; frame-ancestors 'none'";

    /// <summary>
    /// The 'sha256-...' source of every script in a page that runs from the page's own text: not one with a src, and not
    /// a block of data (JSON, JSON-LD), which a browser never runs.
    /// </summary>
    /// <param name="html">The page.</param>
    public static IReadOnlyList<string> InlineScriptHashes(string html) =>
        [.. InlineScript().Matches(html)
            .Where(match => !match.Groups["attributes"].Value.Contains("src=", StringComparison.OrdinalIgnoreCase)
                && !match.Groups["attributes"].Value.Contains("json", StringComparison.OrdinalIgnoreCase))
            .Select(match => $"'sha256-{Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(match.Groups["body"].Value)))}'")
            .Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Writes the headers on every answer, as it starts: the page's policy with the built page's inline script hashes,
    /// read once from the page this host serves; the reference's on /api/reference; and a PDF framed by the site itself.
    /// </summary>
    /// <param name="app">The built host.</param>
    /// <param name="pagePath">The built page, wwwroot/index.html; a host without one allows no inline script at all.</param>
    public static WebApplication UseTheYardSecurityHeaders(this WebApplication app, string pagePath)
    {
        string page = PagePolicy(File.Exists(pagePath) ? InlineScriptHashes(File.ReadAllText(pagePath)) : []);
        app.Use(async (context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                bool pdf = (context.Response.ContentType ?? "").StartsWith("application/pdf", StringComparison.OrdinalIgnoreCase);
                string policy = context.Request.Path.StartsWithSegments("/api/reference") ? ReferencePolicy : page;
                headers.ContentSecurityPolicy = pdf ? policy.Replace("frame-ancestors 'none'", "frame-ancestors 'self'", StringComparison.Ordinal) : policy;
                headers.XFrameOptions = pdf ? "SAMEORIGIN" : "DENY";
                headers.XContentTypeOptions = "nosniff";
                headers["Referrer-Policy"] = ReferrerPolicy;
                headers["Permissions-Policy"] = PermissionsPolicy;
                headers["Cross-Origin-Opener-Policy"] = OpenerPolicy;
                headers.StrictTransportSecurity = Hsts;
                return Task.CompletedTask;
            });
            await next();
        });
        return app;
    }

    [GeneratedRegex(@"<script(?<attributes>[^>]*)>(?<body>.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex InlineScript();
}
