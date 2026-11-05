using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The Azure SQL Database card (ADR: One frame for every resource) on its own: the DTU and data space dials through
/// every tone they can take with fifteen held minutes, the four rows each way, and a source on every row and line.
/// CloudViewTests named its metrics only; this holds what the card says.
/// </summary>
public sealed class SqlCardTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 15, 0, 0, TimeSpan.Zero);

    private static Dictionary<string, string?> Facts() => new(StringComparer.Ordinal)
    {
        ["tier"] = "Basic",
        ["dtu"] = "5",
        ["max_gb"] = "2",
        ["tables"] = "14",
        ["migration"] = "20261020_Slot",
        ["served_by"] = "theyard.stevenstout.biz",
        ["spec"] = "BASIC 5 DTU 2 GB WESTUS3",
    };

    /// <summary>The card's inputs with each reading given minute by minute, oldest first, and an hour of facts.</summary>
    private static ResourceCardInputs Inputs(IReadOnlyList<double?> dtu, double storageGb = 0.5, IReadOnlyDictionary<string, double?>? sums = null, IReadOnlyList<PlanEvent>? events = null, bool hour = true)
    {
        var lastHour = dtu.Select((value, i) => new ResourceMinute(Now.AddMinutes(i - dtu.Count), new Dictionary<string, double?> { ["dtu"] = value, ["storage_gb"] = storageGb, ["sessions"] = 3, ["workers"] = 2, ["log_write"] = 1 }, "test")).ToList();
        var values = new Dictionary<string, double?> { ["dtu"] = dtu.LastOrDefault(), ["storage_gb"] = storageGb };
        var bucket = new ResourceBucket(Now.AddMinutes(-15), dtu.Count, values, values, sums ?? new Dictionary<string, double?> { ["blocked"] = 0, ["deadlocks"] = 0, ["failed"] = 0 }, "test");
        var hours = hour ? new List<ResourceHour> { new(Now.AddHours(-1), "/subscriptions/s/resourceGroups/RG-THEYARD-SS/providers/Microsoft.Sql/servers/sql/databases/db", "tenant", Facts(), events ?? []) } : [];
        return new ResourceCardInputs("24h", TimeSpan.FromHours(24), Now, new PlanHistoryAvailability(true, "kept in the test"), lastHour, [bucket], [bucket], hours, 0.17);
    }

    private static double?[] Minutes(int count, double value) => [.. Enumerable.Repeat<double?>(value, count)];

    [Theory]
    [InlineData(54.9, "good")]
    [InlineData(55, "info")]
    [InlineData(59.9, "info")]
    [InlineData(60, "warning")]
    [InlineData(90, "alarm")]
    public void The_dtu_dial_is_good_under_amber_info_within_five_of_it_warning_from_amber_and_alarm_from_red_held_fifteen_minutes(double percent, string tone)
    {
        var card = SqlCard.View(Inputs(Minutes(15, percent)));

        Assert.Equal(tone, card.Dials[0].Tone);
        Assert.Equal("DTU, right now", card.Dials[0].Name);
        Assert.Equal((60d, 90d), (card.Dials[0].Amber, card.Dials[0].Red));
    }

    [Fact]
    public void Fourteen_minutes_over_red_and_one_under_holds_the_lower_tone_and_fourteen_minutes_alone_are_not_judged()
    {
        Assert.Equal("warning", SqlCard.View(Inputs([.. Minutes(14, 95), 89])).Dials[0].Tone);
        Assert.Equal("good", SqlCard.View(Inputs([.. Minutes(14, 95), 50])).Dials[0].Tone);
        Assert.Equal("info", SqlCard.View(Inputs([.. Minutes(14, 95), 59])).Dials[0].Tone);
        Assert.Equal("info", SqlCard.View(Inputs(Minutes(14, 95))).Dials[0].Tone);
        Assert.Equal("info", SqlCard.View(Inputs([.. Minutes(14, 95), null])).Dials[0].Tone);
    }

    [Theory]
    [InlineData(1.0, "good")]
    [InlineData(1.3, "info")]
    [InlineData(1.4, "warning")]
    [InlineData(1.7, "alarm")]
    public void The_data_space_dial_reads_against_the_database_s_own_cap(double usedGb, string tone)
    {
        var dial = SqlCard.View(Inputs(Minutes(15, 2), storageGb: usedGb)).Dials[1];

        Assert.Equal(tone, dial.Tone);
        Assert.Equal(usedGb / 2 * 100, dial.Percent!.Value, 3);
        Assert.Contains("of 2 GB", dial.Absolute, StringComparison.Ordinal);
    }

    [Fact]
    public void The_four_rows_answer_from_what_was_read_and_a_deadlock_is_red()
    {
        var calm = SqlCard.View(Inputs(Minutes(15, 2)));
        Assert.Equal(["runs", "waiting", "azure", "tier"], calm.Rows.Select(r => r.Key));
        Assert.Equal(("theyard.stevenstout.biz", "good"), (calm.Rows[0].Answer, calm.Rows[0].Tone));
        Assert.Equal(("0 blocked", "good"), (calm.Rows[1].Answer, calm.Rows[1].Tone));
        Assert.Equal(("0 events, 24 hours", "good"), (calm.Rows[2].Answer, calm.Rows[2].Tone));
        Assert.Equal(("Is Basic still right?", "yes, DTU first", "good"), (calm.Rows[3].Question, calm.Rows[3].Answer, calm.Rows[3].Tone));

        var locked = SqlCard.View(Inputs(Minutes(15, 2), sums: new Dictionary<string, double?> { ["blocked"] = 0, ["deadlocks"] = 1, ["failed"] = 0 }));
        Assert.Equal("alarm", locked.Rows[1].Tone);
        Assert.Equal("alarm", locked.Readout.Lines.Single(l => l.Label == "Deadlocks").Tone);

        var hot = SqlCard.View(Inputs(Minutes(15, 95)));
        Assert.Equal(("no, DTU is past its line", "alarm"), (hot.Rows[3].Answer, hot.Rows[3].Tone));
        Assert.Equal("alarm", hot.Rows[1].Tone);
    }

    [Fact]
    public void Nothing_read_says_so_on_every_row()
    {
        var card = SqlCard.View(Inputs([], hour: false, sums: new Dictionary<string, double?>()));

        Assert.Equal("not read yet", card.Rows[0].Answer);
        Assert.Equal("not read yet", card.Rows[1].Answer);
        Assert.Equal("not read yet", card.Rows[2].Answer);
        Assert.False(card.Available);
    }

    [Fact]
    public void An_unplanned_event_from_azure_is_named_on_its_row()
    {
        var card = SqlCard.View(Inputs(Minutes(15, 2), events: [new PlanEvent(Now.AddMinutes(-30), "failover", "Failover", "db", false)]));

        Assert.Equal("1 events, 24 hours", card.Rows[2].Answer);
        Assert.Contains("Failover", card.Rows[2].Detail[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_row_and_readout_line_names_its_source()
    {
        var card = SqlCard.View(Inputs(Minutes(15, 2)));

        Assert.All(card.Rows, row => Assert.False(string.IsNullOrEmpty(row.Source), row.Key));
        Assert.All(card.Readout.Lines, line => Assert.False(string.IsNullOrEmpty(line.Source), line.Label));
    }
}
