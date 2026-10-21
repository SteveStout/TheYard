namespace TheYard.Data;

// #region nightly-run
/// <summary>
/// One run of the nightly workflow as GitHub lists it, in the words the Admin tab's nightly card
/// prints: when it started, which slot that was in Central time, who started it, which lane it ran
/// and what it came to. Kept one document a run, under GitHub's own id, so a run read twice is
/// one run (ADR: The Nightly Function on the Admin tab).
/// </summary>
/// <param name="Id">GitHub's id for the run, which is also its document's id.</param>
/// <param name="Number">The run's number in the workflow, as GitHub numbers it.</param>
/// <param name="At">When GitHub created the run, UTC.</param>
/// <param name="Slot">The Central wall-clock time of <paramref name="At"/>, as HH:mm.</param>
/// <param name="StartedBy">"Function dispatch" for a repository_dispatch, "by hand" for a workflow_dispatch.</param>
/// <param name="Dry">True when the run was started by hand as a dry run.</param>
/// <param name="Lane">"reply", "bookkeeping", or "not named" for a run older than the run name.</param>
/// <param name="TookSeconds">How long it ran, start to last update, in seconds; null while it runs.</param>
/// <param name="CameTo">GitHub's conclusion, or its status while the run has none.</param>
public sealed record NightlyRun(string Id, long Number, DateTimeOffset At, string Slot, string StartedBy, bool Dry, string Lane, long? TookSeconds, string CameTo);

/// <summary>One slot of the nightly Function, judged from Azure Monitor's firing counts: due at its hour, then on time, late or missed.</summary>
/// <param name="Slot">The slot's hour, UTC.</param>
/// <param name="Name">"nightly" or "bookkeeping", the run the slot starts.</param>
/// <param name="Outcome">"on time", "late" or "missed".</param>
/// <param name="FiredAt">The start of the first counted firing in the slot's hour; null when it was missed.</param>
public sealed record NightlyFiring(DateTimeOffset Slot, string Name, string Outcome, DateTimeOffset? FiredAt);
// #endregion nightly-run
