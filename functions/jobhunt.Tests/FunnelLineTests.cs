using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// The job hunt's funnel (PLAN-AUTOHUNT rung 6; FunnelCount, FunnelWeek, FunnelSource, FunnelTimer): one Central day
/// counted from the scan's pass.log line, the JOBS file and the opening phrase of each tracker row, a person once a
/// day; the run line in the shape the Admin tab's funnel card parses; the share's log summed into the week's line on
/// TODAY.md, each day its latest line and a missing day named. Made-up people and companies throughout.
/// </summary>
public sealed class FunnelLineTests
{
    private static readonly DateOnly Monday = new(2026, 11, 9);

    private const string Rows =
        "1,NA,Acme,Application,11/9/2026,,APPLICATION SUBMITTED by a lane via Greenhouse\r\n"
        + "2,,Globex,Greenhouse,11/09/2026,,SUBMITTED AND CONFIRMED ON PAGE\r\n"
        + "3,,Initech,Application,11/9/2026,,Applied (a lane) via Lever\r\n"
        + "4,,Umbrella,Company Website,11/9/2026,,\"NOT APPLIED, BLOCKER: an account wall\"\r\n"
        + "5,NA,Acme,Email,11/9/2026,,\"RECEIPT, generic, for the Acme application\"\r\n"
        + "6,Ann Lee,Acme (VP),LinkedIn,11/9/2026,,CONNECTION REQUEST SENT by a lane\r\n"
        + "7,Ann Lee,Acme,Email,11/9/2026,,EMAIL SENT on the owner's send\r\n"
        + "8,Bo Diaz,Globex,LinkedIn,11/9/2026,,INMAIL SENT by a lane\r\n"
        + "9,Cy Fox,Initech,Email,11/9/2026,,CAMPAIGN EMAIL SENT 8:00 AM by the campaign\r\n"
        + "10,Di Ng,Hooli,LinkedIn,11/9/2026,,SECOND AND FINAL FOLLOW-UP MESSAGE SENT by a lane\r\n"
        + "11,Ed Ruiz,Vandelay,Email,11/9/2026,,SENT by the owner from his Gmail\r\n"
        + "12,Flo Kim,Pied Piper,Phone,11/9/2026,,\"STATUS: SILENT 60+ DAYS, NO MESSAGE SENT.\"\r\n"
        + "13,Gus Hale,Soylent,Email,11/9/2026,,DECLINE SENT BY a lane\r\n"
        + "14,Hal Ito,Wonka,Email,11/9/2026,,INBOUND 9:01 AM: a reply\r\n"
        + "15,NA,Wonka,Email,11/9/2026,,INBOUND 9:02 AM: an automated note\r\n"
        + "16,Ivy Wu,Stark,Email,11/9/2026,,\"INBOUND REJECTION, generic\"\r\n"
        + "17,Jo Park,Tyrell,Calendar,11/9/2026,,INTERVIEW BOOKED: recruiter screen\r\n"
        + "18,Kim Roe,Cyberdyne,Interview,2026-11-09,,ROUND 2 INTERVIEW SCHEDULED AND PREPPED\r\n"
        + "19,Lu Chan,Oscorp,LinkedIn,11/8/2026,,CONNECTION REQUEST SENT by a lane\r\n"
        + "20,Mo Ali,Oscorp,Email,11/8/2026,,INBOUND the day before\r\n";

    private const string PassLog = "2026-11-08 01:00:46 scan: 145 boards, 144 answered, 12469 postings, 58 .NET, 40 s\n"
        + "2026-11-09 01:01:04 scan: 145 boards, 144 answered, 12467 postings, 57 .NET, 58 s\n"
        + "2026-11-09 08:00:10 scan: scanned 01:00 | track A: 44 applications in 7 days\n";

    private const string Jobs = "# JOBS\n\n## READY TO APPLY\n\n| # | Company |\n|---|---|\n| 1 | Acme |\n| 2 | Globex |\n\n## SKIPPED\n\n| 1 | x |\n\nSupply: scanned 4086 postings from 57 boards, 36 alert leads; 45 passed the gates; 32 confirmed; 5 ready.\n";

