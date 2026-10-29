// The nightly card's run log (ADR: The Nightly Function on the Admin tab): the workflow's runs kept
// in the run log as the durable copy, the card's cache filled from it, and GitHub read only to
// reconcile. The workflow posts the run it was as it ends (NightlyRunRoutes), so the hour's first
// question is the log's: while every slot the schedule has had due since the newest
// kept run has been answered by a push, the hour costs nothing on GitHub's sixty-an-hour allowance.
// GitHub is read when a slot came due with nothing pushed, and once a day whatever arrived, because
// GitHub's own conclusion for a run can differ from the word the job had when it posted itself. One
// job, apart from the Azure reads in NightlySource, so it is tested with a GitHub answer and a log
// and nothing of Azure.
using TheYard.Data;

namespace TheYard.Api.Features.Admin.Cloud;

// #region nightly-run-reader
/// <summary>Reads the nightly workflow's runs, keeps them, and fills the card's cache.</summary>
/// <param name="runs">The workflow's runs on GitHub.</param>
/// <param name="log">Where the runs are kept, a year, and where the workflow posts them.</param>
/// <param name="cache">The card's copy of the last thirty days of runs.</param>
/// <param name="clock">The clock the window and the reconcile are measured on.</param>
public sealed class NightlyRunReader(GitHubRuns runs, INightlyRunLog log, NightlyRunCache cache, TimeProvider clock)
{
    /// <summary>How long the card goes on the pushed runs alone before GitHub is read to reconcile.</summary>
    public static readonly TimeSpan Reconcile = TimeSpan.FromDays(1);

    /// <summary>
    /// When GitHub was last read through to its end, in UTC ticks; zero until the first read. Read
    /// and written through Volatile because the recorder's hour writes it while a live read may ask
    /// between, the same reason NightlySource's host name is volatile.
    /// </summary>
    private long _reconciledTicks;

    /// <summary>The card's copy of the run log.</summary>
    public NightlyRunCache Cache => cache;

    /// <summary>
    /// True while the runs held are current: the next slot the schedule has after the newest of them
    /// has not come due yet, so nothing the workflow should have posted is missing. No run held at
    /// all is never current, because an empty log says nothing about what ran.
    /// </summary>
    /// <param name="held">The runs the log holds.</param>
    /// <param name="now">The moment judged.</param>
    public static bool Current(IReadOnlyList<NightlyRun> held, DateTimeOffset now) =>
        held.Count > 0 && now < NightlySlots.Next(held.Max(run => run.At)).At;

    /// <summary>
    /// Fills the card's cache for this hour and says in a few words where the runs came from: "N runs
    /// kept" when the log answered on its own, "N runs read" when GitHub was read, and "not read: "
    /// and why when GitHub was asked and would not answer, which leaves the card on the log.
    /// </summary>
    /// <param name="cancellation">Stops the read.</param>
    public async Task<string> ReadAsync(CancellationToken cancellation)
    {
        var now = clock.GetUtcNow();
        bool kept = (await log.AvailabilityAsync(cancellation)).Available;
        IReadOnlyList<NightlyRun> held = kept ? await log.SinceAsync(now - GitHubRuns.Window, cancellation) : [];
        long reconciled = Volatile.Read(ref _reconciledTicks);
        if (kept && Current(held, now) && reconciled != 0 && now - new DateTimeOffset(reconciled, TimeSpan.Zero) < Reconcile)
        {
            // Every slot due since the newest kept run was answered by a push, and GitHub was read
            // inside the day: this hour asks GitHub nothing at all.
            cache.Replace(held, now);
            return $"{held.Count} runs kept";
        }
        IReadOnlyList<NightlyRun> listed;
        try
        {
            listed = await runs.ReadAsync(now, cancellation);
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested)
        {
            // Any failure but the host stopping: a refusal, the client's own timeout, a body that is
            // not the shape asked for. The card falls back to the log and says which. The reconcile
            // is not marked done, so the next hour asks again rather than waiting out the day.
            if (kept)
            {
                cache.Replace(held, now);
            }
            return ex is HttpRequestException { StatusCode: { } code } ? $"not read: GitHub answered {(int)code}"
                : ex is HttpRequestException ? "not read: GitHub did not answer"
                : $"not read: GitHub did not answer ({ex.GetType().Name})";
        }
        Volatile.Write(ref _reconciledTicks, now.UtcTicks);
        if (!kept)
        {
            cache.Replace(listed, now);
            return $"{listed.Count} runs read";
        }
        // GitHub is the authority on a run it lists, so its copy replaces the pushed one: the outcome
        // a job reported at its own last step becomes the conclusion GitHub settled on.
        await log.UpsertAsync(listed, cancellation);
        var fresh = listed.Select(run => run.Id).ToHashSet(StringComparer.Ordinal);
        cache.Replace([.. listed, .. held.Where(run => !fresh.Contains(run.Id))], now);
        return $"{listed.Count} runs read";
    }
}
// #endregion nightly-run-reader
