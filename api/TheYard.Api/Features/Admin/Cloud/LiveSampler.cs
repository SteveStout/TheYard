// The one loop that runs every card's live source (ADR: Live dials where the source allows): once a second it asks
// each source whose card is leased and whose grain has passed for one sample, and holds it in that card's ring. A
// source nobody is looking at never runs, so the database's view, /proc and the charge line cost nothing while the
// cards are closed.
using TheYard.Application.Shared.Live;

namespace TheYard.Api.Features.Admin.Cloud;

// #region live-sampler
/// <summary>Runs each leased live source at its grain and holds its samples.</summary>
/// <param name="sources">Every live source this process has.</param>
/// <param name="rings">Their rings.</param>
/// <param name="lease">Who is looking.</param>
/// <param name="clock">This process's clock.</param>
/// <param name="logger">Where a read that threw is noted, by exception type only.</param>
public sealed class LiveSampler(IEnumerable<ILiveSource> sources, LiveRings rings, LiveLease lease, TimeProvider clock, ILogger<LiveSampler> logger) : BackgroundService
{
    /// <summary>How often the loop looks.</summary>
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);

    /// <summary>One pass: every leased source whose grain has passed reads once. Public so a test runs one without a host.</summary>
    public async Task<int> TickOnceAsync(CancellationToken cancellation)
    {
        int read = 0;
        var now = clock.GetUtcNow();
        foreach (var source in sources)
        {
            if (source.Absent is not null || !lease.Leased(source.Key, now))
            {
                continue;
            }
            var ring = rings.For(source.Key);
            if (ring.NewestAt is { } newest && now - newest < TimeSpan.FromSeconds(source.GrainSeconds))
            {
                continue;
            }
            try
            {
                if (await source.SampleAsync(now, cancellation) is { } sample)
                {
                    ring.Push(sample);
                    read++;
                }
            }
            catch (Exception ex) when (!cancellation.IsCancellationRequested)
            {
                logger.LogWarning("The live source {Key} did not read ({Exception})", source.Key, ex.GetType().Name);
            }
        }
        return read;
    }

    /// <summary>The loop, a second apart, until the host stops.</summary>
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        while (!stopping.IsCancellationRequested)
        {
            await TickOnceAsync(stopping);
            await Task.Delay(Tick, stopping).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }
}
// #endregion live-sampler
