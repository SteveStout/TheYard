using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The nightly picker's card (ADR-125, the addendum on the picker's card): the facts read from the Function's two run
/// lines and their requests, each count parsed from the words the Function writes, the rows for a night that ran, one
/// that failed, one that is late and one never read, the blocked postings named as the amber's cause, the boards dial,
/// every row and line sourced, and no company or title anywhere on the wire.
/// </summary>
public sealed class PickerCardTests
{
    // 09:00 CST Saturday 7 November 2026; the night's pick ran at 02:00 CST, the scan at 01:01.
    private static readonly DateTimeOffset Nine = new(2026, 11, 7, 15, 0, 0, TimeSpan.Zero);

    private const string Pick = "jobs: nightly/jobs-candidates-2026-11-07.tsv: scanned 0 postings from 0 boards, 30 alert leads; 20 passed the gates; 0 confirmed by the feeds; postings blocked: no_postings, alert leads alone; tracker sql, 10 failed | dispatch jobs 204";
    private const string Scan = "board scan: 145 boards, 143 answered, 12009 postings, 58 .NET, postings blocked: no_postings, 62 s | 0 board files, 0 boards added";

    private static IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows(string pick = Pick, string pickOk = "True", string pickAt = "2026-11-07T08:00:13Z") =>
    [
        new Dictionary<string, string?> { ["Kind"] = "pick", ["At"] = pickAt, ["Line"] = pick, ["TookMs"] = "14012.5", ["Ok"] = pickOk },
        new Dictionary<string, string?> { ["Kind"] = "scan", ["At"] = "2026-11-07T07:01:08Z", ["Line"] = Scan, ["TookMs"] = "62000", ["Ok"] = "True" },
        new Dictionary<string, string?> { ["Kind"] = "other", ["At"] = "2026-11-07T07:00:00Z", ["Line"] = "ignored" },
    ];

    private static ResourceCardInputs Inputs(IReadOnlyDictionary<string, string?>? facts)
    {
        var values = PickerSource.Readings.ToDictionary(r => r, r => facts is null ? null : PickerSource.Number(facts.GetValueOrDefault(r)), StringComparer.Ordinal);
        var lastHour = facts is null ? [] : new List<ResourceMinute> { new(Nine.AddMinutes(-30), values, "test") };
        var bucket = new ResourceBucket(Nine.AddMinutes(-30), 1, values, values, values, "test");
        IReadOnlyList<ResourceHour> hours = facts is null ? [] : [new ResourceHour(Nine.AddMinutes(-30), "", null, facts, [])];
        return new ResourceCardInputs("24h", TimeSpan.FromDays(1), Nine, new PlanHistoryAvailability(true, "kept in the test"), lastHour, [bucket], [bucket], hours, 3.4);
    }

    private static CloudRow Row(CloudReport report, string key) => report.Rows.Single(row => row.Key == key);

    [Fact]
    public void The_counts_are_read_from_the_words_the_function_writes()
    {
        var facts = PickerSource.Facts(Rows());

        Assert.Equal("30", facts["leads"]);
        Assert.Equal("20", facts["passed"]);
        Assert.Equal("0", facts["confirmed"]);
        Assert.Equal("10", facts["failed"]);
        Assert.Equal("no_postings", facts["blocked"]);
        Assert.Equal("dispatch jobs 204", facts["dispatch"]);
        Assert.Equal("145", facts["boards"]);
        Assert.Equal("143", facts["answered"]);
        Assert.Equal("12009", facts["postings"]);
        Assert.Null(facts["pick_failed"]);
        Assert.False(facts.ContainsKey("other_line"));
        Assert.Contains("'func-jobhunt-ss'", new PickerSource(null!, "/w", "func-jobhunt-ss").Query, StringComparison.Ordinal);
        Assert.Equal("func-jobhunt-ss", new PickerSource(null!, "/w", "func-jobhunt-ss").Function);
    }

