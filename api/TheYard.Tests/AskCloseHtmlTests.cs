using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api.Features.Docs;

namespace TheYard.Tests;

/// <summary>
/// The site's one closing row on /about (ADR: The sales pages set the site's look, the addendum on the one close): the
/// page is not React, so AskCloseHtml holds the words, and these hold them equal to the React ones in
/// src/lib/reviewVerdict.ts, read as text; the page ends on the row, its quiet GitHub link and then its Where next,
/// whose first link is the review page and whose second names the recommendations' count read from the file.
/// </summary>
public sealed class AskCloseHtmlTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static string ReactWords() => File.ReadAllText(Path.Combine(Repo.Root(), "src", "lib", "reviewVerdict.ts"));

    [Fact]
    public void The_words_and_addresses_are_the_ones_the_React_pages_draw()
    {
        string react = ReactWords();
        Assert.Contains($"question: '{AskCloseHtml.Question}',", react, StringComparison.Ordinal);
        Assert.Contains($"line: '{AskCloseHtml.Line}',", react, StringComparison.Ordinal);
        Assert.Contains($"book: '{AskCloseHtml.Book}',", react, StringComparison.Ordinal);
        Assert.Contains($"resume: '{AskCloseHtml.Resume}',", react, StringComparison.Ordinal);
        Assert.Contains($"linkedin: '{AskCloseHtml.LinkedIn}',", react, StringComparison.Ordinal);
        Assert.Contains($"BOOK_ADDRESS = '{AskCloseHtml.BookAddress}';", react, StringComparison.Ordinal);
        Assert.Contains($"RESUME_ADDRESS = '{AskCloseHtml.ResumeAddress}';", react, StringComparison.Ordinal);
        Assert.Contains($"LINKEDIN_ADDRESS = '{AboutPage.Profiles[0]}';", react, StringComparison.Ordinal);
    }

    [Fact]
    public void The_row_draws_the_question_its_line_the_three_moves_in_order_and_the_quiet_link()
    {
        string html = AskCloseHtml.Html(AboutPage.Profiles[0], AboutPage.Profiles[1]);

        Assert.Contains("data-mock=\"close\"", html, StringComparison.Ordinal);
        Assert.Contains("data-mock=\"close-ask\"", html, StringComparison.Ordinal);
        Assert.Contains($"data-mock=\"close-question\">{WebUtility.HtmlEncode(AskCloseHtml.Question)}</h2>", html, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"op-ask-sub\">{AskCloseHtml.Line}</p>", html, StringComparison.Ordinal);
        int book = html.IndexOf($"<a class=\"op-pill-primary\" href=\"{AskCloseHtml.BookAddress}\">{AskCloseHtml.Book}</a>", StringComparison.Ordinal);
        int resume = html.IndexOf($"<a class=\"op-pill-resume\" href=\"{AskCloseHtml.ResumeAddress}\"><svg", StringComparison.Ordinal);
        int linkedIn = html.IndexOf($"<a class=\"op-pill-secondary\" href=\"{AboutPage.Profiles[0]}\" rel=\"me\">{AskCloseHtml.LinkedIn}</a>", StringComparison.Ordinal);
        int gitHub = html.IndexOf($"<a href=\"{AboutPage.Profiles[1]}\" rel=\"me\">{AskCloseHtml.GitHub}</a>", StringComparison.Ordinal);
        Assert.True(book > 0 && book < resume && resume < linkedIn && linkedIn < gitHub, "the moves in order, then the quiet link");
        Assert.Contains($"</svg>{AskCloseHtml.Resume}</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("\u2014", html + AskCloseHtml.Style, StringComparison.Ordinal);
    }

    [Fact]
    public async Task About_ends_on_the_row_then_Where_next_to_the_review_page_and_the_counted_recommendations()
    {
        var file = JsonSerializer.Deserialize<RecommendationsReply>(File.ReadAllText(Repo.DataFile("recommendations.json")), RecommendationsFile.Format)!;
        using var response = await factory.CreateClient().GetAsync("/about");
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("class=\"links\"", html, StringComparison.Ordinal);
        Assert.Contains(AskCloseHtml.Style.Trim(), html, StringComparison.Ordinal);
        int close = html.IndexOf("data-mock=\"close\"", StringComparison.Ordinal);
        int article = html.IndexOf("</article>", StringComparison.Ordinal);
        int next = html.IndexOf("data-mock=\"where-next\"", StringComparison.Ordinal);
        Assert.True(close > 0 && close < article && article < next, "the row closes the article and Where next follows it");
        string footer = html[next..html.IndexOf("</nav>", next, StringComparison.Ordinal)];
        Assert.Contains("<a href=\"/?view=review\" data-mock=\"next-link\">Check my work</a> <span>Three questions on the running system, each answered live. 2 min</span>", footer, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"/?view=recommendations\" data-mock=\"next-link\">Recommendations</a> <span>{file.Recommendations.Count} people who worked with him, in their words. 4 min</span>", footer, StringComparison.Ordinal);
        Assert.DoesNotContain("{recommendations}", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_Where_next_link_whose_count_cannot_be_read_is_left_out()
    {
        var file = PageHooks.Of(Repo.DataFile("hooks.json"));

        string drawn = PageHooks.NextHtml(file, "about", null, new Dictionary<string, int>());
        Assert.Contains("Check my work", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("Recommendations", drawn, StringComparison.Ordinal);
        Assert.Contains("people who worked with him", PageHooks.NextHtml(file, "about", null, new Dictionary<string, int> { ["recommendations"] = 13 }), StringComparison.Ordinal);
        var counted = new PageHooksFile([], [new PageNext("about", [new PageNextLink("Recommendations", "{recommendations} people.", 4, "/?view=recommendations")])]);
        Assert.Equal("", PageHooks.NextHtml(counted, "about"));
    }
}
