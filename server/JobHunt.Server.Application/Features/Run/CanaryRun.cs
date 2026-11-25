namespace JobHunt.Server.Application.Features.Run;

/// <summary>
/// The canary (W8 of the year-end plan): before the 05:30 quiet, and on demand after any fix, each machine lane is run
/// dry on one row with a four-minute limit, so a lane that cannot reach its target is found and held before the 06:00
/// run instead of during it. It sends nothing and records nothing: it is a dry run by construction, whatever the local
/// config says. J:\dispatch\canary.py runs one a lane, retries a failure once and holds the lane that fails twice.
/// </summary>
public static class CanaryRun
{
    /// <summary>The most a canary lane may run.</summary>
    public const int Minutes = 4;

    /// <summary>A canary that would still be running at 05:28 does not start (the 05:30 quiet), and the answer says not run.</summary>
    public static readonly TimeSpan Lead = TimeSpan.FromMinutes(2);

    /// <summary>The exit a passed canary answers.</summary>
    public const int Passed = 0;

    /// <summary>The exit a canary answers when its lane did not reach its target.</summary>
    public const int Failed = 3;

    /// <summary>The exit a canary answers when it did not start, which is never a failure.</summary>
    public const int NotRun = 4;

    /// <summary>The settings a canary runs under: dry, one row, and its lane's limit cut to four minutes.</summary>
    /// <param name="config">The program's settings, changed in place before the run is built.</param>
    /// <param name="lane">The canary's lane.</param>
    public static void Fit(ServerConfig config, Lane lane)
    {
        config.DryRun = true;
        config.DryRows = 1;
        LaneConfig own = config.For(lane);
        own.TimeoutMinutes = Math.Min(own.TimeoutMinutes, Minutes);
        config.Lanes[RunSlot.Word(lane)] = own;
    }

    /// <summary>
    /// Why a canary does not start now, or null when it may: from 05:28 to 07:00 Central (the morning's quiet, read with
    /// the canary's lead), so the answer is not run, never failed.
    /// </summary>
    /// <param name="now">The instant.</param>
    /// <param name="lane">The canary's lane.</param>
    public static string? NotStarted(DateTimeOffset now, Lane lane) =>
        RunSlot.Quiet(now, Lead) ? $"{RegistryRows.Lane}: no canary starts from 05:28 to 07:00 Central; the {RunSlot.Word(lane)} canary was not run." : null;

    /// <summary>
    /// The canary's exit: passed when its lane reached its target; not run when the lane had nothing to reach (an empty
    /// worklist, so no model ran, another lane holding LinkedIn or Chrome, or the claude CLI's sign-in expired), which is
    /// never the lane failing; else failed.
    /// </summary>
    /// <param name="reports">The run's lane reports.</param>
    public static int Exit(IReadOnlyList<LaneReport> reports) =>
        Reached(reports) ? Passed
        : reports.Count == 1 && (reports[0] is { Held: null, Rows.Count: 0 } || SignIn.Out(reports[0]) || reports[0].Held?.StartsWith("a hold on LinkedIn or Chrome by", StringComparison.Ordinal) == true) ? NotRun
        : Failed;

    /// <summary>Whether the canary's one lane ran and answered: not held, an answer, not cut short, and no defect line.</summary>
    /// <param name="reports">The run's lane reports.</param>
    public static bool Reached(IReadOnlyList<LaneReport> reports) =>
        reports.Count == 1 && reports[0] is { Held: null, Answer: not null, CutShort: false } report
        && !report.Lines.Any(line => line.StartsWith("DEFECT", StringComparison.Ordinal));
}
