using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api.Features.Docs;

namespace TheYard.Tests;

/// <summary>
/// /about lists what colleagues said (ADR: The recommendations have a page): every recommendation in the file by name,
/// title and pull sentence, each linking to its card on the recommendations page by the same anchor the page gives it,
/// under a count worked out from the file; and its links reach the recommendations page and the review route.
/// </summary>
public sealed class AboutRecommendationsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task About_lists_every_recommendation_in_the_file_with_a_link_to_its_card()
    {
        var file = JsonSerializer.Deserialize<RecommendationsReply>(File.ReadAllText(Repo.DataFile("recommendations.json")), RecommendationsFile.Format)!;
        using var response = await factory.CreateClient().GetAsync("/about");
        string html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains($"{file.Recommendations.Count} people who worked with me", html, StringComparison.Ordinal);
        Assert.All(file.Recommendations, entry =>
        {
            Assert.Contains(WebUtility.HtmlEncode(entry.Name), html, StringComparison.Ordinal);
            Assert.Contains($"href=\"{AboutRecommendations.CardAddress(entry.Name)}\"", html, StringComparison.Ordinal);
        });
        Assert.Contains("<a href=\"/?view=recommendations\">Recommendations</a>", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"/?view=review\">Check my work</a>", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Henry Crans", "/?view=recommendations#henry-crans")]
    [InlineData("Renée O’Brien", "/?view=recommendations#renee-o-brien")]
    public void The_card_address_is_the_anchor_the_page_gives_the_same_name(string name, string address) =>
        Assert.Equal(address, AboutRecommendations.CardAddress(name));

    [Fact]
    public void Nothing_in_shape_draws_nothing() =>
        Assert.Equal("", AboutRecommendations.Html(null));
}
