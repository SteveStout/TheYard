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
