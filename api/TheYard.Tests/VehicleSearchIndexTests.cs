using TheYard.Data;

namespace TheYard.Tests;

/// <summary>
/// The search index (ADR: The search index) is an optimisation, and the only thing
/// that matters about an optimisation is that it did not change the answer.
/// Every test here compares the indexed path against the path that computes
/// the searchable text per vehicle, which is the behaviour that shipped before
/// it existed.
/// </summary>
public class VehicleSearchIndexTests
{
    private static readonly AuctionClock Now =
        TestData.ClockAt(new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.FromHours(-4)));

    private static readonly Vehicle[] Fleet =
    [
        TestData.Vehicle(id: "a", make: "Ford", bodyStyle: "SUV"),
        TestData.Vehicle(id: "b", make: "Honda", bodyStyle: "sedan"),
        TestData.Vehicle(id: "c", make: "Ford", bodyStyle: "truck"),
    ];

    // #region equivalence
    [Theory]
    [InlineData("")]
    [InlineData("ford")]
    [InlineData("FORD")]
    [InlineData("  ford   bronco ")]
    [InlineData("honda sedan")]
    [InlineData("toronto ontario clean")]
    [InlineData("2023")]
    [InlineData("live")]
    [InlineData("ended")]
    [InlineData("upcoming")]
    [InlineData("ford live")]
    [InlineData("quebec")]
    [InlineData("nothingmatchesthis")]
    public void The_indexed_path_answers_exactly_what_the_unindexed_path_answers(string query)
    {
        var filter = new VehicleFilter { Query = query };
        var index = new VehicleSearchIndex(Fleet);
        var withIndex = filter.Compile(Now, index);
        var without = filter.Compile(Now);

        foreach (var vehicle in Fleet)
        {
            Assert.Equal(without(vehicle), withIndex(vehicle));
        }
    }
    // #endregion equivalence

    [Fact]
    public void The_status_token_still_matches_through_the_index()
    {
        // Status is the one searchable value the index deliberately leaves out,
        // because the clock decides it. It has to keep working anyway.
        var vehicle = Fleet[0];
        string actual = AuctionSchedule.StatusFor(vehicle.Id, Now).ToString().ToLowerInvariant();
        string other = actual == "live" ? "ended" : "live";
        var index = new VehicleSearchIndex(Fleet);

        Assert.True(new VehicleFilter { Query = actual }.Compile(Now, index)(vehicle));
        Assert.True(new VehicleFilter { Query = $"ford {actual}" }.Compile(Now, index)(vehicle));
        Assert.False(new VehicleFilter { Query = other }.Compile(Now, index)(vehicle));
    }

    [Fact]
    public void A_bid_overlay_keeps_its_place_in_the_index()
    {
        // The overlay rebuilds each vehicle with `with` before filtering, so the
        // instance handed to the predicate is not the one the index was built
        // from. Keying by id is what makes that free.
        var index = new VehicleSearchIndex(Fleet);
        var overlaid = Fleet[0] with { CurrentBid = 99_000, BidCount = 12 };

        Assert.True(new VehicleFilter { Query = "ford bronco" }.Compile(Now, index)(overlaid));
        Assert.Equal(Fleet[0].Id, overlaid.Id);
    }

    [Fact]
    public void A_vehicle_the_index_never_saw_is_still_searched()
    {
        // A miss must fall back to computing the text, not silently fail to
        // match. This is the case a stale index would produce in production.
        var index = new VehicleSearchIndex(Fleet);
        var stranger = TestData.Vehicle(id: "not-in-the-index", make: "Toyota");

        Assert.True(new VehicleFilter { Query = "toyota" }.Compile(Now, index)(stranger));
        Assert.False(new VehicleFilter { Query = "honda" }.Compile(Now, index)(stranger));
    }

    [Fact]
    public void Matches_and_a_compiled_predicate_agree()
    {
        var filter = new VehicleFilter { Query = "ford", Province = "Ontario", PriceMin = 1 };
        var compiled = filter.Compile(Now);

        foreach (var vehicle in Fleet)
        {
            Assert.Equal(filter.Matches(vehicle, Now), compiled(vehicle));
        }
    }

    [Fact]
    public void The_index_holds_one_entry_per_vehicle_and_lowercases_it()
    {
        var index = new VehicleSearchIndex(Fleet);

        Assert.Equal(Fleet.Length, index.Count);
        string text = index.For(Fleet[0]);
        Assert.Equal(text.ToLowerInvariant(), text);
        Assert.Contains("ford", text);
        Assert.Contains("2023", text);
        Assert.DoesNotContain("live", text);
        Assert.DoesNotContain("upcoming", text);
    }

    // #region words
    /// <summary>
    /// A token of three characters or more is the start of a word, which is what a word index can
    /// seek; a shorter one is looked for anywhere, as every token was before 1.0.3.183 (ADR: The
    /// store does the work, the addendum of 19 October).
    /// </summary>
    [Theory]
    [InlineData("2023 ford bronco big bend suv clean ontario toronto", "hon", false)]
    [InlineData("2019 honda civic ex sedan clean ontario toronto", "hon", true)]
    [InlineData("2019 honda civic ex sedan clean ontario toronto", "onda", false)]
    [InlineData("2019 honda civic ex sedan clean ontario toronto", "on", true)]
    [InlineData("2019 honda civic ex sedan clean ontario toronto", "2019", true)]
    [InlineData("2019 honda civic ex sedan clean ontario toronto", "019", false)]
    [InlineData("2019 honda civic ex sedan clean ontario toronto", "toronto", true)]
    [InlineData("upcoming", "up", true)]
    [InlineData("upcoming", "com", false)]
    [InlineData("live", "liv", true)]
    public void A_word_is_the_start_of_a_word_and_a_shorter_token_is_anywhere(string text, string token, bool held) =>
        Assert.Equal(held, VehicleSearchIndex.Holds(text, token));

    [Fact]
    public void A_texts_words_are_each_word_once_in_the_order_they_first_appear() =>
        Assert.Equal(["2019", "honda", "civic", "sedan", "toronto"], VehicleSearchIndex.WordsOf("2019 honda civic sedan honda toronto"));
    // #endregion words
}