    private const string Leaders = "> RULES FIRST\n\n# OUTREACH-DRAFTS.md\n\n## Acme - Ann Lee, VP Engineering\n- Seat: read\n\n## Globex - Ned Fry, CTO\n- Seat: read\n";

    private const string LinkedIn = "Person\tCompany\tProfileUrl\tThreadId\tLastInbound\tLastOutbound\tFirstTwoWay\n"
        + "Hal Ito\tWonka\thttps://www.linkedin.com/in/hal/\t2-a\t2026-11-09T21:41:00Z\t2026-11-08T15:00:00Z\t2026-10-24T13:00:00Z\n"
        + "Oz Kane\tAcme\thttps://www.linkedin.com/in/oz/\t2-b\t2026-11-10T02:30:00Z\t\t2026-10-20T13:00:00Z\n"
        + "Pat Su\tAcme\thttps://www.linkedin.com/in/pat/\t2-c\t2026-11-10T07:30:00Z\t\t2026-10-20T13:00:00Z\n";

    [Fact]
    public void A_day_counts_each_step_from_its_own_source_and_each_person_once()
    {
        var day = FunnelCount.Of(Monday, new FunnelInputs(Scratch.Header + "\r\n" + Rows, PassLog, Jobs, Leaders, [LinkedIn]));

        Assert.Equal(12467, day.Scanned);
        Assert.Equal(5, day.Ready);
        Assert.Equal(3, day.Applied);
        Assert.Equal((1, 1, 1, 1, 1), (day.Invites, day.InMails, day.Campaign, day.Messages, day.Emails));
        Assert.Equal(5, day.Reached);
        Assert.Equal(2, day.Leaders);
        Assert.Equal(2, day.Replied);
        Assert.Equal(2, day.Booked);
    }

    [Fact]
    public void A_send_that_was_not_one_is_never_a_touch_and_the_channel_is_read_off_the_phrase()
    {
        Assert.Null(FunnelCount.Channel("STATUS: SILENT 60+ DAYS, NO MESSAGE SENT.", "Phone"));
        Assert.Null(FunnelCount.Channel("DECLINE SENT BY CLAUDE", "Email"));
        Assert.Null(FunnelCount.Channel("Message sent in prose, not a type", "LinkedIn"));
        Assert.Equal("invites", FunnelCount.Channel("MANAGER OUTREACH, SCOPED CONNECTION REQUEST SENT BY CLAUDE", "LinkedIn"));
        Assert.Equal("messages", FunnelCount.Channel("FOLLOW-UP MESSAGE SENT by a lane", "LinkedIn"));
        Assert.Equal("emails", FunnelCount.Channel("EMAIL SENT BY STEVE HIMSELF", "Email"));
    }

    [Fact]
    public void Ready_falls_back_to_the_ready_table_and_a_missing_file_reads_zero()
    {
        Assert.Equal(2, FunnelCount.Ready("## READY TO APPLY\n\n| # | Company |\n|---|---|\n| 1 | Acme |\n| 2 | Globex |\n\n## SKIPPED\n| 3 | x |\n"));
        Assert.Equal(0, FunnelCount.Ready(""));
        Assert.Equal(0, FunnelCount.Scanned("", Monday));
        Assert.Null(FunnelCount.Date("Mon"));
    }

    [Fact]
    public void The_run_line_carries_the_day_as_its_detail_and_the_six_counts_the_card_reads()
    {
        var run = new RunLine(FunnelCount.Process, new DateTimeOffset(2026, 11, 10, 13, 15, 0, TimeSpan.Zero)).Came("success", "2026-11-09");
        foreach (var (name, value) in new FunnelDay(Monday, 212, 14, 9, 8, 1, 6, 2, 1, 3, 2, 1).Named)
        {
            run.Count(name, value);
        }
        string text = run.Text(new DateTimeOffset(2026, 11, 10, 13, 15, 1, TimeSpan.Zero));

        Assert.StartsWith("process:Funnel outcome=success took_ms=1000 started=2026-11-10T13:15:00Z detail=2026-11-09 applied=9 booked=1 campaign=6", text, StringComparison.Ordinal);
        foreach (var (name, value) in new[] { ("scanned", 212), ("ready", 14), ("applied", 9), ("reached", 18), ("replied", 2), ("booked", 1) })
        {
            Assert.Matches($"(?:^| ){name}={value}(?: |$)", text);
        }
        Assert.Null(RunLine.Word("2026-11-09-x"));
        Assert.Null(RunLine.Word("a-b"));
    }

