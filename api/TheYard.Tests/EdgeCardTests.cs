using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The Netlify edge card (ADR: One frame for every resource) on its own: the retire row before its date, on it and
/// after it; the credits dial judged on its newest hourly reading, as an hourly card is; a certificate inside two
/// weeks and a failed deploy each a warning. Edge:Retires is required configuration (AzureResources.cs), so there is
/// no card without a date to hold; CardFrameTests.Rows held the past-date row alone before this class.
/// </summary>
public sealed class EdgeCardTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 15, 0, 0, TimeSpan.Zero);

    private static Dictionary<string, string?> Facts() => new(StringComparer.Ordinal)
    {
        ["token"] = "set",
        ["domain"] = "theyard.stevenstout.biz",
        ["cert_names"] = "theyard.stevenstout.biz",
        ["cert_expires"] = "2027-01-20T00:00:00Z",
        ["deploys_failed"] = "0",
    };

    /// <summary>The card's inputs with the credits read each hour, oldest first, and the newest hour's facts.</summary>
    private static ResourceCardInputs Inputs(IReadOnlyList<double?> credits, IReadOnlyDictionary<string, string?>? facts = null)
    {
        var lastHour = credits.Select((value, i) => new ResourceMinute(Now.AddHours(i - credits.Count), new Dictionary<string, double?> { ["credits"] = value, ["deploys"] = 4, ["bandwidth_gb"] = 0.2 }, "test")).ToList();
        var values = new Dictionary<string, double?> { ["credits"] = credits.LastOrDefault() };
        var bucket = new ResourceBucket(Now.AddHours(-1), 1, values, values, values, "test");
        return new ResourceCardInputs("24h", TimeSpan.FromHours(24), Now, new PlanHistoryAvailability(true, "kept in the test"), lastHour, [bucket], [bucket],
            [new ResourceHour(Now.AddHours(-1), "netlify:theyard-edge", null, facts ?? Facts(), [])], null);
    }

    private static CloudRow Needed(DateOnly retires) =>
        EdgeCard.View(Inputs([49]), true, "theyard-edge", retires).Rows.Single(row => row.Key == "needed");

    [Fact]
    public void Before_its_date_the_edge_is_information_and_counts_the_days_left()
    {
        var row = Needed(new DateOnly(2026, 11, 20));

        Assert.Equal(("until Nov 20, 15 days", "info"), (row.Answer, row.Tone));
        Assert.Contains("this card retires with the edge", row.Detail[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void On_its_date_and_after_it_the_edge_is_a_warning_that_names_the_date()
    {
        var on = Needed(new DateOnly(2026, 11, 5));
        var after = Needed(new DateOnly(2026, 10, 30));

        Assert.Equal(("past its date; retire it", "warning"), (on.Answer, on.Tone));
        Assert.Equal(("past its date; retire it", "warning"), (after.Answer, after.Tone));
        Assert.Contains("past its date, Oct 30, 2026", after.Detail[0].Text, StringComparison.Ordinal);
        Assert.Equal("info", Needed(new DateOnly(2026, 11, 6)).Tone);
    }

    [Theory]
    [InlineData(150, "good")]
    [InlineData(180, "warning")]
    [InlineData(255, "alarm")]
    public void The_credits_dial_is_judged_on_its_newest_hourly_reading_alone(double newest, string tone)
    {
        // An hourly card's one document stands for its hour, so the hour before, far over red, does not hold the tone.
        var card = EdgeCard.View(Inputs([290, newest]), true, "theyard-edge", new DateOnly(2026, 11, 20));

        Assert.Equal(tone, card.Dials[0].Tone);
        Assert.Equal($"{newest:0} of 300", card.Dials[0].Absolute);
    }

    [Fact]
    public void A_certificate_inside_two_weeks_and_a_failed_deploy_are_each_a_warning()
    {
        var soon = Facts();
        soon["cert_expires"] = "2026-11-15T00:00:00Z";
        var failed = Facts();
        failed["deploys_failed"] = "1";

        Assert.Equal("warning", EdgeCard.View(Inputs([49], soon), true, "theyard-edge", new DateOnly(2026, 11, 20)).Rows.Single(r => r.Key == "through").Tone);
        Assert.Equal("good", EdgeCard.View(Inputs([49]), true, "theyard-edge", new DateOnly(2026, 11, 20)).Rows.Single(r => r.Key == "through").Tone);
        Assert.Equal("warning", EdgeCard.View(Inputs([49], failed), true, "theyard-edge", new DateOnly(2026, 11, 20)).Rows.Single(r => r.Key == "credits").Tone);
    }

    [Fact]
    public void Every_row_and_readout_line_names_its_source()
    {
        var card = EdgeCard.View(Inputs([49]), true, "theyard-edge", new DateOnly(2026, 11, 20));

        Assert.All(card.Rows, row => Assert.False(string.IsNullOrEmpty(row.Source), row.Key));
        Assert.All(card.Readout.Lines, line => Assert.False(string.IsNullOrEmpty(line.Source), line.Label));
    }
}
