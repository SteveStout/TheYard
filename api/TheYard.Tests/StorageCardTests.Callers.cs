using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The share card's callers (ADR: One frame for every resource, the addenda of 1.0.4.139 and 1.0.4.140): every value
/// the hour's split by authentication returns, named with its count; the last full hour beside its own total; the
/// split summed over one window; and the dial and the walking row on the readings they fall back to.
/// </summary>
public sealed partial class StorageCardTests
{
    /// <summary>
    /// Every caller the hour's split names, not two of them: on 5 November the readout named SAS and the account key
    /// beside 17,275 transactions in the hour and accounted for 69, so about 17,000 an hour carried a name the card
    /// never printed. The line lists each value Azure returned with its count, the biggest first, and the walking row
    /// says the same split.
    /// </summary>
    [Fact]
    public void The_share_names_every_caller_the_hour_s_split_returns_with_its_count_the_biggest_first()
    {
        var facts = ShareFacts();
        facts["auth_hour"] = "AccountKey=69;SAS=0;Unnamed=17206";
        var card = ShareCard.View(Inputs(100, facts));

        var line = card.Readout.Lines.Single(l => l.Label == "Every caller this hour");
        Assert.Equal("Unnamed 17,206 · the account key (the job hunt's Function) 69 · SAS (the laptop's mount and the chats) 0", line.Value);
        Assert.Equal("Azure Monitor, transactions by authentication", line.Source);
        Assert.Contains("this clock hour Unnamed 17,206 · the account key (the job hunt's Function) 69 · SAS (the laptop's mount and the chats) 0 transactions", card.Rows[2].Detail[0].Text, StringComparison.Ordinal);

        facts["auth_hour"] = "";
        Assert.Equal("not read", ShareCard.View(Inputs(100, facts)).Readout.Lines.Single(l => l.Label == "Every caller this hour").Value);
    }

    /// <summary>
    /// The split under way is read once in its hour, so beside a total summed through the hour it reads short; the last
    /// full hour's split is printed beside that same hour's own total, so the two numbers are one window.
    /// </summary>
    [Fact]
    public void The_last_full_hour_s_callers_are_printed_beside_that_hour_s_own_total()
    {
        var facts = ShareFacts();
        var lastHour = Noon.AddHours(-1);
        facts["auth_last_hour"] = "AccountKey=11000;SAS=1200;Unknown=0";
        facts["auth_last_from"] = lastHour.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        facts["tx_hourly"] = Series.Encode(new Dictionary<DateTimeOffset, double> { [lastHour.AddHours(-1)] = 5_000, [lastHour] = 12_200, [Noon] = 900 });

        var line = ShareCard.View(Inputs(100, facts)).Readout.Lines.Single(l => l.Label == "Every caller, the last full hour");

        Assert.Equal("the account key (the job hunt's Function) 11,000 · SAS (the laptop's mount and the chats) 1,200 · Unknown 0, of 12,200 from 14:00 to 15:00 UTC", line.Value);
        Assert.Equal("Azure Monitor, transactions by authentication", line.Source);
        Assert.Equal("not read", ShareCard.View(Inputs(100, ShareFacts())).Readout.Lines.Single(l => l.Label == "Every caller, the last full hour").Value);
    }

    [Fact]
    public void The_split_sums_each_value_over_its_window_and_keeps_only_numbers()
    {
        var at = Noon.AddHours(-2);
        var byAuth = new Dictionary<string, IReadOnlyDictionary<DateTimeOffset, double>>(StringComparer.Ordinal)
        {
            ["AccountKey"] = new Dictionary<DateTimeOffset, double> { [at] = 10, [at.AddHours(1)] = 20, [at.AddHours(1).AddMinutes(30)] = 5, [at.AddHours(2)] = 7 },
        };

        Assert.Equal("AccountKey=25", ShareCard.AuthWindow(byAuth, at.AddHours(1), at.AddHours(2)));
        Assert.Equal("AccountKey=32", ShareCard.AuthHour(byAuth, at.AddHours(1)));
        Assert.Equal("B 5 · C 5 · A 1", ShareCard.Callers(new Dictionary<string, double> { ["C"] = 5, ["A"] = 1, ["B"] = 5 }));

        var facts = ShareFacts();
        facts["auth_hour"] = "SAS=many;AccountKey=5";
        var card = ShareCard.View(Inputs(100, facts));
        Assert.Equal("not read", card.Readout.Lines.Single(l => l.Label == "Who mounts it").Value);
        Assert.Equal("the account key (the job hunt's Function) 5", card.Readout.Lines.Single(l => l.Label == "Every caller this hour").Value);
    }

    [Fact]
    public void The_dial_reads_the_hourly_series_when_no_minute_is_kept_and_marks_its_peak()
    {
        var card = ShareCard.View(Inputs(100, ShareFacts(), minutes: 0));

        Assert.True(card.Available, "an hour read is enough to draw the card");
        Assert.Equal("1,200,000", card.Dials[0].Big);
        Assert.Equal(6_000, card.Dials[0].Peak);
        Assert.Equal("none in the last sixty minutes", ShareCard.View(Inputs(0, ShareFacts())).Readout.Lines.Single(l => l.Label == "Last write seen").Value);
    }

    [Fact]
    public void The_alert_s_own_line_is_the_walking_row_s_red_and_twenty_thousand_only_when_it_is_not_read()
    {
        var auth = new Dictionary<string, double>();
        Assert.Equal("warning", ShareCard.Walking(Inputs(3 * 20_000.0 / 60, ShareFacts()), 30_000, auth).Tone);
        Assert.Equal("alarm", ShareCard.Walking(Inputs(3 * 20_000.0 / 60, ShareFacts()), null, auth).Tone);
    }

    [Fact]
    public void The_split_by_caller_is_the_clock_hour_under_way_and_never_two_hours_added()
    {
        var hour = new DateTimeOffset(2026, 10, 25, 22, 0, 0, TimeSpan.Zero);
        var byAuth = new Dictionary<string, IReadOnlyDictionary<DateTimeOffset, double>>(StringComparer.Ordinal)
        {
            ["SAS"] = new Dictionary<DateTimeOffset, double> { [hour.AddHours(-1)] = 2_000, [hour] = 1_569 },
            ["AccountKey"] = new Dictionary<DateTimeOffset, double> { [hour.AddHours(-1)] = 6_625, [hour] = 3_594 },
        };

        Assert.Equal("SAS=1569;AccountKey=3594", ShareCard.AuthHour(byAuth, hour));
    }
}