    [Fact]
    public void A_night_that_ran_reads_fine_with_the_blocked_postings_named_as_the_one_amber()
    {
        var report = PickerCard.View(Inputs(PickerSource.Facts(Rows())));

        Assert.Equal(("good", "yes, Sat 7 Nov 02:00, 14 s"), (Row(report, "pick").Tone, Row(report, "pick").Answer));
        Assert.Equal("dispatch: dispatch jobs 204", Row(report, "pick").Detail[0].Text);
        Assert.Equal(("good", "yes, Sat 7 Nov 01:01, 62 s"), (Row(report, "scan").Tone, Row(report, "scan").Answer));
        Assert.Equal("143 of 145 boards answered, 12009 postings", Row(report, "scan").Detail[0].Text);
        Assert.Equal("warning", Row(report, "supply").Tone);
        Assert.Equal("30 alert leads, 20 passed, 0 confirmed by the feeds, 10 failed", Row(report, "supply").Answer);
        Assert.StartsWith("postings blocked: no_postings", Row(report, "supply").Detail[0].Text, StringComparison.Ordinal);
        Assert.Equal(66.67, report.Dials[0].Percent);
        Assert.Equal(1.4, report.Dials[1].Percent);
        Assert.Equal("good", report.Dials[1].Tone);
    }

    [Fact]
    public void A_failed_pick_reads_red_a_late_one_amber_and_a_night_with_no_postings_blocked_reads_fine()
    {
        var failed = PickerCard.View(Inputs(PickerSource.Facts(Rows(pickOk: "False"))));
        var thrown = PickerCard.View(Inputs(PickerSource.Facts(Rows(pick: "jobs: FAILED: SqlException | dispatch jobs 204"))));
        var late = PickerCard.View(Inputs(PickerSource.Facts(Rows(pickAt: "2026-11-06T08:00:13Z"))));
        var open = PickerCard.View(Inputs(PickerSource.Facts(Rows(pick: Pick.Replace("postings blocked: no_postings, alert leads alone; ", "", StringComparison.Ordinal)))));

        Assert.Equal("alarm", Row(failed, "pick").Tone);
        Assert.Equal("alarm", Row(thrown, "pick").Tone);
        Assert.Equal(("warning", "late: the newest is Fri 6 Nov 02:00"), (Row(late, "pick").Tone, Row(late, "pick").Answer));
        Assert.Equal("good", Row(open, "supply").Tone);
    }

    [Fact]
    public void Before_the_first_read_every_row_waits_and_the_dials_say_not_read()
    {
        var report = PickerCard.View(Inputs(null));
        var empty = PickerCard.View(Inputs(PickerSource.Facts([])));

        Assert.All(report.Rows.Where(row => row.Key != "cost"), row => Assert.Equal(("info", "not read yet"), (row.Tone, row.Answer)));
        Assert.Equal("not read", report.Dials[0].Big);
        Assert.Equal("not read", report.Dials[1].Big);
        Assert.Equal(("warning", "no pick in the last two days"), (Row(empty, "pick").Tone, Row(empty, "pick").Answer));
    }

    [Fact]
    public void Every_row_and_line_names_its_source_and_nothing_names_a_company()
    {
        var report = PickerCard.View(Inputs(PickerSource.Facts(Rows())));
        string wire = System.Text.Json.JsonSerializer.Serialize(report);

        Assert.All(report.Rows, row => Assert.False(string.IsNullOrEmpty(row.Source), row.Key));
        Assert.All(report.Readout.Lines, line => Assert.False(string.IsNullOrEmpty(line.Source), line.Label));
        Assert.DoesNotContain("jobs-candidates-2026", wire, StringComparison.Ordinal);
        Assert.DoesNotContain(".NET", wire, StringComparison.Ordinal);
    }

    [Fact]
    public void A_line_the_parser_does_not_know_gives_empty_counts_and_never_throws()
    {
        var none = PickerSource.Counts(null, null).ToDictionary(c => c.Key, c => c.Value);
        var thrown = PickerSource.Counts("jobs: FAILED: SqlException | dispatch jobs 204", "board scan: the share did not answer").ToDictionary(c => c.Key, c => c.Value);

        Assert.All(none.Where(c => c.Key != "pick_failed"), c => Assert.Equal("", c.Value));
        Assert.Null(none["pick_failed"]);
        Assert.Equal("yes", thrown["pick_failed"]);
        Assert.Equal("dispatch jobs 204", thrown["dispatch"]);
        Assert.Equal("", thrown["boards"]);
        Assert.Equal("", thrown["leads"]);
    }

