// The box's two writes and its one read (ADR: The box reports itself), each a handler by rule 7 of ADR: The
// onion cut into vertical slices. A post from the box is a write from outside, so it is a command: the Domain's
// rules first, the one sentence that is wrong given back, the port second. The live read is a query over the ring
// with the silence measured on this process's clock. Each takes its port and its clock by constructor and is
// tested with a fake and no host.
using TheYard.Application.Shared;
using TheYard.Data;
using TheYard.Domain.Features.Admin;

namespace TheYard.Application.Features.Admin.Cloud;

// #region box-handlers
/// <summary>Keeps one snapshot the box posted, or says what is wrong with it.</summary>
/// <param name="snapshots">Where the snapshots are held.</param>
/// <param name="clock">The clock the snapshot's time is judged against.</param>
public sealed class KeepBoxSnapshotHandler(IBoxSnapshots snapshots, TimeProvider clock) : ICommandHandler<KeepBoxSnapshotCommand, BoxPosted>
{
    /// <summary>Kept, or the one sentence wrong with it.</summary>
    /// <param name="command">The snapshot.</param>
    /// <param name="cancellation">Unused: holding a snapshot is a write to memory.</param>
    public Task<BoxPosted> HandleAsync(KeepBoxSnapshotCommand command, CancellationToken cancellation)
    {
        if (BoxSnapshotRules.Validate(command.Snapshot, clock.GetUtcNow()) is { } wrong)
        {
            return Task.FromResult(new BoxPosted(wrong));
        }
        snapshots.Accept(command.Snapshot);
        return Task.FromResult(new BoxPosted(null));
    }
}

/// <summary>Adds one live sample the box posted to the ring, or says what is wrong with it.</summary>
/// <param name="live">The ring.</param>
/// <param name="clock">The clock the sample's time is judged against.</param>
public sealed class PushBoxSampleHandler(IBoxLive live, TimeProvider clock) : ICommandHandler<PushBoxSampleCommand, BoxPosted>
{
    /// <summary>Added, or the one sentence wrong with it.</summary>
    /// <param name="command">The sample.</param>
    /// <param name="cancellation">Unused: the ring is in memory.</param>
    public Task<BoxPosted> HandleAsync(PushBoxSampleCommand command, CancellationToken cancellation)
    {
        if (BoxSnapshotRules.Validate(command.Sample, clock.GetUtcNow()) is { } wrong)
        {
            return Task.FromResult(new BoxPosted(wrong));
        }
        live.Push(command.Sample);
        return Task.FromResult(new BoxPosted(null));
    }
}

/// <summary>The live ring as the card's panels read it: the last minute of samples and how long the box has been silent.</summary>
/// <param name="live">The ring.</param>
/// <param name="clock">The clock the silence is measured on.</param>
public sealed class ReadBoxLiveHandler(IBoxLive live, TimeProvider clock) : IQueryHandler<ReadBoxLiveQuery, BoxLiveReport>
{
    /// <summary>How often the box posts a sample.</summary>
    public const int CadenceSeconds = 2;

    /// <summary>The window the panels draw.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    /// <summary>The last minute of samples, oldest first, and the seconds since the newest; null silence before the first.</summary>
    /// <param name="query">The read.</param>
    /// <param name="cancellation">Unused: the ring is in memory.</param>
    public Task<BoxLiveReport> HandleAsync(ReadBoxLiveQuery query, CancellationToken cancellation)
    {
        var now = clock.GetUtcNow();
        long? silent = live.NewestAt is { } newest ? (long)Math.Max(0, (now - newest).TotalSeconds) : null;
        return Task.FromResult(new BoxLiveReport(now.ToUnixTimeMilliseconds(), CadenceSeconds, silent, live.Last(Window)));
    }
}

/// <summary>One snapshot, to keep.</summary>
/// <param name="Snapshot">The snapshot.</param>
public sealed record KeepBoxSnapshotCommand(BoxSnapshot Snapshot);

/// <summary>One live sample, to add to the ring.</summary>
/// <param name="Sample">The sample.</param>
public sealed record PushBoxSampleCommand(BoxLiveSample Sample);

/// <summary>A read of the live ring.</summary>
public sealed record ReadBoxLiveQuery;

/// <summary>What became of a post from the box.</summary>
/// <param name="Wrong">The one sentence wrong with it; null when it was taken.</param>
public sealed record BoxPosted(string? Wrong);

/// <summary>The live ring as the panels read it.</summary>
/// <param name="AsOfMs">This process's clock, milliseconds since the epoch.</param>
/// <param name="CadenceSeconds">How often the box posts a sample.</param>
/// <param name="SilentSeconds">Seconds since the newest sample, or null before the first.</param>
/// <param name="Samples">The last minute of samples, oldest first.</param>
public sealed record BoxLiveReport(long AsOfMs, int CadenceSeconds, long? SilentSeconds, IReadOnlyList<BoxLiveSample> Samples);
// #endregion box-handlers
