// A card's live route, as a query (ADR: Live dials where the source allows): it marks the card leased, so its source
// runs for the next minute, and answers what the ring holds. A card with no live source answers 200 with available
// false and one sentence, never 404, so the page draws "no live source" the way an absent reading reads.
using TheYard.Application.Shared;
using TheYard.Application.Shared.Live;

namespace TheYard.Application.Features.Admin.Cloud;

// #region read-live
/// <summary>A read of one card's live ring.</summary>
/// <param name="Key">The card's key.</param>
public sealed record ReadLiveQuery(string Key);

/// <summary>A card's live ring as the page reads it.</summary>
/// <param name="Available">True when the card has a live source in this process.</param>
/// <param name="Note">Why there is none, or null.</param>
/// <param name="GrainSeconds">How often the source reads.</param>
/// <param name="Source">Where it reads.</param>
/// <param name="AsOfMs">This process's clock, milliseconds since the epoch.</param>
/// <param name="Samples">The last ten minutes, oldest first.</param>
public sealed record LiveReport(bool Available, string? Note, int GrainSeconds, string? Source, long AsOfMs, IReadOnlyList<LiveSample> Samples);

/// <summary>Leases a card's source and answers its ring.</summary>
/// <param name="sources">Every live source this process has.</param>
/// <param name="rings">Their rings.</param>
/// <param name="lease">The lease.</param>
/// <param name="clock">This process's clock.</param>
public sealed class ReadLiveHandler(IEnumerable<ILiveSource> sources, LiveRings rings, LiveLease lease, TimeProvider clock) : IQueryHandler<ReadLiveQuery, LiveReport>
{
    /// <summary>How far back the answer reaches.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    /// <summary>The words a card with no live source says.</summary>
    public const string NoSource = "this card has no live source: it is read at its own cadence, which the card names";

    /// <summary>Leases the card's source for a minute and answers its ring.</summary>
    public Task<LiveReport> HandleAsync(ReadLiveQuery query, CancellationToken cancellation)
    {
        var now = clock.GetUtcNow();
        var source = sources.FirstOrDefault(candidate => candidate.Key == query.Key);
        if (source is null)
        {
            return Task.FromResult(new LiveReport(false, NoSource, 0, null, now.ToUnixTimeMilliseconds(), []));
        }
        if (source.Absent is { } absent)
        {
            return Task.FromResult(new LiveReport(false, absent, source.GrainSeconds, source.Source, now.ToUnixTimeMilliseconds(), []));
        }
        lease.Touch(query.Key, now);
        return Task.FromResult(new LiveReport(true, null, source.GrainSeconds, source.Source, now.ToUnixTimeMilliseconds(), rings.For(query.Key).Last(Window)));
    }
}
// #endregion read-live
