using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The two cost dials' grade (ADR-102, the addendum on what a colour means), each line from both sides: green under $10
/// past the free amount and a last day under 1.5 times its week's median, amber at either, red only past $10 with the
/// subscription's month forecast past the $70 budget, and information when the cost is not read.
/// </summary>
public sealed class CostGradeTests
{
    [Theory]
    [InlineData(9.99, null, "good")]
    [InlineData(10.0, null, "warning")]
    [InlineData(0.0, null, "good")]
    [InlineData(9.99, 500.0, "good")]
    [InlineData(10.0, 70.0, "warning")]
    [InlineData(10.0, 70.01, "alarm")]
    [InlineData(40.0, 77.10, "alarm")]
    public void The_dollar_line_turns_amber_at_ten_and_red_only_with_the_forecast_past_seventy(double pastFree, double? forecast, string tone) =>
        Assert.Equal(tone, CostGrade.Tone(pastFree, 100, 100, forecast));

    [Theory]
    [InlineData(149.9, "good")]
    [InlineData(150.0, "warning")]
    [InlineData(400.0, "warning")]
    public void A_day_at_one_and_a_half_times_its_week_s_median_is_amber_under_the_dollar_line(double lastDay, string tone) =>
        Assert.Equal(tone, CostGrade.Tone(1, lastDay, 100, 500));

    [Fact]
    public void A_spike_alone_never_turns_red_and_no_week_means_no_spike()
    {
        Assert.Equal("warning", CostGrade.Tone(1, 1_000, 100, 500));
        Assert.Equal("good", CostGrade.Tone(1, 1_000, null, null));
        Assert.Equal("good", CostGrade.Tone(1, 1_000, 0, null));
        Assert.Equal("good", CostGrade.Tone(1, null, 100, null));
    }

    [Fact]
    public void A_cost_not_read_is_information_never_green()
    {
        Assert.Equal("info", CostGrade.Tone(null, 1, 1, 100));
        Assert.Null(CostGrade.PastFree(null, 5, 2.3));
    }

    [Fact]
    public void Past_free_is_the_projected_use_over_the_free_amount_at_its_price()
    {
        Assert.Equal(5.98, CostGrade.PastFree(7.6, 5, 2.30));
        Assert.Equal(0, CostGrade.PastFree(4.2, 5, 2.30));
        Assert.Equal(2.16, CostGrade.PastFree(183_000, 100_000, 0.000026));
    }

    [Fact]
    public void The_median_is_the_middle_day_or_the_mean_of_the_middle_two()
    {
        Assert.Null(CostGrade.Median([]));
        Assert.Equal(3, CostGrade.Median([5, 1, 3]));
        Assert.Equal(2.5, CostGrade.Median([4, 1, 2, 3]));
    }

    [Fact]
    public void The_words_say_the_dollars_the_day_against_its_week_the_forecast_and_the_rule()
    {
        string words = CostGrade.Words(5.87, "the free 5 GB", 0.3, 0.25, 77.1);
        Assert.Equal("about $5.87 past the free 5 GB by the month's end · the last day 1.2 times the median of the 7 days before it · the subscription's month forecast $77.10 against the $70.00 budget · " + CostGrade.Rule, words);
        Assert.Equal("no cost past the free grant until the month's use is kept · no week kept to read the last day against · the subscription's month forecast is not on the bill yet · " + CostGrade.Rule, CostGrade.Words(null, "the free grant", null, null, null));
        Assert.Equal(["cost amber $10.00 past the free amount", "cost amber at 1.5 times the 7-day median", "cost red past $10.00 with the month's forecast past $70.00"], CostGrade.Chips.Select(chip => chip.Label));
    }

    [Fact]
    public void A_day_with_nothing_kept_is_left_out_of_the_week()
    {
        var now = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
        var units = new Dictionary<string, double?> { ["units_mbms"] = 10 };
        ResourceBucket At(double daysAgo) => new(now.AddDays(-daysAgo), 5, units, units, units, "test");

        Assert.Equal([20, 10], CostGrade.DaysBefore([At(1.5), At(1.2), At(3.5), At(0.5), At(9)], "units_mbms", now));
    }
}