    [Fact]
    public void The_week_sums_each_day_s_latest_line_and_names_the_days_with_none()
    {
        string log = "2026-11-09 18:30:00 hand: process:Funnel outcome=success took_ms=9 started=2026-11-10T00:30:00Z detail=2026-11-09 applied=1 reached=1 scanned=10\n"
            + "2026-11-10 07:15:02 timer: process:Funnel outcome=success took_ms=9 started=2026-11-10T13:15:00Z detail=2026-11-09 applied=3 booked=1 emails=2 invites=4 reached=6 replied=1 scanned=100 ready=5 leaders=2\n"
            + "2026-11-12 07:15:02 timer: process:Funnel outcome=success took_ms=9 started=2026-11-12T13:15:00Z detail=2026-11-11 applied=2 reached=1 messages=1 scanned=50\n"
            + "2026-11-12 07:15:09 timer: process:Funnel outcome=failed took_ms=9 started=2026-11-12T13:15:00Z detail=IOException\n";

        string week = FunnelWeek.Text(log, new DateOnly(2026, 11, 11));

        Assert.Equal("Funnel, week of Mon 11/9, 2 of 7 days read (Mon 11/9 to Wed 11/11): 150 postings scanned, 5 READY, 5 applied, 7 humans reached (4 invites, 0 InMails, 0 campaign, 1 LinkedIn messages, 2 emails; 2 leaders named), 1 human replies, 1 calls booked, against 2 to 3 a week. No line for Tue 11/10.", week);
        Assert.Contains("(Mon 11/9 to Mon 11/9, Mon 11/9 so far): 10 postings", FunnelWeek.Text(log.Split('\n')[0], Monday), StringComparison.Ordinal);
        Assert.Equal(Monday, FunnelWeek.Monday(new DateOnly(2026, 11, 15)));
    }

    [Fact]
    public void The_line_goes_under_the_written_line_once_and_is_replaced_where_it_stands()
    {
        string card = "> RULES FIRST\n\n# TODAY, Monday 11/9\n\nWritten 07:30 Central by the Function.\n\n## CRITICAL sends\n";

        string once = FunnelWeek.Splice(card, FunnelWeek.Opening + "one");
        string twice = FunnelWeek.Splice(once, FunnelWeek.Opening + "two");

        Assert.Equal("> RULES FIRST\n\n# TODAY, Monday 11/9\n\nWritten 07:30 Central by the Function.\n\nFunnel, week of one\n\n## CRITICAL sends\n", once);
        Assert.Equal(once.Replace("of one", "of two", StringComparison.Ordinal), twice);
    }

