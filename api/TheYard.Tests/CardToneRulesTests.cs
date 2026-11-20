using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The three colour rules of ADR-102's addendum on pace and stale cards: a row past its own amber line takes that tone
/// as its dial does, a card past its unread age shows nothing as fine, and a monthly allowance is read against where
/// the month is heading at its pace, so the Function's free grant and Insights' free 5 GB turn amber before they run out.
/// </summary>
public sealed class CardToneRulesTests
{
    private static readonly DateTimeOffset Wednesday = new(2026, 11, 18, 12, 50, 0, TimeSpan.Zero);

    /// <summary>On 18 November the grant dial read 78 % in green under its amber 80: at that pace the month ends near 134 %.</summary>
    [Fact]
    public void The_month_s_pace_is_its_use_so_far_carried_to_its_last_day_and_judged_from_the_third_day()
    {
        Assert.Equal(78.46 * 30 / 17.534722, MonthPace.Projected(78.46, Wednesday)!.Value, 2);
        Assert.Null(MonthPace.Projected(40, new DateTimeOffset(2026, 11, 2, 23, 0, 0, TimeSpan.Zero)));
        Assert.Equal(31 / 3.0 * 10, MonthPace.Projected(10, new DateTimeOffset(2026, 12, 4, 0, 0, 0, TimeSpan.Zero))!.Value, 6);
        Assert.Null(MonthPace.Projected(null, Wednesday));

        Assert.Equal("at this pace 7.55 GB by the month's end", MonthPace.Words(7.548, "{0:0.00} GB"));
        Assert.Equal("too early in the month to project", MonthPace.Words(null, "{0:0} %"));
    }

    /// <summary>
    /// On 18 November Insights read 4.41 GB, about 7.55 GB by the 30th at its pace: 2.55 GB past the free 5 is about $5.85,
    /// working as expected under the owner's ruling (ADR-102, the addendum on what a colour means). A month heading for
    /// 16 GB is about $26 past it, worth researching.
    /// </summary>
    [Fact]
    public void Insights_cost_row_is_graded_on_the_dollars_its_month_comes_to_past_the_free_five_gigabytes()
    {
        string Tone(double monthGb, out string detail)
        {
            var row = InsightsCard.View(Insights(monthGb)).Rows.Single(r => r.Key == "cost");
            detail = string.Join(" | ", row.Detail.Select(d => d.Text));
            return row.Tone;
        }

        Assert.Equal("good", Tone(4.41, out string paced));
        Assert.Contains("at this pace 7.55 GB by the month's end against the free 5 GB", paced, StringComparison.Ordinal);
        Assert.Contains("about $5.85 past the free 5 GB by the month's end", paced, StringComparison.Ordinal);
        Assert.Contains("the subscription's month forecast is not on the bill yet", paced, StringComparison.Ordinal);
        Assert.Equal("good", Tone(2.0, out _));
        Assert.Equal("warning", Tone(9.6, out string costly));
        Assert.Contains("about $26.28 past the free 5 GB", costly, StringComparison.Ordinal);
    }

    /// <summary>
    /// 19 November, 13:13 UTC: 4.757 GB used, 0.312 GB in the last day. The straight line said 7.69 GB; the last day's rate
    /// says 8.33, and after a cut it would say less at once, where the straight line would wait for the month to dilute it
    /// (ADR-102, the addendum on the honesty rule).
    /// </summary>
    [Fact]
    public void The_month_is_projected_at_the_last_day_s_rate_when_the_last_day_is_read()
    {
        var thursday = new DateTimeOffset(2026, 11, 19, 13, 13, 0, TimeSpan.Zero);
        double elapsed = 18 + 13 / 24.0 + 13 / 1440.0;

        Assert.Equal(4.757 + 0.312 * (30 - elapsed), MonthPace.Projected(4.757, thursday, 0.312)!.Value, 6);
        Assert.Equal(4.757 * 30 / elapsed, MonthPace.Projected(4.757, thursday)!.Value, 6);
        Assert.Null(MonthPace.Projected(1, new DateTimeOffset(2026, 11, 2, 0, 0, 0, TimeSpan.Zero), 0.3));
        Assert.Equal(0.312 * 30, MonthPace.RunRate(0.312, thursday)!.Value, 6);
        Assert.Null(MonthPace.RunRate(null, thursday));
        Assert.Equal("at the last day's rate 8.33 GB by the month's end", MonthPace.Words(8.33, "{0:0.00} GB", rated: true));
    }

