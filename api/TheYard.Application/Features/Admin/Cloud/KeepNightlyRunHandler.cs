// The Cloud slice's one write (ADR: The Nightly Function on the Admin tab), a handler by rule 7 of
// ADR: The onion cut into vertical slices: the workflow that ran posts the run it was, and the run
// is kept when the Domain's rules admit it. A push is a write from
// outside, so it is a command, and the one sentence that is wrong is given back when the rules
// refuse it. It takes its port and its clock by constructor and is tested with a fake and no host.
using TheYard.Application.Shared;
using TheYard.Application.Shared.History;
using TheYard.Data;
using TheYard.Domain.Features.Admin;

namespace TheYard.Application.Features.Admin.Cloud;

// #region keep-nightly-run
/// <summary>Keeps one nightly run as the workflow that ran it posted it, or says what is wrong with it.</summary>
/// <param name="log">Where the runs are kept, a year, under GitHub's own id.</param>
/// <param name="clock">The clock the run's start is judged against.</param>
public sealed class KeepNightlyRunHandler(INightlyRunLog log, TimeProvider clock) : ICommandHandler<KeepNightlyRunCommand, NightlyRunKept>
{
    /// <summary>
    /// Kept, or why not: the one sentence wrong with the run, or the log's own reason when it cannot
    /// keep anything now. A run is never reported kept when the log could not keep it. Posting the
    /// same run again replaces the copy kept, because the document's id is GitHub's own, so a
    /// workflow may post again after a failure without the card counting the run twice.
    /// </summary>
    /// <param name="command">The run.</param>
    /// <param name="cancellation">Stops the write.</param>
    public async Task<NightlyRunKept> HandleAsync(KeepNightlyRunCommand command, CancellationToken cancellation)
    {
        if (NightlyRunRules.Validate(command.Run, clock.GetUtcNow()) is { } wrong)
        {
            return new NightlyRunKept(wrong, null);
        }
        var availability = await log.AvailabilityAsync(cancellation);
        if (!availability.Available)
        {
            return new NightlyRunKept(null, availability.Reason);
        }
        await log.UpsertAsync([command.Run], cancellation);
        return new NightlyRunKept(null, null);
    }
}

/// <summary>One nightly run, to keep.</summary>
/// <param name="Run">The run.</param>
public sealed record KeepNightlyRunCommand(NightlyRun Run);

/// <summary>What became of a posted nightly run.</summary>
/// <param name="Wrong">The one sentence wrong with it; null when the rules admit it.</param>
/// <param name="Unavailable">Why the log could not keep it now; null when it was kept or was wrong.</param>
public sealed record NightlyRunKept(string? Wrong, string? Unavailable)
{
    /// <summary>True when the run is in the log.</summary>
    public bool Kept => Wrong is null && Unavailable is null;
}
// #endregion keep-nightly-run
