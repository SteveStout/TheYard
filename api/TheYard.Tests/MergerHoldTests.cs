using TheYard.Api.Features.Admin.Cloud;
using TheYard.Api.Features.Docs;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The Function's health route is asked as seldom as its answer changes (ADR-109, the addendum on what the grant pays
/// for): Flex Consumption bills each answer a full second of 512 MB, and on 18 November the site asked it 2,638 times,
/// a fifth of the Function's grant for the day. The merger's numbers are read every five minutes; the switches, which
/// are app settings changed by hand, every fifteen, however often the card's fold is read again.
/// </summary>
public class MergerHoldTests
{
    private static readonly DateTimeOffset Nine = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void The_merger_s_numbers_are_read_every_five_minutes_and_the_switches_every_fifteen()
    {
        Assert.Equal(TimeSpan.FromMinutes(5), MergerReading.Every);
        Assert.Equal(TimeSpan.FromMinutes(15), NightlyProcesses.SwitchesKeep);
        Assert.True(NightlyProcesses.SwitchesKeep > NightlyProcesses.Keep);
    }

    [Fact]
    public async Task The_card_s_fold_is_read_again_after_two_minutes_and_the_health_route_only_after_fifteen()
    {
        var clock = new MovableClock(Nine);
        var runs = new CountingRuns();
        int asked = 0;
        var processes = new NightlyProcesses(runs, _ => { asked++; return Task.FromResult<string?>(null); },
            new ProcessDescriptions(new LiveCodeRegions(Repo.Root(), "local")), "test", clock);
        var card = new CloudReport("24h", ["1h", "24h"], true, null, 0, 30, "The Nightly Function", null, null, [], [], new CloudChart("", [], [], 90, "", 0), [], new CloudReadout("The Function", null, "", [], ""));

        await processes.EnrichAsync(card, CancellationToken.None);
        clock.Now = Nine.AddMinutes(3);
        await processes.EnrichAsync(card, CancellationToken.None);
        clock.Now = Nine.AddMinutes(10);
        await processes.EnrichAsync(card, CancellationToken.None);

        Assert.Equal(3, runs.Folds);
        Assert.Equal(1, asked);

        clock.Now = Nine.AddMinutes(15);
        await processes.EnrichAsync(card, CancellationToken.None);

        Assert.Equal(2, asked);
    }

    /// <summary>
    /// On 19 November, after the roll that read the route every five minutes, the merger row read "not read" for good: the
    /// recorder reads five minutes back, and the minute that carried the reading came back on the next read without it and
    /// was written over. The reading now stays on the minute it landed on until the next ask.
    /// </summary>
    [Fact]
    public void A_reading_stays_on_the_minute_it_landed_on_until_the_next_ask()
    {
        var hold = new MergerHold();
        ResourceMinute At(int minutes) => new(Nine.AddMinutes(minutes), new Dictionary<string, double?> { ["executions"] = 1 }, "test");
        var reading = new Dictionary<string, double?> { [MergerReading.Waiting] = 2, [MergerReading.Over5] = 0, [MergerReading.Over10] = 0, [MergerReading.LogAge] = 30, [MergerReading.Oldest] = 90 };

        Assert.True(hold.Due(Nine));
        var first = hold.Fold([At(-2), At(-1)], reading, Nine, "test");
        Assert.Equal(2d, first[^1].Values[MergerReading.Waiting]);

        var again = hold.Fold([At(-2), At(-1), At(0)], null, Nine.AddMinutes(1), "test");
        Assert.Equal(2d, again.Single(minute => minute.At == Nine.AddMinutes(-1)).Values[MergerReading.Waiting]);
        Assert.Equal(1d, again.Single(minute => minute.At == Nine.AddMinutes(-1)).Values["executions"]);
        Assert.False(again[0].Values.ContainsKey(MergerReading.Waiting));
        Assert.False(again[^1].Values.ContainsKey(MergerReading.Waiting));
        Assert.Equal(again[1], MergerReading.Newest(again));

        Assert.Null(MergerReading.Newest(hold.Fold([At(5), At(6)], null, Nine.AddMinutes(7), "test")));
        Assert.Empty(new MergerHold().Fold([], null, Nine, "test"));
    }

    /// <summary>The workspace, folding nothing and counting the folds.</summary>
    private sealed class CountingRuns : IProcessRunsReader
    {
        public int Folds { get; private set; }

        public Task<ProcessRunsRead> RunsAsync(FunctionProcess process, TimeSpan window, int skip, int take, CancellationToken cancellation) =>
            Task.FromResult(new ProcessRunsRead(true, null, [], ProcessTally.None));

        public Task<ProcessFold> FoldAsync(TimeSpan window, CancellationToken cancellation)
        {
            Folds++;
            return Task.FromResult(ProcessFold.Unread("test"));
        }

        public string? TraceHref(string operationId) => null;

        public string? ProcessHref(string process) => null;
    }

    private sealed class MovableClock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