    /// <summary>
    /// Past the free 5 GB the cost row reads the last day's rate into dollars: 5.2 GB on 18 November at 0.33 GB a day is
    /// about $9.92 past the allowance and green, at 0.34 about $10.21 and amber, and red only with the subscription's month
    /// forecast past the $70 budget. Before 1.0.5.106's rule was replaced, both read amber.
    /// </summary>
    [Fact]
    public void Insights_past_the_free_five_gigabytes_turns_amber_at_ten_dollars_and_red_only_past_the_budget()
    {
        string Tone(double dayGb, double? forecast = null) =>
            InsightsCard.View(Insights(5.2, dayGb) with { Forecast = forecast }).Rows.Single(r => r.Key == "cost").Tone;

        Assert.Equal("good", Tone(0.33));
        Assert.Equal("warning", Tone(0.34));
        Assert.Equal("warning", Tone(0.34, 70));
        Assert.Equal("alarm", Tone(0.34, 77.10));
        Assert.Equal("good", Tone(0.33, 77.10));
    }

    /// <summary>A last day at one and a half times the median of the seven before it is a spike to research, under the dollar line too.</summary>
    [Fact]
    public void Insights_last_day_at_one_and_a_half_times_its_week_is_amber()
    {
        var week = Enumerable.Range(1, 7).Select(back =>
        {
            var reading = new Dictionary<string, double?> { ["ingest_day_gb"] = 0.25 };
            return new ResourceBucket(Wednesday.AddDays(-back).AddMinutes(-30), 5, reading, reading, reading, "test");
        }).ToList();
        string Tone(double dayGb) => InsightsCard.View(Insights(1.0, dayGb) with { Month = week }).Rows.Single(r => r.Key == "cost").Tone;

        Assert.Equal("good", Tone(0.374));
        Assert.Equal("warning", Tone(0.375));
        Assert.Equal([0.25, 0.25, 0.25, 0.25, 0.25, 0.25, 0.25], CostGrade.RollingDaysBefore(week, "ingest_day_gb", Wednesday));
    }

    /// <summary>The Nightly dial's last day is the month's newest twenty-four hours of execution units.</summary>
    [Fact]
    public void The_nightly_last_day_is_the_newest_twenty_four_hours_of_execution_units()
    {
        var units = new Dictionary<string, double?> { ["units_mbms"] = 1_024_000 };
        ResourceBucket At(double hoursAgo) => new(Wednesday.AddHours(-hoursAgo), 5, units, units, units, "test");
        var inputs = Insights(1) with { Month = [At(30), At(23), At(1)] };

        Assert.Equal(2, NightlyCard.GbSecondsLastDay(inputs));
        Assert.Null(NightlyCard.GbSecondsLastDay(inputs with { Month = [At(30)] }));
    }

