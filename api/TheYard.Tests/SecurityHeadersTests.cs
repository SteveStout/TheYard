using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Application.Features.Blog;

namespace TheYard.Tests;

/// <summary>
/// The headers every answer carries (ADR: Every LinkedIn post is also a blog post, the addendum on the headers): a
/// Content-Security-Policy that lets no inline script run but the page's own, by its hash; nosniff; a referrer kept to
/// the origin across sites; the browser's powers the site never uses turned off; the opener isolated; never framed by
/// another site; and HSTS with its subdomains, never preloaded. Held on a page, an API route and a picture, and the two
/// exceptions held too: the API reference's own inline scripts, and the resume's PDF, which the site frames itself.
/// </summary>
public sealed class SecurityHeadersTests(SecurityHeadersTests.RelationalHost factory) : IClassFixture<SecurityHeadersTests.RelationalHost>
{
    /// <summary>The site's host on the relational path even when the suite is pointed at Cosmos DB: no header here depends on the store.</summary>
    public sealed class RelationalHost : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.UseSetting("Cosmos:AccountEndpoint", "");
    }

    private static string Header(HttpResponseMessage answer, string name) =>
        answer.Headers.TryGetValues(name, out var values) ? string.Join(", ", values)
        : answer.Content.Headers.TryGetValues(name, out var content) ? string.Join(", ", content)
        : "";

    // #region security-headers
    [Theory]
    [InlineData("/")]
    [InlineData("/?view=blog")]
    [InlineData("/api/blog/posts")]
    [InlineData("/api/health")]
    [InlineData("/blog/images/{picture}")]
    [InlineData("/api/blog/posts/no-such-post")]
    public async Task Every_answer_carries_every_header(string path)
    {
        string name = BlogImageNames.For(BlogImageTests.Webp, "webp");
        await factory.Services.GetRequiredService<MemoryBlogImageStore>().PutAsync(name, new BlogImageFile(BlogImageTests.Webp, "image/webp"), CancellationToken.None);
        using var answer = await factory.CreateClient().GetAsync(path.Replace("{picture}", name, StringComparison.Ordinal));

        string policy = Header(answer, "Content-Security-Policy");
        Assert.Contains("default-src 'self'", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("object-src 'none'", policy, StringComparison.Ordinal);
        Assert.Contains("base-uri 'self'", policy, StringComparison.Ordinal);
        string scripts = policy.Split(';').Select(part => part.Trim()).Single(part => part.StartsWith("script-src ", StringComparison.Ordinal));
        Assert.DoesNotContain("'unsafe-inline'", scripts, StringComparison.Ordinal);
        Assert.DoesNotContain("'unsafe-eval'", scripts, StringComparison.Ordinal);
        Assert.Equal("nosniff", Header(answer, "X-Content-Type-Options"));
        Assert.Equal("strict-origin-when-cross-origin", Header(answer, "Referrer-Policy"));
        Assert.Contains("camera=()", Header(answer, "Permissions-Policy"), StringComparison.Ordinal);
        Assert.Contains("geolocation=()", Header(answer, "Permissions-Policy"), StringComparison.Ordinal);
        Assert.Equal("same-origin", Header(answer, "Cross-Origin-Opener-Policy"));
        Assert.Equal("DENY", Header(answer, "X-Frame-Options"));
        Assert.Equal("max-age=31536000; includeSubDomains", Header(answer, "Strict-Transport-Security"));
        Assert.DoesNotContain("preload", Header(answer, "Strict-Transport-Security"), StringComparison.Ordinal);
    }
    // #endregion security-headers

    [Fact]
    public void The_page_s_own_inline_script_is_let_through_by_its_hash_and_nothing_else_is()
    {
        string page = """
            <script>console.log('the preload hints');</script>
            <script type="application/ld+json">{"name":"data, never run"}</script>
            <script type="module" crossorigin src="/assets/index.js"></script>
            <script type="application/json" id="yard-first-load">{"data":"never run"}</script>
            """;
        var hashes = TheYard.Api.Shared.Wiring.SecurityHeaders.InlineScriptHashes(page);
        string expected = "'sha256-" + Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData("console.log('the preload hints');"u8)) + "'";
        Assert.Equal([expected], hashes);
        Assert.Contains(expected, TheYard.Api.Shared.Wiring.SecurityHeaders.PagePolicy(hashes), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_api_reference_may_run_its_own_inline_script_and_nothing_else_widens()
    {
        using var answer = await factory.CreateClient().GetAsync("/api/reference/");
        string policy = Header(answer, "Content-Security-Policy");
        Assert.Contains("script-src 'self' 'unsafe-inline'", policy, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", policy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_resume_s_pdf_may_be_framed_by_the_site_itself_and_by_no_other()
    {
        using var answer = await factory.CreateClient().GetAsync("/resume?pdf=1");
        if (answer.Content.Headers.ContentType?.MediaType != "application/pdf")
        {
            // A checkout with no built resume answers no PDF; the rule is held where one is served.
            return;
        }
        Assert.Contains("frame-ancestors 'self'", Header(answer, "Content-Security-Policy"), StringComparison.Ordinal);
        Assert.Equal("SAMEORIGIN", Header(answer, "X-Frame-Options"));
    }

    [Fact]
    public void The_rendering_service_sends_the_same_policy_the_api_does()
    {
        string render = File.ReadAllText(Path.Combine(Repo.Root(), "render", "security.ts"));
        foreach (string held in new[]
        {
            TheYard.Api.Shared.Wiring.SecurityHeaders.ReferrerPolicy,
            TheYard.Api.Shared.Wiring.SecurityHeaders.PermissionsPolicy,
            TheYard.Api.Shared.Wiring.SecurityHeaders.Hsts,
            TheYard.Api.Shared.Wiring.SecurityHeaders.OpenerPolicy,
            TheYard.Api.Shared.Wiring.SecurityHeaders.EveryOtherSource,
        })
        {
            Assert.Contains(held, render, StringComparison.Ordinal);
        }
    }
}
