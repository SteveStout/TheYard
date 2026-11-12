using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api.Features.Docs;

namespace TheYard.Tests;

/// <summary>
/// The hook on the two pages the API draws in C# (ADR-132, A hook on every page): /about and the resume door each open
/// with their entry of data/hooks.json, the one file the React build reads for the other pages (pageHooks.test.ts holds
/// that side). About carries the resume's two lines word for word; the door carries the quality sentence and a link
/// to the recommendations whose count is read from data/recommendations.json, never typed.
/// </summary>
public sealed class PageHooksTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string BrowserAccept = "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8";

    /// <summary>One page's entry in data/hooks.json, read as plain JSON so this side and the page's side read the same text.</summary>
    private static JsonElement Entry(string page)
    {
        using var file = JsonDocument.Parse(File.ReadAllText(Repo.DataFile("hooks.json")));
        return file.RootElement.GetProperty("hooks").EnumerateArray()
            .Single(hook => hook.GetProperty("page").GetString() == page).Clone();
    }

    private static string[] Lines(JsonElement entry) =>
        [.. entry.GetProperty("lines").EnumerateArray().Select(line => line.GetString()!)];

    /// <summary>The page's hook box, from its opening tag to its close.</summary>
    private static string HookBox(string html, string page)
    {
        int open = html.IndexOf($"data-hook=\"{page}\"", StringComparison.Ordinal);
        Assert.True(open >= 0, $"no hook box for {page}");
        return html[open..html.IndexOf("</section>", open, StringComparison.Ordinal)];
    }

    [Fact]
    public async Task About_opens_with_the_resumes_two_lines_from_the_hook_file_before_anything_else()
    {
        using var response = await factory.CreateClient().GetAsync("/about");
        string html = await response.Content.ReadAsStringAsync();
        string[] lines = Lines(Entry("about"));
        string box = HookBox(html, "about");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            ["Sustained 200+ software releases on a live financial platform", "Sole engineer for the last three of five years"],
            lines);
        Assert.All(lines, line => Assert.Contains(WebUtility.HtmlEncode(line), box, StringComparison.Ordinal));
        Assert.True(
            html.IndexOf("data-hook=\"about\"", StringComparison.Ordinal) < html.IndexOf("<h2", StringComparison.Ordinal),
            "the hook sits above the page's first section");
        Assert.DoesNotContain("downtime", box, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_resume_door_opens_with_the_quality_sentence_and_the_recommendations_counted_from_their_file()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/resume?src=email");
        request.Headers.Accept.ParseAdd(BrowserAccept);
        using var response = await factory.CreateClient().SendAsync(request);
        string html = await response.Content.ReadAsStringAsync();
        var entry = Entry("resume");
        string box = HookBox(html, "resume");
        var recommendations = JsonSerializer.Deserialize<RecommendationsReply>(
            File.ReadAllText(Repo.DataFile("recommendations.json")), RecommendationsFile.Format)!;
        string label = entry.GetProperty("link").GetProperty("label").GetString()!
            .Replace("{recommendations}", recommendations.Recommendations.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);

        Assert.All(Lines(entry), line => Assert.Contains(WebUtility.HtmlEncode(line), box, StringComparison.Ordinal));
        Assert.Contains("href=\"/recommendations\"", box, StringComparison.Ordinal);
        Assert.Contains(WebUtility.HtmlEncode(label), box, StringComparison.Ordinal);
        Assert.True(
            html.IndexOf("data-hook=\"resume\"", StringComparison.Ordinal) < html.IndexOf("<iframe", StringComparison.Ordinal),
            "the hook sits above the embedded PDF");
    }

    /// <summary>Number words as the sentence writes them, enough for the ratchet to grow.</summary>
    private static readonly string[] Words =
    [
        "Zero", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten", "Eleven", "Twelve",
        "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen", "Twenty",
    ];

    [Fact]
    public void The_quality_sentence_opens_with_the_count_of_numbers_the_ratchet_holds()
    {
        using var ratchet = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo.Root(), ".github", "quality-ratchet.json")));
        int count = ratchet.RootElement.EnumerateObject().Count();
        string[] said = [.. Lines(Entry("landing")), .. Lines(Entry("resume"))];

        // Steve's sentence, word for word; a sixteenth number in the ratchet turns this red rather than the hook stale.
        Assert.All(said, line => Assert.Equal(
            $"{Words[count]} quality numbers are measured on every version and none may get worse.", line));
    }

    [Fact]
    public void The_landing_chart_is_the_Quality_pages_first_chart_named_as_the_page_names_it()
    {
        string page = File.ReadAllText(Path.Combine(Repo.Root(), "docs", "quality", "QUALITY.md"));
        var fence = System.Text.RegularExpressions.Regex.Match(page, @"```quality-trend\n([^\n|]+)\|([^\n|]+)\|[^\n]*\n\{\{live:quality-trend (\w+)\}\}");
        var chart = Entry("landing").GetProperty("chart");

        Assert.True(fence.Success, "the Quality page has a first chart");
        Assert.Equal(fence.Groups[1].Value.Trim(), chart.GetProperty("label").GetString());
        Assert.Equal(fence.Groups[2].Value.Trim(), chart.GetProperty("direction").GetString());
        Assert.Equal(fence.Groups[3].Value, chart.GetProperty("number").GetString());
    }

    [Fact]
    public void A_link_whose_count_cannot_be_read_is_left_out_and_a_page_with_no_lines_draws_nothing()
    {
        var file = PageHooks.Of(Repo.DataFile("hooks.json"));
        var none = new Dictionary<string, int>();

        Assert.NotNull(file);
        Assert.Null(PageHooks.Filled("Read the {recommendations} recommendations", none));
        Assert.Equal("Read the 13 recommendations", PageHooks.Filled("Read the {recommendations} recommendations", new Dictionary<string, int> { ["recommendations"] = 13 }));
        Assert.DoesNotContain("href=\"/recommendations\"", PageHooks.Html(file, "resume", none), StringComparison.Ordinal);
        Assert.Contains("data-hook=\"resume\"", PageHooks.Html(file, "resume", none), StringComparison.Ordinal);
        Assert.Equal("", PageHooks.Html(file, "quality", none));
        Assert.Equal("", PageHooks.Html(null, "about", none));
        Assert.Null(PageHooks.Of(Path.Combine(Repo.Root(), "data", "no-such-hooks.json")));
    }

    [Fact]
    public void No_address_in_the_hook_file_opens_Admin()
    {
        string text = File.ReadAllText(Repo.DataFile("hooks.json"));

        Assert.DoesNotContain("view=admin", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/api/admin", text, StringComparison.OrdinalIgnoreCase);
    }
}