    /// <summary>The SQL card's tier row said "yes, DTU first" in green over a DTU past its amber 60; past red it was already red.</summary>
    [Fact]
    public void The_tier_rows_take_amber_past_their_own_amber_line_as_their_dials_do()
    {
        var minutes = Enumerable.Range(0, 15).Select(i => new ResourceMinute(Wednesday.AddMinutes(i - 15), new Dictionary<string, double?> { ["dtu"] = 70, ["storage_gb"] = 0.5, ["sessions"] = 3, ["workers"] = 2, ["log_write"] = 1 }, "test")).ToList();
        var bucket = new ResourceBucket(Wednesday.AddMinutes(-15), 15, minutes[0].Values, minutes[0].Values, new Dictionary<string, double?> { ["blocked"] = 0, ["deadlocks"] = 0, ["failed"] = 0 }, "test");
        var inputs = new ResourceCardInputs("24h", TimeSpan.FromHours(24), Wednesday, new PlanHistoryAvailability(true, "kept in the test"), minutes, [bucket], [bucket],
            [new ResourceHour(Wednesday.AddHours(-1), "/subscriptions/s/resourceGroups/g/providers/Microsoft.Sql/servers/sql/databases/db", "tenant", new Dictionary<string, string?> { ["tier"] = "Basic" }, [])], 0.17);

        var tier = SqlCard.View(inputs).Rows.Single(r => r.Key == "tier");

        Assert.Equal(("for now; DTU is past its amber line", "warning"), (tier.Answer, tier.Tone));
        Assert.Contains("amber at 60 %, red at 90 %", tier.Detail[0].Text, StringComparison.Ordinal);
    }

    /// <summary>The registry card read unread at 61,133 s against its 10,800 while every dial and row on it stood green.</summary>
    [Fact]
    public void A_card_past_its_unread_age_shows_nothing_as_fine_and_keeps_what_it_was_warned_of()
    {
        var dial = new CloudDial("Images an anonymous pull reads", 100, "2 of 2", "", 100, 100, null, null, "good");
        var rows = new[] { new CloudRow("holds", "What does it hold?", "2 images", [], "info"), new CloudRow("public", "Is it public?", "yes", [], "good") };
        var lines = new[] { new CloudLine("Same build", "yes", "good"), new CloudLine("Tags", "376", "info") };
        CloudReport Aged(long age) => new("24h", ["24h"], true, null, 0, age, "Registry", null, null, [dial], [], new CloudChart("", [], [], 90, "", 0), rows, new CloudReadout("The registry", null, "", lines, ""));

        var stale = CardFrame.Complete(Aged(61_133), null, hourly: true);
        Assert.Equal("unread", stale.Tone);
        Assert.DoesNotContain("good", stale.Dials.Select(d => d.Tone).Concat(stale.Rows.Select(r => r.Tone)).Concat(stale.Readout.Lines.Select(l => l.Tone)));
        Assert.Equal(["info", "info"], stale.Rows.Select(r => r.Tone));

        var fresh = CardFrame.Complete(Aged(3_173), null, hourly: true);
        Assert.Equal(("fine", "good", "good"), (fresh.Tone, fresh.Dials[0].Tone, fresh.Rows[1].Tone));

        var warned = CardFrame.Complete(Aged(61_133) with { Rows = [rows[0], rows[1] with { Tone = "warning" }] }, null, hourly: true);
        Assert.Equal(("amber", "warning"), (warned.Tone, warned.Rows[1].Tone));
    }

    private static ResourceCardInputs Insights(double monthGb, double? dayGb = null)
    {
        var minute = new Dictionary<string, double?>
        {
            ["ingest_today_gb"] = 0.173,
            ["ingest_component_today_gb"] = 0.019,
            ["ingest_month_gb"] = monthGb,
            ["ingest_day_gb"] = dayGb,
            ["sampling"] = 100,
            ["capped"] = 0,
        };
        var lastHour = Enumerable.Range(0, 60).Select(i => new ResourceMinute(Wednesday.AddMinutes(i - 60), minute, "test")).ToList();
        var bucket = new ResourceBucket(Wednesday.AddMinutes(-60), 60, minute, minute, minute, "test");
        var facts = new Dictionary<string, string?> { ["cap_gb"] = "0.1", ["cap_from"] = "the component", ["workspace_name"] = "log-test" };
        return new ResourceCardInputs("24h", TimeSpan.FromDays(1), Wednesday, new PlanHistoryAvailability(true, "kept in the test"), lastHour, [bucket], [bucket],
            [new ResourceHour(Wednesday.AddHours(-1), "/subscriptions/s/resourceGroups/g/providers/microsoft.insights/components/appi-test", "tenant", facts, [])], null);
    }
}
