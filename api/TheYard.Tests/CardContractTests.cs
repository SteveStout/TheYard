using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The contract every card under Cloud infrastructure keeps, card by card, from the catalogue the host built (ADR:
/// One frame for every resource, the addendum on the cadence): drawn from nothing read it names a source on every row
/// and line and nothing it did not read colours it; drawn from fifteen quiet minutes of every reading it reads fine;
/// its cadence on the wire is its catalogue flag's; past three reads it is unread and inside them it is not; and a
/// warning row outranks an old reading. The per-card lines each side are held in the card's own test class.
/// </summary>
public sealed class CardContractTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>Every key the rail draws under Cloud infrastructure, in its order; a card added is red until it is here.</summary>
    private static readonly string[] KeyList = ["sqldb", "cosmosdb", "registry", "insights", "share", "blob", "mail", "edge", "nightly", "claude", "campaign", "network", "box", "picker"];

    /// <summary>The keys as the theory's rows.</summary>
    public static readonly TheoryData<string> Keys = new(KeyList);

    /// <summary>Before the edge's retire date, so the one row a date judges reads information, not a reading.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 15, 0, 0, TimeSpan.Zero);

    private ResourceCard Card(string key) =>
        factory.Services.GetRequiredService<IReadOnlyList<(ResourceCard Card, IResourceHistory History)>>().Single(entry => entry.Card.Key == key).Card;

    private static ResourceCardInputs Quiet(ResourceCard card, int minutes)
    {
        var values = card.Readings.ToDictionary(reading => reading, _ => (double?)0, StringComparer.Ordinal);
        var lastHour = Enumerable.Range(0, minutes).Select(i => new ResourceMinute(Now.AddMinutes(i - minutes), values, "test")).ToList();
        var buckets = minutes == 0 ? new List<ResourceBucket>() : [new ResourceBucket(Now.AddMinutes(-minutes), minutes, values, values, values, "test")];
        return new ResourceCardInputs("24h", TimeSpan.FromHours(24), Now, new PlanHistoryAvailability(true, "kept in the test"), lastHour, buckets, buckets, [], null);
    }

    [Fact]
    public void The_theory_walks_every_card_the_host_built()
    {
        var built = factory.Services.GetRequiredService<IReadOnlyList<(ResourceCard Card, IResourceHistory History)>>().Select(entry => entry.Card.Key);

        Assert.Equal(KeyList, built);
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void Nothing_read_names_a_source_everywhere_and_colours_nothing(string key)
    {
        var card = Card(key);
        var view = CardFrame.Complete(card.View(Quiet(card, 0)), card.SpecSource, card.Hourly);

        Assert.All(view.Rows, row => Assert.False(string.IsNullOrEmpty(row.Source), $"{key}: {row.Key} names no source"));
        Assert.All(view.Readout.Lines, line => Assert.False(string.IsNullOrEmpty(line.Source), $"{key}: {line.Label} names no source"));
        Assert.All(view.Dials, dial => Assert.DoesNotContain(dial.Tone, new[] { "warning", "alarm" }));
        Assert.Contains(view.Tone, new[] { "fine", "unread" });
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void Fifteen_quiet_minutes_of_every_reading_never_raise_a_dial(string key)
    {
        var card = Card(key);
        var view = CardFrame.Complete(card.View(Quiet(card, 15)) with { ReadAgeSeconds = 30 }, card.SpecSource, card.Hourly);

        // A dial whose line runs downward (red under amber, the share of requests sampling kept) is worst at zero,
        // so zero is its alarm, not its rest; every other dial at zero rests.
        Assert.All(view.Dials.Where(dial => dial.Red >= dial.Amber), dial => Assert.Contains(dial.Tone, new[] { "good", "info" }));
        Assert.All(view.Dials.Where(dial => dial.Red < dial.Amber && dial.Percent == 0), dial => Assert.Equal("alarm", dial.Tone));
        Assert.All(view.Thresholds, chip => Assert.Contains(chip.Tone, new[] { "amber", "red", "held" }));
    }

    [Theory]
    [MemberData(nameof(Keys))]
    public void The_cadence_is_the_catalogue_s_and_three_reads_old_is_unread(string key)
    {
        var card = Card(key);
        var drawn = card.View(Quiet(card, 15));
        drawn = drawn with { Rows = [.. drawn.Rows.Select(row => row with { Tone = "good" })], Dials = [.. drawn.Dials.Select(dial => dial with { Tone = "good" })] };
        int cadence = card.Hourly ? CardFrame.HourCadence : CardFrame.MinuteCadence;

        var fresh = CardFrame.Complete(drawn with { ReadAgeSeconds = 3 * cadence }, card.SpecSource, card.Hourly);
        var old = CardFrame.Complete(drawn with { ReadAgeSeconds = 3 * cadence + 1 }, card.SpecSource, card.Hourly);
        var warned = CardFrame.Complete(drawn with { ReadAgeSeconds = 3 * cadence + 1, Rows = [drawn.Rows[0] with { Answer = "1 blocked", Tone = "warning" }, .. drawn.Rows.Skip(1)] }, card.SpecSource, card.Hourly);

        Assert.Equal((cadence, 3 * cadence), (fresh.CadenceSeconds, fresh.UnreadAfterSeconds));
        Assert.Equal("fine", fresh.Tone);
        Assert.Equal("unread", old.Tone);
        Assert.Equal("amber", warned.Tone);
    }
}