    [Fact]
    public async Task The_run_counts_from_the_share_logs_its_line_twice_over_and_the_hand_run_puts_the_week_on_today()
    {
        using var scratch = new Scratch();
        scratch.Master(Rows);
        scratch.Write("campaign/pass.log", PassLog);
        scratch.Write("JOBS-2026-11-09.md", Jobs);
        scratch.Write("staging/apply-2026-11-09-leaders/OUTREACH-DRAFTS.md", Leaders);
        scratch.Write("networklog/linkedin-inbox/done/a.tsv", LinkedIn);
        scratch.Write(TodayCard.Path, "> RULES FIRST\n\n# TODAY\n\nWritten 07:30 Central.\n\n## CRITICAL sends\n");
        var log = new Lines();
        var time = new FixedTime(new DateTimeOffset(2026, 11, 10, 13, 15, 2, TimeSpan.Zero));

        Assert.False(await FunnelTimer.WrittenAsync(scratch.Store, Monday, CancellationToken.None));
        string line = await FunnelTimer.WriteAsync(scratch.Store, time, log, Monday, "timer", CancellationToken.None);
        string week = await FunnelTimer.SpliceAsync(scratch.Store, Monday, CancellationToken.None);

        Assert.Equal("2026-11-10 07:15:02 timer: process:Funnel outcome=success took_ms=0 started=2026-11-10T13:15:02Z detail=2026-11-09 applied=3 booked=2 campaign=1 emails=1 inmails=1 invites=1 leaders=2 messages=1 reached=5 ready=5 replied=2 scanned=12467", line);
        Assert.Equal(line + "\n", scratch.Text(FunnelWeek.Log));
        Assert.Equal(line[(line.IndexOf("process:", StringComparison.Ordinal))..], Assert.Single(log.Written));
        Assert.True(await FunnelTimer.WrittenAsync(scratch.Store, Monday, CancellationToken.None));
        Assert.StartsWith("Funnel, week of Mon 11/9, 1 of 7 days read (Mon 11/9 to Mon 11/9): 12467 postings scanned, 5 READY, 3 applied, 5 humans reached", week, StringComparison.Ordinal);
        Assert.Contains("Written 07:30 Central.\n\n" + week + "\n\n## CRITICAL sends", scratch.Text(TodayCard.Path), StringComparison.Ordinal);
        Assert.Equal(("on", "off"), (FunnelTimer.Mode(null), FunnelTimer.Mode(" OFF ")));
    }

