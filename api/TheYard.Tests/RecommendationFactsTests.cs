using TheYard.Domain.Features.Docs;

namespace TheYard.Tests;

/// <summary>
/// The recommendations' facts are worked out, never stated: the count, the years they span and the groups by
/// LinkedIn's relationship line in their fixed order with an empty group left out; every phrasing LinkedIn uses has
/// its group, and a line no group knows has none, so the shape check can refuse it by name.
/// </summary>
public sealed class RecommendationFactsTests
{
    [Theory]
    [InlineData("Ann managed Steven directly", "above")]
    [InlineData("Steven reported directly to Ann", "above")]
    [InlineData("Ann was Steven's mentor", "above")]
    [InlineData("Ann taught Steven", "above")]
    [InlineData("Steven was senior to Drew but did not manage Drew directly", "led")]
    [InlineData("Drew reported directly to Steven", "led")]
    [InlineData("Steven managed Drew directly", "led")]
    [InlineData("Steven taught Drew", "led")]
    [InlineData("Steven was Drew's mentor", "led")]
    [InlineData("Khushbu worked with Steven on the same team", "team")]
    [InlineData("Bill worked with Steven but on different teams", "other")]
    [InlineData("Cliff worked with Steven but they were at different companies", "outside")]
    [InlineData("Steven was a client of Dana's", "outside")]
    [InlineData("Test sat near Steven", null)]
    [InlineData("", null)]
    public void Every_phrasing_LinkedIn_uses_has_its_group_and_a_strange_one_has_none(string relationship, string? group) =>
        Assert.Equal(group, RecommendationFacts.GroupOf(relationship));

    [Fact]
    public void The_count_the_span_and_the_groups_follow_the_entries()
    {
        var facts = RecommendationFacts.Of(
        [
            ("Cliff", "Cliff worked with Steven but they were at different companies", "November 2026"),
            ("Drew", "Steven was senior to Drew but did not manage Drew directly", "April 2023"),
            ("Bill", "Bill worked with Steven but on different teams", "May 2024"),
            ("Henry", "Steven was senior to Henry but did not manage Henry directly", "June 2024"),
        ]);

        Assert.Equal((4, "2023", "2026"), (facts.Count, facts.From, facts.To));
        Assert.Equal(["led", "other", "outside"], facts.Groups.Select(g => g.Key));
        Assert.Equal(["Drew", "Henry"], facts.Groups[0].Names);
        Assert.Equal("People he was senior to", facts.Groups[0].Title);
    }

    [Fact]
    public void A_fourteenth_entry_from_above_moves_the_count_the_span_and_opens_its_group()
    {
        var thirteen = Enumerable.Range(1, 13).Select(i => ($"P{i}", "P worked with Steven on the same team", "March 2024")).ToList();
        var fourteen = RecommendationFacts.Of([.. thirteen, ("Test", "Test managed Steven directly", "January 2022")]);

        Assert.Equal(13, RecommendationFacts.Of(thirteen).Count);
        Assert.Equal((14, "2022", "2024"), (fourteen.Count, fourteen.From, fourteen.To));
        Assert.Equal("People he reported to", fourteen.Groups[0].Title);
        Assert.Equal(["Test"], fourteen.Groups[0].Names);
    }
}
