using Microsoft.Extensions.Logging;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// Every process writes one run line a run (ADR-109, the addendum on the six processes): each outcome word
/// renders, a late run says how late, a thrown run comes to failed with its type, the line carries counts and
/// one-word details only (an address or a sentence is dropped), and a timer switched off writes its line once.
/// </summary>
public sealed class RunLineTests
{
    private static readonly DateTimeOffset Slot = new(2026, 10, 24, 15, 20, 0, TimeSpan.Zero);

    [Fact]
    public void Every_outcome_word_renders_with_its_name_its_time_and_its_counts()
    {
        foreach (string outcome in RunLine.Outcomes.Where(o => o != "late"))
        {
            string text = new RunLine("Merger", Slot).Count("merged", 3).Came(outcome).Text(Slot.AddMilliseconds(812));

            Assert.Equal($"process:Merger outcome={outcome} took_ms=812 started=2026-10-24T15:20:00Z merged=3", text);
        }
    }

    [Fact]
    public void A_run_that_starts_past_its_slot_by_more_than_a_minute_came_to_late_with_the_seconds()
    {
        string onTime = new RunLine("AtsSweep", Slot.AddSeconds(20), [20, 50]).Text(Slot.AddSeconds(30));
        string late = new RunLine("AtsSweep", Slot.AddMinutes(3), [20, 50]).Text(Slot.AddMinutes(4));

        Assert.Contains("outcome=success", onTime, StringComparison.Ordinal);
        Assert.Contains("outcome=late took_ms=60000 started=2026-10-24T15:23:00Z detail=180", late, StringComparison.Ordinal);
    }

    [Fact]
    public void A_thrown_run_came_to_failed_with_the_exception_s_type()
    {
        var run = new RunLine("DailyCard", Slot);

        run.Failed(new IOException("the share said no for someone@example.com"));

        Assert.Contains("outcome=failed", run.Text(Slot), StringComparison.Ordinal);
        Assert.Contains("detail=IOException", run.Text(Slot), StringComparison.Ordinal);
        Assert.DoesNotContain("@", run.Text(Slot), StringComparison.Ordinal);
    }

    [Fact]
    public void A_detail_or_a_count_that_could_carry_an_address_or_a_sentence_is_dropped()
    {
        var run = new RunLine("AtsSweep", Slot).Came("blocked", "dan@technologypartners.net").Count("From Dan", 1).Count("waiting", 2);

        string text = run.Text(Slot);

        Assert.DoesNotContain("@", text, StringComparison.Ordinal);
        Assert.DoesNotContain("detail=", text, StringComparison.Ordinal);
        Assert.EndsWith("waiting=2", text, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => run.Came("great"));
    }

    [Fact]
    public void The_inbox_pass_folds_its_parts_into_counts_and_never_their_words()
    {
        var run = AtsSweepTimer.Fold(new RunLine("AtsSweep", Slot), "ON found 5: rows 2, archive 3 | 3 archived and read back, 0 failed",
            "ON alerts 4, requisitions 9, pass 2, blocks unread 0", "0 unjudged, nothing for the model", "1 waiting, shadow: would fire 4 AM reply (waiting: dan@technologypartners.net)", refused: false, acted: true);

        string text = run.Text(Slot);

        Assert.Contains("outcome=success", text, StringComparison.Ordinal);
        Assert.EndsWith("archived=3 leads=9 leads_passed=2 read=5 unjudged=0 waiting=1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("@", text, StringComparison.Ordinal);
        Assert.Contains("outcome=blocked", AtsSweepTimer.Fold(new RunLine("AtsSweep", Slot), "skipped", "skipped", "off", "off", refused: true, acted: true).Text(Slot), StringComparison.Ordinal);
        Assert.Contains("outcome=planned", AtsSweepTimer.Fold(new RunLine("AtsSweep", Slot), "SHADOW found 0: ", "off", "off", "off", refused: false, acted: false).Text(Slot), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_timer_switched_off_writes_its_run_line_once_under_its_name()
    {
        using var scratch = new Scratch();
        var merger = new Lines<Merger>();
        await new Merger(new TrackerMerger(scratch.Store, TimeProvider.System), scratch.Store, new TrackerSeen(), TimeProvider.System, merger).RunAsync(null!, CancellationToken.None);
        var card = new Lines<DailyCardTimer>();
        await new DailyCardTimer(scratch.Store, new NoHttp(), TimeProvider.System, card).RunAsync(null!, CancellationToken.None);
        var mirror = new Lines<SheetMirrorTimer>();
        await new SheetMirrorTimer(scratch.Store, new NoHttp(), TimeProvider.System, mirror).RunAsync(null!, CancellationToken.None);

        foreach (var (lines, name) in new (IReadOnlyList<string>, string)[] { (merger.Written, "Merger"), (card.Written, "DailyCard"), (mirror.Written, "SheetMirror") })
        {
            string line = Assert.Single(lines, l => l.StartsWith(RunLines.Prefix, StringComparison.Ordinal));
            Assert.StartsWith($"process:{name} outcome=unchanged", line, StringComparison.Ordinal);
            Assert.Contains("detail=switch_off", line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_health_route_names_the_switches_by_word_and_never_another_setting_s_value()
    {
        string? before = Environment.GetEnvironmentVariable("NIGHTLY_ANY_HOUR");
        try
        {
            Environment.SetEnvironmentVariable("NIGHTLY_ANY_HOUR", "https://example.com/?sig=secret");
            var switches = MergerWatch.Switches();

            Assert.Equal(MergerWatch.SwitchNames, switches.Keys.OrderBy(k => MergerWatch.SwitchNames.ToList().IndexOf(k)));
            Assert.Equal("set", switches["NIGHTLY_ANY_HOUR"]);
            // The Merger's SQL copy of the tracker has its switch on the card beside MERGER and APPENDS.
            Assert.Equal(MergerWatch.SwitchNames.ToList().IndexOf("APPENDS") + 1, MergerWatch.SwitchNames.ToList().IndexOf("TRACKER_SQL"));
            Assert.All(switches.Values, v => Assert.Matches("^[a-z]{1,12}$", v));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NIGHTLY_ANY_HOUR", before);
        }
    }

    /// <summary>A logger that keeps every line written to it.</summary>
    private sealed class Lines<T> : ILogger<T>
    {
        public List<string> Written { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Written.Add(formatter(state, exception));
    }

    private sealed class NoHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException("no network in this test");
    }
}