    [Fact]
    public void The_morning_card_draws_the_week_s_line_under_its_written_line()
    {
        var inputs = new TodayInputs(new DateTime(2026, 11, 10, 7, 30, 0), null, "", new Dictionary<string, string>(), new HashSet<string>(), new Dictionary<string, string>(),
            "", "", "", "", "", null, "", "", [], FunnelWeek.Opening + "Mon 11/9.");

        Assert.Matches(new Regex(@"Written 07:30 Central [^\n]+\n\nFunnel, week of Mon 11/9\.\n\n## CRITICAL sends"), TodayCard.Render(inputs));
        Assert.DoesNotContain("Funnel, week of", TodayCard.Render(inputs with { Funnel = "" }), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_share_reader_reads_both_linkedin_folders_and_a_missing_file_as_empty()
    {
        using var scratch = new Scratch();
        scratch.Write("networklog/linkedin-inbox/waiting.tsv", LinkedIn);
        scratch.Write("networklog/linkedin-inbox/done/read.tsv", LinkedIn);
        scratch.Write("networklog/linkedin-inbox/done/refused.log", "line 2: no profile");

        var inputs = await new FunnelSource(scratch.Store).ReadAsync(Monday, CancellationToken.None);

        Assert.Equal(2, inputs.LinkedIn.Count);
        Assert.Equal(("", "", "", ""), (inputs.Tracker, inputs.PassLog, inputs.Jobs, inputs.Leaders));
        Assert.Equal("", await new FunnelSource(scratch.Store).LogAsync(CancellationToken.None));
        Assert.Equal(2, FunnelCount.Of(Monday, inputs).Replied);
    }

    [Fact]
    public async Task Only_the_timer_s_own_line_marks_a_day_written_so_a_hand_run_never_stops_the_morning_run()
    {
        using var scratch = new Scratch();
        scratch.Write(FunnelWeek.Log, "2026-11-09 18:30:00 hand: process:Funnel outcome=success took_ms=9 started=2026-11-10T00:30:00Z detail=2026-11-09 scanned=1\n");

        Assert.False(await FunnelTimer.WrittenAsync(scratch.Store, Monday, CancellationToken.None));

        await scratch.Store.AppendAsync(FunnelWeek.Log, TrackerCsvText.Encode("2026-11-10 07:15:00 timer: process:Funnel outcome=success took_ms=9 started=2026-11-10T13:15:00Z detail=2026-11-09 scanned=2\n"), CancellationToken.None);

        Assert.True(await FunnelTimer.WrittenAsync(scratch.Store, Monday, CancellationToken.None));
        Assert.False(await FunnelTimer.WrittenAsync(scratch.Store, Monday.AddDays(1), CancellationToken.None));
        Assert.Equal(2, FunnelWeek.Days(scratch.Text(FunnelWeek.Log))[Monday].Day.Scanned);
    }

    [Fact]
    public void A_failed_line_or_a_stray_line_is_never_a_day_and_a_card_with_no_written_line_takes_the_line_first()
    {
        string log = "not a line\n"
            + "2026-11-10 07:15:00 timer: process:Funnel outcome=failed took_ms=9 started=2026-11-10T13:15:00Z detail=IOException\n"
            + "2026-11-10 07:15:00 timer: process:TodayCard outcome=success took_ms=9 started=2026-11-10T13:15:00Z detail=2026-11-09 scanned=9\n";

        Assert.Empty(FunnelWeek.Days(log));
        Assert.EndsWith("0 calls booked, against 2 to 3 a week. No line for Mon 11/9, Tue 11/10.", FunnelWeek.Text(log, Monday.AddDays(1)), StringComparison.Ordinal);
        Assert.Equal("Funnel, week of x\n\n# TODAY\n", FunnelWeek.Splice("# TODAY\n", FunnelWeek.Opening + "x"));
        Assert.Equal("2026-11-10 07:15:02 timer: process:Funnel", FunnelWeek.LogLine(new DateTime(2026, 11, 10, 7, 15, 2), "timer", "process:Funnel"));
    }

    [Theory]
    [InlineData("INTERVIEW BOOKED: a screen", 1)]
    [InlineData("BOOKED 2:10 PM by a lane", 1)]
    [InlineData("CALL BOOKED for Tuesday", 1)]
    [InlineData("SCREEN BOOKED for Wednesday", 1)]
    [InlineData("INTERVIEW SCHEDULED with the panel", 1)]
    [InlineData("INTERVIEW HELD 1:00 PM", 0)]
    [InlineData("Booked in prose is not a type", 0)]
    public void A_call_is_booked_only_by_the_row_s_opening_phrase(string notes, int booked)
    {
        string tracker = Scratch.Header + "\r\n1,Jo Park,Tyrell,Calendar,11/9/2026,,\"" + notes + "\"\r\n";

        Assert.Equal(booked, FunnelCount.Of(Monday, new FunnelInputs(tracker, "", "", "", [])).Booked);
    }

    [Fact]
    public void Two_scan_lines_on_one_day_add_up_and_the_day_before_s_never_does()
    {
        string log = PassLog + "2026-11-09 13:00:00 scan: 10 boards, 10 answered, 33 postings, 1 .NET, 4 s\n";

        Assert.Equal(12500, FunnelCount.Scanned(log, Monday));
        Assert.Equal(12469, FunnelCount.Scanned(log, Monday.AddDays(-1)));
        Assert.Equal(new DateOnly(2026, 11, 9), FunnelCount.Date(" 2026-11-09 "));
    }

    [Fact]
    public void The_day_names_the_card_s_six_counts_first_and_reached_is_the_five_channels_only()
    {
        var day = new FunnelDay(Monday, 1, 2, 3, 4, 5, 6, 7, 8, 100, 9, 10);
        var named = day.Named.ToDictionary(n => n.Name, n => n.Value, StringComparer.Ordinal);

        Assert.Equal(["scanned", "ready", "applied", "reached", "replied", "booked"], day.Named.Take(6).Select(n => n.Name));
        Assert.Equal(4 + 5 + 6 + 7 + 8, day.Reached);
        Assert.Equal(100, named["leaders"]);
        Assert.Equal(12, named.Count);
        Assert.All(named.Keys, name => Assert.Matches("^[a-z][a-z0-9_]{0,30}$", name));
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Lines : ILogger
    {
        public List<string> Written { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Written.Add(formatter(state, exception));
    }
}
