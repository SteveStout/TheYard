// The Usage slice's one handler (ADR: Every Claude lane reports its usage), a handler by rule 7 of
// ADR: The onion cut into vertical slices: a lane's usage document is kept when the Domain's rules admit it, and
// the one sentence that is wrong is given back when they do not. It takes its port and its clock by
// constructor and is tested with a fake and no host.
using TheYard.Domain;

namespace TheYard.Application.Features.Usage;

/// <summary>Keeps one run's usage document, or says what is wrong with it.</summary>
/// <param name="log">Where the documents are kept.</param>
/// <param name="clock">The clock the document's start is judged against.</param>
public sealed class KeepUsageHandler(IUsageLog log, TimeProvider clock)
{
    /// <summary>
    /// Kept, or why not: the one sentence wrong with the document, or the log's own reason when it cannot
    /// keep anything now. A document is never reported kept when the log could not keep it.
    /// </summary>
    /// <param name="record">The document.</param>
    /// <param name="cancellation">Stops the write.</param>
    public async Task<UsageKept> HandleAsync(UsageRecord record, CancellationToken cancellation)
    {
        if (record.Validate(clock.GetUtcNow()) is { } wrong)
        {
            return new UsageKept(wrong, null);
        }
        var availability = await log.AvailabilityAsync(cancellation);
        if (!availability.Available)
        {
            return new UsageKept(null, availability.Reason);
        }
        await log.UpsertAsync(record, cancellation);
        return new UsageKept(null, null);
    }
}

/// <summary>What became of a posted usage document.</summary>
/// <param name="Wrong">The one sentence wrong with it; null when the rules admit it.</param>
/// <param name="Unavailable">Why the log could not keep it now; null when it was kept or was wrong.</param>
public sealed record UsageKept(string? Wrong, string? Unavailable)
{
    /// <summary>True when the document is in the log.</summary>
    public bool Kept => Wrong is null && Unavailable is null;
}
