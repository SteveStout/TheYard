// What a nightly run may say about itself (ADR: The Nightly Function on the Admin tab). A run
// reaches the card two ways: read from GitHub's own list once a day, or posted by the workflow that
// ran it. The rule of what a run may hold is here, in one method, so the
// route that takes a posted run and the reader that parses GitHub's list cannot disagree about it.
// Pure: the run and the clock go in, the one sentence that is wrong comes out; nothing here reads
// the clock, the disk or the network.
using System.Text.RegularExpressions;
using TheYard.Data;

namespace TheYard.Domain.Features.Admin;

// #region nightly-run-rules
/// <summary>The rules a nightly run passes before it is kept, whether GitHub listed it or the workflow posted it.</summary>
public static partial class NightlyRunRules
{
    /// <summary>How far back a posted run may say it ran: a workflow that could not reach the site posts its last seven days.</summary>
    public static readonly TimeSpan Oldest = TimeSpan.FromDays(7);

    /// <summary>How far ahead of this process's clock a posted run may say it ran: a runner's clock may be a little ahead.</summary>
    public static readonly TimeSpan Skew = TimeSpan.FromMinutes(5);

    /// <summary>The longest a run may say it took: a day, past which the number is not a duration.</summary>
    public const long LongestSeconds = 86_400;

    /// <summary>The one sentence that is wrong with this run, or null when it may be kept.</summary>
    /// <param name="run">The run.</param>
    /// <param name="now">This process's clock.</param>
    public static string? Validate(NightlyRun run, DateTimeOffset now) =>
        string.IsNullOrEmpty(run.Id) || run.Id.Length > 20 || !Digits().IsMatch(run.Id) ? "run is GitHub's own run id, a whole number."
        : run.Number <= 0 ? "number is the run's number in the workflow, one or more."
        : !SlotWords().IsMatch(run.Slot) ? "slot is HH:MM."
        : string.IsNullOrEmpty(run.StartedBy) || run.StartedBy.Length > 30 ? "started_by is 1 to 30 characters."
        : string.IsNullOrEmpty(run.Lane) || run.Lane.Length > 30 || !LaneWords().IsMatch(run.Lane) ? "lane is 1 to 30 lower-case letters or spaces."
        : run.TookSeconds is < 0 ? "took_seconds is zero or more."
        : run.TookSeconds is > LongestSeconds ? "took_seconds is a day or less."
        : string.IsNullOrEmpty(run.CameTo) || run.CameTo.Length > 30 || !OutcomeWords().IsMatch(run.CameTo) ? "came_to is 1 to 30 lower-case letters or underscores."
        : run.At > now + Skew ? "at is in the future."
        : run.At < now - Oldest ? "at is more than seven days ago."
        : null;

    [GeneratedRegex(@"^[0-9]+$")]
    private static partial Regex Digits();

    [GeneratedRegex(@"^([01][0-9]|2[0-3]):[0-5][0-9]$")]
    private static partial Regex SlotWords();

    [GeneratedRegex(@"^[a-z ]+$")]
    private static partial Regex LaneWords();

    [GeneratedRegex(@"^[a-z_]+$")]
    private static partial Regex OutcomeWords();
}
// #endregion nightly-run-rules
