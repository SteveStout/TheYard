using System.Text.RegularExpressions;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Tests;

// #region process-runs-tests
/// <summary>
/// The job hunt Function's six processes on the nightly card (ADR-109, the addendum on the six processes): the
/// runs handler with a fake reader and no host, the words every outcome and process prints, the strip's cells,
/// the schedule table against the Function's own cron strings, and the six sections built from a fold.
/// </summary>
public sealed class ProcessRunsTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 24, 17, 0, 0, TimeSpan.Zero);

    private sealed class FakeRuns(IReadOnlyList<ProcessRun> rows, ProcessTally totals) : IProcessRunsReader
    {
        public List<(string Process, TimeSpan Window, int Skip, int Take)> Asked { get; } = [];

        public Task<ProcessRunsRead> RunsAsync(FunctionProcess process, TimeSpan window, int skip, int take, CancellationToken cancellation)
        {
            Asked.Add((process.Key, window, skip, take));
            return Task.FromResult(new ProcessRunsRead(true, null, [.. rows.Skip(skip).Take(take)], totals));
        }

        public Task<ProcessFold> FoldAsync(TimeSpan window, CancellationToken cancellation) => Task.FromResult(ProcessFold.Unread("fake"));

        public string? TraceHref(string operationId) => $"https://trace/{operationId}";

        public string? ProcessHref(string process) => $"https://all/{process}";
    }

    private static ProcessRun Run(string line, int minutesAgo = 0, string op = "op") =>
        ProcessRun.From(Noon.AddMinutes(-minutesAgo), 91_000, true, line, op);

    [Fact]
    public async Task A_page_is_twenty_runs_newest_first_with_its_words_its_trace_and_the_window_s_totals()
    {
        var rows = Enumerable.Range(0, 25).Select(i => Run($"process:AtsSweep outcome=success took_ms=91000 started=x read={42 - i} archived=9 leads=6 leads_passed=2 waiting=0", i * 30, $"op-{i}")).ToList();
        var fake = new FakeRuns(rows, new ProcessTally(25, 25, 25, 0, 0, 0, 0, 95_000));
        var handler = new ReadProcessRunsHandler(fake);

        var page = (await handler.HandleAsync(new ReadProcessRunsQuery("AtsSweep", "sideways", null), CancellationToken.None))!;

        Assert.Equal(("7d", 1, 20, true), (page.Window, page.Page, page.Rows.Count, page.More));
        Assert.Equal(("AtsSweep", TimeSpan.FromDays(7), 0, 20), fake.Asked[0]);
        var first = page.Rows[0];
        Assert.Equal(("Sat 24 Oct", "12:00", "1 min 31 s", "success", "good"), (first.Day, first.When, first.Took, first.Came, first.Tone));
        Assert.Equal("42 read · 9 archived · 6 leads, 2 passed · 0 waiting", first.Sentence);
        Assert.Equal("https://trace/op-0", first.TraceHref);
        Assert.Equal("25 runs in 7 days, the newest 20 first", page.Count);
        Assert.Equal("25 in 7 days · 0 late · 0 blocked · 0 failed", page.Totals);
        Assert.Equal("https://all/AtsSweep", page.AllHref);

        var past = (await handler.HandleAsync(new ReadProcessRunsQuery("AtsSweep", "30d", 9), CancellationToken.None))!;
        Assert.Empty(past.Rows);
        Assert.False(past.More);
        Assert.Equal("25 in 30 days · 0 late · 0 blocked · 0 failed", past.Totals);
    }

    [Fact]
    public async Task A_page_past_the_last_is_read_as_the_last_and_the_health_route_s_slowest_are_drawn_newest_first()
    {
        var rows = new[] { Run("process:MergerHealth outcome=success took_ms=900 started=x waiting=0", 90, "slow"), Run("process:MergerHealth outcome=success took_ms=800 started=x waiting=0", 10, "newer") };
        var fake = new FakeRuns(rows, new ProcessTally(2, 2, 2, 0, 0, 0, 0, 900));
        var handler = new ReadProcessRunsHandler(fake);

        var far = (await handler.HandleAsync(new ReadProcessRunsQuery("Merger", "7d", 214_748_365), CancellationToken.None))!;
        Assert.Equal(ReadProcessRunsHandler.MaxPage, far.Page);
        Assert.Equal((ReadProcessRunsHandler.MaxPage - 1) * ReadProcessRunsHandler.PageSize, fake.Asked[^1].Skip);

        var health = (await handler.HandleAsync(new ReadProcessRunsQuery("MergerHealth", "24h", 1), CancellationToken.None))!;
        Assert.Equal(["https://trace/newer", "https://trace/slow"], health.Rows.Select(r => r.TraceHref));
    }

    [Fact]
    public void A_detail_that_is_not_one_word_is_not_read_and_a_late_dispatch_names_its_slot_plainly()
    {
        Assert.Null(Run("process:AtsSweep outcome=blocked took_ms=1 started=x detail=someone@example.com").Detail);
        Assert.Equal("blocked, no reason given", ProcessWords.Came(Run("process:AtsSweep outcome=blocked took_ms=1 started=x detail=a.b")));
        Assert.Equal("dispatched the slot", ProcessWords.Sentence("Nightly", Run("process:Nightly outcome=late took_ms=1 started=x detail=75 dispatched=1")));
    }

    [Fact]
    public async Task A_name_not_among_the_six_is_no_page_and_reads_nothing()
    {
        var fake = new FakeRuns([], ProcessTally.None);

        Assert.Null(await new ReadProcessRunsHandler(fake).HandleAsync(new ReadProcessRunsQuery("Vault", "7d", 1), CancellationToken.None));
        Assert.Null(await new ReadProcessRunsHandler(fake).HandleAsync(new ReadProcessRunsQuery("atssweep", "7d", 1), CancellationToken.None));
        Assert.Empty(fake.Asked);
    }

    [Theory]
    [InlineData("process:X outcome=success took_ms=1 started=x", "success", "good")]
    [InlineData("process:X outcome=late took_ms=1 started=x detail=62", "late, 1 min 2 s after its slot", "warning")]
    [InlineData("process:X outcome=blocked took_ms=1 started=x detail=google_token", "blocked, the Google token was refused", "alarm")]
    [InlineData("process:X outcome=failed took_ms=1 started=x detail=IOException", "failed, IOException", "alarm")]
    [InlineData("process:X outcome=planned took_ms=1 started=x", "planned, acted on nothing", "info")]
    [InlineData("process:X outcome=unchanged took_ms=1 started=x detail=switch_off", "unchanged, the switch is off", "quiet")]
    public void Every_outcome_word_renders_with_its_tone(string line, string came, string tone)
    {
        var run = Run(line);

        Assert.Equal(came, ProcessWords.Came(run));
        Assert.Equal(tone, ProcessWords.Tone(run.Outcome));
        Assert.Contains(run.Outcome, ProcessWords.Outcomes);
    }

    [Theory]
    [InlineData("Merger", "process:Merger outcome=success took_ms=8 started=x merged=3", "3 merged")]
    [InlineData("Merger", "process:Merger outcome=unchanged took_ms=8 started=x merged=0", "nothing waiting")]
    [InlineData("Merger", "process:Merger outcome=unchanged took_ms=8 started=x merged=0 tracker_sql_rows=3005 tracker_sql_written=0", "nothing waiting · the SQL copy in step")]
    [InlineData("Merger", "process:Merger outcome=success took_ms=8 started=x merged=2 tracker_sql_rows=3005 tracker_sql_written=1 tracker_sql_parity=1 tracker_sql_csv_rows=3005 tracker_sql_sql_rows=3005", "2 merged · 1 row copied · parity same")]
    [InlineData("Merger", "process:Merger outcome=unchanged took_ms=8 started=x merged=0 tracker_sql_rows=3005 tracker_sql_written=0 tracker_sql_parity=0 tracker_sql_csv_rows=3005 tracker_sql_sql_rows=3004", "nothing waiting · the SQL copy in step · PARITY DIFFERENT")]
    [InlineData("Merger", "process:Merger outcome=unchanged took_ms=8 started=x merged=0 tracker_sql_failed=1", "nothing waiting · copy failed, retried next pass")]
    [InlineData("Nightly", "process:Nightly outcome=success took_ms=8 started=x detail=bookkeeping dispatched=1", "dispatched the records run")]
    [InlineData("Nightly", "process:Nightly outcome=unchanged took_ms=8 started=x detail=not_a_slot", "not a slot hour")]
    [InlineData("SheetMirror", "process:SheetMirror outcome=planned took_ms=8 started=x master_rows=2996 sheet_rows=2993 would_append=3", "3 rows would append · Sheet 2,993 · tracker 2,996")]
    [InlineData("SheetMirror", "process:SheetMirror outcome=blocked took_ms=8 started=x detail=gate master_rows=2994 sheet_rows=2852 would_append=0", "stopped at a row the gate holds; the rows after it wait · 0 rows would append · Sheet 2,852 · tracker 2,994")]
    [InlineData("DailyCard", "process:DailyCard outcome=unchanged took_ms=8 started=x written=0", "unchanged, not written")]
    [InlineData("MergerHealth", "process:MergerHealth outcome=success took_ms=8 started=x newest_log_age_s=7440 past_10=0 past_5=0 waiting=0", "0 waiting · 0 past 5 min · 0 past 10 · newest log 2 h 4 min")]
    [InlineData("AtsSweep", "process:AtsSweep outcome=blocked took_ms=8 started=x detail=google_token", "the Google token was refused; the pass skipped its later parts")]
    [InlineData("AtsSweep", null, "no run line, from before the processes wrote one: the request row only")]
    public void Each_process_s_sentence_is_built_from_its_counts_by_its_own_table(string process, string? line, string sentence)
    {
        Assert.Equal(sentence, ProcessWords.Sentence(process, Run(line!)));
    }

    [Fact]
    public void A_run_line_carries_counts_and_names_only_and_a_row_without_one_is_the_request_s_word()
    {
        var run = Run("process:AtsSweep outcome=success took_ms=91000 started=2026-10-24T17:00:00Z read=42 note=hello@example.com");

        Assert.True(run.HasLine);
        Assert.Equal(["read"], run.Counts.Keys);
        Assert.All(run.Counts.Keys, name => Assert.Matches("^[a-z0-9_]+$", name));

        var bare = ProcessRun.From(Noon, 400, false, null, null);
        Assert.Equal(("failed", false), (bare.Outcome, bare.HasLine));
        Assert.Equal(("success", false), (ProcessRun.From(Noon, 400, true, "something else", null).Outcome, false));
    }

    [Theory]
    [InlineData(400, "0.4 s")]
    [InlineData(42_000, "42 s")]
    [InlineData(91_000, "1 min 31 s")]
    [InlineData(120_000, "2 min")]
    [InlineData(7_440_000, "2 h 4 min")]
    public void How_long_a_run_took_is_said_in_seconds_minutes_or_hours(long ms, string words) => Assert.Equal(words, ProcessWords.Took(ms));

    [Fact]
    public void The_strip_s_cell_is_the_day_s_worst_run_and_hollow_where_none_ran()
    {
        Assert.Equal("skip", ProcessWords.Cell(null));
        Assert.Equal("skip", ProcessWords.Cell(ProcessTally.None));
        Assert.Equal("ok", ProcessWords.Cell(new ProcessTally(1, 1, 1, 0, 0, 0, 0, 5)));
        Assert.Equal("shadow", ProcessWords.Cell(new ProcessTally(96, 96, 0, 0, 0, 0, 96, 5)));
        Assert.Equal("late", ProcessWords.Cell(new ProcessTally(48, 48, 46, 2, 0, 0, 0, 5)));
        Assert.Equal("fail", ProcessWords.Cell(new ProcessTally(48, 48, 44, 2, 0, 2, 0, 5)));
    }

    [Fact]
    public void The_schedule_table_holds_the_Function_s_own_cron_strings_and_says_when_each_next_runs()
    {
        string folder = Path.Combine(Repo.Root(), "functions", "jobhunt");
        var crons = Directory.GetFiles(folder, "*.cs")
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), "\\[Function\\(\"(?<name>\\w+)\"\\)\\][\\s\\S]{0,200}?TimerTrigger\\(\"(?<cron>[^\"]+)\"\\)").Select(m => (m.Groups["name"].Value, m.Groups["cron"].Value)))
            .ToDictionary(pair => pair.Item1, pair => pair.Item2);

        Assert.Equal(6, crons.Count);
        Assert.All(FunctionProcesses.All.Where(p => p.Cron is not null), process => Assert.Equal(crons[process.Key], process.Cron));
        Assert.Equal(7, FunctionProcesses.All.Select(p => p.Key).Distinct().Count());

        var at = new DateTimeOffset(2026, 10, 24, 16, 43, 10, TimeSpan.Zero);
        Assert.Equal(at.Date.AddHours(16).AddMinutes(50), FunctionProcesses.Next(FunctionProcesses.Find("AtsSweep")!, at).UtcDateTime);
        Assert.Equal(at.Date.AddHours(16).AddMinutes(44), FunctionProcesses.Next(FunctionProcesses.Find("Merger")!, at).UtcDateTime);
        Assert.Equal(at.Date.AddHours(16).AddMinutes(52), FunctionProcesses.Next(FunctionProcesses.Find("SheetMirror")!, at).UtcDateTime);
        Assert.Equal(at.Date.AddHours(17).AddMinutes(5), FunctionProcesses.Next(FunctionProcesses.Find("DailyCard")!, at).UtcDateTime);
        // 17:00 UTC is noon Central in October: the records run's slot.
        Assert.Equal(at.Date.AddHours(17), FunctionProcesses.Next(FunctionProcesses.Find("Nightly")!, at).UtcDateTime);
        // The campaign acts at 8 AM Central: 13:00 UTC under daylight time, 14:00 UTC from November 1.
        Assert.Equal(at.Date.AddDays(1).AddHours(13), FunctionProcesses.Next(FunctionProcesses.Find("CampaignSend")!, at).UtcDateTime);
        Assert.Equal(new DateTime(2026, 11, 2, 14, 0, 0), FunctionProcesses.Next(FunctionProcesses.Find("CampaignSend")!, new DateTimeOffset(2026, 11, 1, 14, 0, 0, TimeSpan.Zero)).UtcDateTime);
        Assert.Equal(new DateTime(2026, 11, 1, 14, 0, 0), FunctionProcesses.Next(FunctionProcesses.Find("CampaignSend")!, new DateTimeOffset(2026, 11, 1, 13, 30, 0, TimeSpan.Zero)).UtcDateTime);
    }

    [Fact]
    public void The_merger_turns_amber_when_its_tracker_copy_differs_or_fails_and_names_its_switch()
    {
        var switches = new Dictionary<string, string> { ["MERGER"] = "on", ["APPENDS"] = "on", ["TRACKER_SQL"] = "on" };
        ProcessFold Fold(string newest, string? parity) => new(true, null, [], parity is null
            ? new Dictionary<string, ProcessRun> { ["Merger"] = Run(newest) }
            : new Dictionary<string, ProcessRun> { ["Merger"] = Run(newest), [ProcessSections.ParityKey] = Run(parity, 300) });
        string Tone(string newest, string? parity) =>
            ProcessSections.Build(Fold(newest, parity), switches, _ => "", _ => null, Noon).Single(s => s.Key == "Merger").Tone;

        const string quiet = "process:Merger outcome=unchanged took_ms=8 started=x merged=0 tracker_sql_rows=3005 tracker_sql_written=0";
        Assert.Equal("good", Tone(quiet, null));
        Assert.Equal("good", Tone(quiet, quiet + " tracker_sql_parity=1"));
        Assert.Equal("warning", Tone(quiet, quiet + " tracker_sql_parity=0"));
        Assert.Equal("warning", Tone("process:Merger outcome=unchanged took_ms=8 started=x merged=0 tracker_sql_failed=1", quiet + " tracker_sql_parity=1"));

        var merger = ProcessSections.Build(Fold(quiet, null), switches, _ => "", _ => null, Noon).Single(s => s.Key == "Merger");
        Assert.Equal(["MERGER on", "APPENDS on", "TRACKER_SQL on"], merger.Switches.Select(s => $"{s.Name} {s.Value}"));
        Assert.Contains("moved the SQL copy", ProcessWords.CountLine(FunctionProcesses.Find("Merger")!, new ProcessTally(1438, 6, 4, 0, 0, 0, 0, 900), "24 hours"), StringComparison.Ordinal);
    }

    [Fact]
    public void The_seven_sections_come_from_the_fold_the_switches_and_the_summaries()
    {
        var today = DateOnly.FromDateTime(NightlySlots.Local(Noon));
        var fold = new ProcessFold(true, null,
            [new("AtsSweep", today, new ProcessTally(22, 22, 20, 1, 0, 1, 0, 95_000)), new("AtsSweep", today.AddDays(-3), new ProcessTally(48, 48, 48, 0, 0, 0, 0, 90_000))],
            new Dictionary<string, ProcessRun> { ["AtsSweep"] = Run("process:AtsSweep outcome=success took_ms=1 started=x read=42", 40) });
        var switches = new Dictionary<string, string> { ["ATS_SWEEP"] = "on", ["REPLY_ON_HUMAN"] = "shadow", ["SHEET_MIRROR"] = "shadow" };

        var sections = ProcessSections.Build(fold, switches, key => $"{key} does its work.", key => $"https://all/{key}", Noon);

        Assert.Equal(["The inbox pass", "The tracker merger", "The clock", "The Sheet mirror", "The daily card", "The email campaign", "The merger's health"], sections.Select(s => s.Name));
        var inbox = sections[0];
        Assert.Equal(("good", "on", "fine"), (inbox.Tone, inbox.Switches[0].Value, inbox.Switches[0].Tone));
        Assert.Equal(("shadow", "info"), (inbox.Switches.Single(s => s.Name == "REPLY_ON_HUMAN").Value, inbox.Switches.Single(s => s.Name == "REPLY_ON_HUMAN").Tone));
        Assert.Equal(("LEADS", "off", "off"), (inbox.Switches[1].Name, inbox.Switches[1].Value, inbox.Switches[1].Tone));
        Assert.Equal(ProcessSections.StripDays, inbox.Strip.Count);
        Assert.Equal(["ok", "skip", "skip", "fail"], inbox.Strip.TakeLast(4));
        Assert.Equal("70", inbox.StripTotal);
        Assert.Equal(["Last run", "It read and wrote", "30 days", "Next"], inbox.Figures.Select(f => f.Label));
        Assert.Equal("11:20 Central · 1 min 31 s · success", inbox.Figures[0].Text);
        Assert.Equal("68 of 70 on time · 1 late · 1 failed or blocked", inbox.Figures[2].Text);
        Assert.Equal("12:20 Central", inbox.Figures[3].Text);
        Assert.Equal("info", sections[3].Tone);
        Assert.Equal(["EMAIL_CAMPAIGN off"], sections[5].Switches.Select(s => $"{s.Name} {s.Value}"));
        Assert.Equal("off", sections[5].Tone);
        Assert.Equal(["always on"], sections[6].Switches.Select(s => $"{s.Name} {s.Value}"));
        Assert.All(sections, section => Assert.All(section.Figures, figure => Assert.False(string.IsNullOrEmpty(figure.Source))));

        var unread = ProcessSections.Build(ProcessFold.Unread("Application Insights answered 403"), null, _ => "", _ => null, Noon);
        Assert.Equal("Application Insights answered 403", unread[0].Figures[0].Text);
        Assert.Equal("not read", unread[0].Switches[0].Value);
        Assert.All(unread[0].Strip, cell => Assert.Equal("skip", cell));
    }
}
// #endregion process-runs-tests
