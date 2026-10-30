// One read of the bill and what it leaves behind: the days kept, the card's status line, and the
// wait the answer asked for. Its own file because how to read is CostReader.cs, when to read is
// CostRecorder.cs, and what a read leaves behind is neither of those.

namespace TheYard.Api.Shared.CostReadings;

// #region cost-keeping
/// <summary>
/// One read of Cost Management, kept. Thirty-five days each time, the month window and five more,
/// because a day is revised after it ends and the next read is how the revision lands (ADR: What
/// Azure charges). Everything a single read touches is here: whether there is anything to ask
/// with, whether the keep is available, the days written, the card's status line, and the wait a
/// refusal named. What is not here is when to do it again, which is <see cref="CostRecorder"/>.
/// </summary>
/// <param name="reader">The read itself, and whether this process has anything to ask with.</param>
/// <param name="history">The kept days, which a read that went through is written to.</param>
/// <param name="status">The card's status line, set whether the read went through or not.</param>
/// <param name="clock">The clock the status line is stamped with.</param>
/// <param name="logger">Where a read that did not finish is noted, by exception type and never its message.</param>
public sealed class CostKeeping(
    CostReader reader,
    ICostHistory history,
    CostStatus status,
    TimeProvider clock,
    ILogger<CostKeeping> logger)
{
    /// <summary>Whether this process has anything to ask Cost Management with.</summary>
    public bool Configured => reader.Configured;

    /// <summary>The wait the last read asked for, so the loop sleeps as long as the refusal named.</summary>
    public TimeSpan? RetryAfter { get; private set; }

    /// <summary>One read, kept; says how it went.</summary>
    /// <param name="cancellation">The read's cancellation.</param>
    public async Task<CostOutcome> RecordOnceAsync(CancellationToken cancellation)
    {
        var now = clock.GetUtcNow();
        if (!reader.Configured)
        {
            status.Set(now, false, CostReader.NotConfigured);
            return CostOutcome.Failed;
        }

        var availability = await history.AvailabilityAsync(cancellation);
        if (!availability.Available)
        {
            status.Set(now, false, availability.Reason);
            return CostOutcome.Failed;
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var read = await reader.ReadAsync(today.AddDays(-(CostWindows.ReadDays - 1)), today, cancellation);
        if (read.Outcome == CostOutcome.Read)
        {
            await history.KeepAsync(read.Days, read.Forecast, cancellation);
        }

        status.Set(now, read.Outcome == CostOutcome.Read, read.Note);
        RetryAfter = read.RetryAfter;
        return read.Outcome;
    }

    /// <summary>
    /// The same read with its failures caught and written down, which is what a loop wants: an
    /// exception from one read is not a reason to stop reading, and the card says so in a sentence
    /// rather than leaving the last good answer looking current.
    /// </summary>
    /// <param name="cancellation">The read's cancellation, which a cancel is rethrown for.</param>
    public async Task<CostOutcome> TryRecordAsync(CancellationToken cancellation)
    {
        try
        {
            return await RecordOnceAsync(cancellation);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The type, never the message: a message from the identity
            // endpoint or the service names the resource it refused.
            logger.LogWarning("The bill could not be read ({Exception})", ex.GetType().Name);
            status.Set(clock.GetUtcNow(), false, $"the last read did not finish ({ex.GetType().Name}); the next is in five minutes");
            return CostOutcome.Failed;
        }
    }
}
// #endregion cost-keeping