    [Theory]
    [InlineData("14012.5", 14012.5)]
    [InlineData("0", 0.0)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("not a number", null)]
    public void A_fact_is_a_number_or_nothing(string? fact, double? number) => Assert.Equal(number, PickerSource.Number(fact));

    [Fact]
    public void The_role_is_quoted_so_a_name_cannot_close_the_query()
    {
        var source = new PickerSource(null!, "/w", "o'brien");

        Assert.Contains("let role = 'o''brien';", source.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("__ROLE__", source.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Off_azure_the_source_reads_nothing_and_says_nothing()
    {
        var source = new PickerSource(new AzureMetrics("client", configured: false), "/w", "func-jobhunt-ss");

        Assert.Empty(await source.MinutesAsync(Nine.AddMinutes(-1), Nine, "1.0.4.0", CancellationToken.None));
        Assert.Empty((await source.HourAsync(Nine, Nine.AddHours(-1), CancellationToken.None)).Facts);
    }

    [Theory]
    [InlineData("145", "143", "good")]
    [InlineData("100", "88", "warning")]
    [InlineData("100", "70", "alarm")]
    public void The_boards_dial_turns_amber_at_a_tenth_silent_and_red_at_a_quarter(string boards, string answered, string tone)
    {
        var facts = PickerSource.Facts(Rows());
        facts["boards"] = boards;
        facts["answered"] = answered;

        Assert.Equal(tone, PickerCard.View(Inputs(facts)).Dials[1].Tone);
    }

    [Fact]
    public void The_cost_row_reads_the_functions_line_of_the_bill_or_says_it_is_not_on_it()
    {
        var billed = PickerCard.CostRow(Inputs(PickerSource.Facts(Rows())));
        var unbilled = PickerCard.CostRow(Inputs(PickerSource.Facts(Rows())) with { CostMonth = null });

        Assert.Equal(("info", "$3.40 this month, the whole Function"), (billed.Tone, billed.Answer));
        Assert.Equal("not on the bill yet", unbilled.Answer);
    }

    [Fact]
    public void A_failed_scan_reads_red_and_a_scan_a_day_and_an_hour_old_reads_late()
    {
        var rows = Rows().Select(row => row["Kind"] == "scan" ? new Dictionary<string, string?>(row) { ["Ok"] = "False" } : row).ToList();
        var failed = PickerCard.View(Inputs(PickerSource.Facts(rows)));
        var facts = PickerSource.Facts(Rows());
        facts["scan_at"] = Nine.Add(-PickerCard.Due).AddMinutes(-1).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        var late = PickerCard.View(Inputs(facts));
        facts["scan_at"] = Nine.Add(-PickerCard.Due).AddMinutes(1).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        var onTime = PickerCard.View(Inputs(facts));

        Assert.Equal(("alarm", "failed, Sat 7 Nov 01:01"), (Row(failed, "scan").Tone, Row(failed, "scan").Answer));
        Assert.Equal("warning", Row(late, "scan").Tone);
        Assert.StartsWith("late: the newest is Fri 6 Nov", Row(late, "scan").Answer, StringComparison.Ordinal);
        Assert.Equal("good", Row(onTime, "scan").Tone);
    }

    [Fact]
    public void A_night_whose_postings_were_read_names_both_sources_and_a_run_with_no_time_says_so()
    {
        var facts = PickerSource.Facts(Rows(pick: Pick.Replace("postings blocked: no_postings, alert leads alone; ", "", StringComparison.Ordinal)));
        facts["pick_ms"] = null;
        var report = PickerCard.View(Inputs(facts));

        Assert.Equal("the boards' own postings and the alert leads", Row(report, "supply").Detail[0].Text);
        Assert.Equal("yes, Sat 7 Nov 02:00, time not read", Row(report, "pick").Answer);
        Assert.Equal("The nightly picker", report.Title);
        Assert.Null(report.Resource);
        Assert.Equal(4, report.Rows.Count);
        Assert.Equal(6, report.Readout.Lines.Count);
    }
}
