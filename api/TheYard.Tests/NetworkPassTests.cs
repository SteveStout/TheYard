using TheYard.Api.Features.Admin.Cloud;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The network card's logging row reads the newest pass, not the switch alone: a failed pass is alarm with what it
/// came to, a blocked or late one is warning with its words, no pass for a day is warning with the last one's time,
/// a pass that came through is good, an unread history is info, and the switch off is off; the source keeps the
/// newest listed pass as the hour's facts from the run history, and says why when it cannot.
/// </summary>
public sealed class NetworkPassTests
{
    // 09:00 CST on Wednesday 4 November 2026; that morning's 6 AM pass started at 12:00 UTC.
    private static readonly DateTimeOffset Nine = new(2026, 11, 4, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SixAm = new(2026, 11, 4, 12, 0, 1, TimeSpan.Zero);

    private static Dictionary<string, string?> Facts(string? outcome, string? came, DateTimeOffset? at = null, string? note = null, string mode = "shadow")
    {
        var facts = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["switch"] = mode,
            ["weeks"] = "2026-10-26,6;2026-11-02,3",
            [NetworkCard.PassNoteFact] = note,
        };
        if (outcome is not null)
        {
            facts[NetworkCard.PassFact] = outcome;
            facts[NetworkCard.PassCameFact] = came;
            facts[NetworkCard.PassAtFact] = (at ?? SixAm).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        }
        return facts;
    }

    private static CloudRow Logging(Dictionary<string, string?> facts)
    {
        var hour = new ResourceHour(Nine.AddHours(-1), "/subscriptions/s/resourceGroups/g/providers/Microsoft.Sql/servers/s/databases/d", null, facts, []);
        var inputs = new ResourceCardInputs("24h", TimeSpan.FromDays(1), Nine, new PlanHistoryAvailability(true, "kept in the test"), [], [], [], [hour], 0);
        return NetworkCard.View(inputs).Rows.Single(r => r.Key == "logging");
    }

    [Fact]
    public void A_failed_pass_reads_alarm_with_what_it_came_to()
    {
        var row = Logging(Facts("failed", "failed, SqlException"));
        Assert.Equal(("the last pass failed", "alarm"), (row.Answer, row.Tone));
        Assert.Contains(row.Detail, line => line.Text == "the last pass: failed, SqlException, Wed 4 Nov 06:00 Central");
    }

    [Theory]
    [InlineData("blocked", "blocked, no_table")]
    [InlineData("late", "late, 1 min 2 s after its slot")]
    public void A_blocked_or_late_pass_reads_warning_with_its_words(string outcome, string came)
    {
        var row = Logging(Facts(outcome, came));
        Assert.Equal((came, "warning"), (row.Answer, row.Tone));
    }

    [Theory]
    [InlineData("success")]
    [InlineData("unchanged")]
    public void A_pass_that_came_through_reads_good_with_this_week(string outcome)
    {
        var row = Logging(Facts(outcome, outcome));
        Assert.Equal(("3 started this week", "good"), (row.Answer, row.Tone));
    }

    [Fact]
    public void No_pass_for_a_day_reads_warning_with_the_last_one_and_none_in_the_window_says_so()
    {
        var stale = Logging(Facts("success", "success", SixAm.AddHours(-26)));
        Assert.Equal(("no pass since Tue 3 Nov 04:00 Central", "warning"), (stale.Answer, stale.Tone));
        var none = Logging(Facts(null, null));
        Assert.Equal(("no pass in 7 days", "warning"), (none.Answer, none.Tone));
    }

    [Fact]
    public void An_unread_history_reads_info_and_the_switch_off_is_off()
    {
        var unread = Logging(Facts(null, null, note: "not on Azure"));
        Assert.Equal(("3 started this week", "info"), (unread.Answer, unread.Tone));
        Assert.Contains(unread.Detail, line => line.Text == "the last pass was not read: not on Azure");
        var older = Facts(null, null);
        older.Remove(NetworkCard.PassNoteFact);
        Assert.Equal("info", Logging(older).Tone);
        var off = Logging(Facts("failed", "failed, SqlException", mode: "off"));
        Assert.Equal(("Off (off)", "info"), (off.Answer, off.Tone));
    }

    [Fact]
    public async Task The_source_keeps_the_newest_listed_pass_or_why_it_was_not_read()
    {
        var run = new ProcessRun(SixAm, 6300, "failed", "SqlException", new Dictionary<string, long>(), "op", true);
        var facts = await Source(new Runs(new ProcessRunsRead(true, null, [run], ProcessTally.None))).PassAsync(CancellationToken.None);
        Assert.Equal(("failed", "failed, SqlException", null), (facts[NetworkCard.PassFact], facts[NetworkCard.PassCameFact], facts[NetworkCard.PassNoteFact]));
        Assert.Equal(SixAm, DateTimeOffset.Parse(facts[NetworkCard.PassAtFact]!, System.Globalization.CultureInfo.InvariantCulture));
        var empty = await Source(new Runs(new ProcessRunsRead(true, null, [], ProcessTally.None))).PassAsync(CancellationToken.None);
        Assert.False(empty.ContainsKey(NetworkCard.PassFact));
        var unread = await Source(new Runs(new ProcessRunsRead(false, "not on Azure", [], ProcessTally.None))).PassAsync(CancellationToken.None);
        Assert.Equal("not on Azure", unread[NetworkCard.PassNoteFact]);
        Assert.Equal("the Function's run history is not read here", (await Source(null).PassAsync(CancellationToken.None))[NetworkCard.PassNoteFact]);
        var thrown = await Source(new Runs(null)).PassAsync(CancellationToken.None);
        Assert.Equal("the Function's run history failed (InvalidOperationException)", thrown[NetworkCard.PassNoteFact]);
    }

    private static NetworkPasses Source(IProcessRunsReader? runs) => new("/subscriptions/s/resourceGroups/g/providers/Microsoft.Web/sites/func-jobhunt-ss", runs);

    [Fact]
    public async Task The_hour_carries_the_pass_facts_even_when_the_counts_are_not_read()
    {
        var run = new ProcessRun(SixAm, 6300, "success", "shadow", new Dictionary<string, long>(), "op", true);
        var source = new NetworkSource(new NoCounts(), _ => Task.FromResult<string?>(null), _ => Task.FromResult<(string, string?)?>(null), "db", Source(new Runs(new ProcessRunsRead(true, null, [run], ProcessTally.None))));
        var hour = await source.HourAsync(Nine, Nine, CancellationToken.None);
        Assert.Equal("success", hour.Facts[NetworkCard.PassFact]);
        Assert.Equal("/subscriptions/s/resourceGroups/g/providers/Microsoft.Web/sites/func-jobhunt-ss", hour.Facts["function"]);
        var bare = await new NetworkSource(new NoCounts(), _ => Task.FromResult<string?>(null), _ => Task.FromResult<(string, string?)?>(null), "db").HourAsync(Nine, Nine, CancellationToken.None);
        Assert.Equal("the Function's run history is not read here", bare.Facts[NetworkCard.PassNoteFact]);
    }

    private sealed class NoCounts : INetworkCounts
    {
        public Task<NetworkCountsRead> ReadAsync(CancellationToken cancellation) => Task.FromResult(NetworkCountsRead.Unread("no"));
    }

    private sealed class Runs(ProcessRunsRead? read) : IProcessRunsReader
    {
        public Task<ProcessRunsRead> RunsAsync(FunctionProcess process, TimeSpan window, int skip, int take, CancellationToken cancellation) =>
            read is null ? throw new InvalidOperationException("no workspace") : Task.FromResult(read with { Rows = process.Key == NetworkCard.PassProcess ? read.Rows : [] });

        public Task<ProcessFold> FoldAsync(TimeSpan window, CancellationToken cancellation) => Task.FromResult(ProcessFold.Unread("unused"));

        public string? TraceHref(string operationId) => null;

        public string? ProcessHref(string process) => null;
    }
}
