// One run of Claude, as a lane reports it (ADR: Every Claude lane reports its usage): which lane,
// billed how, which model, the four token counts, the wall time, the API-equivalent cost, and the
// run it was. The rule of what a document may hold is here, in one method, so the endpoint that
// takes a document and the client that writes one from a response cannot disagree about it.
using System.Globalization;
using System.Text.RegularExpressions;

namespace TheYard.Domain.Shared.Usage;

/// <summary>How a lane's use of Claude is billed.</summary>
public enum UsageKind
{
    /// <summary>Billed to the subscription and measured in the plan's percent.</summary>
    Plan,

    /// <summary>Billed per token and measured in dollars.</summary>
    Api,
}

// #region usage-record
/// <summary>One run of Claude, reported by the lane that ran it.</summary>
/// <param name="Lane">The lane's name as the card shows it: "Azure Functions, nightly lane", "Personal".</param>
/// <param name="Kind">How it is billed.</param>
/// <param name="Model">The model id the run used.</param>
/// <param name="TokensIn">Input tokens not read from the cache.</param>
/// <param name="CacheWrite">Input tokens written to the cache.</param>
/// <param name="CacheRead">Input tokens read from the cache.</param>
/// <param name="TokensOut">Output tokens, thinking included.</param>
/// <param name="WallMs">How long the run took, in milliseconds.</param>
/// <param name="CostUsd">What the run would have cost at API prices; null when the lane does not know.</param>
/// <param name="Run">The run's own id: a GitHub run id, or one the lane makes per day and model.</param>
/// <param name="Slot">The slot it ran in, as HH:MM Central; null when the lane has none.</param>
/// <param name="Mode">What the run did, in a word or two: bookkeeping, reply; "-dry" for a dry run.</param>
/// <param name="At">When the run started, UTC.</param>
public sealed partial record UsageRecord(
    string Lane,
    UsageKind Kind,
    string Model,
    long TokensIn,
    long CacheWrite,
    long CacheRead,
    long TokensOut,
    long WallMs,
    decimal? CostUsd,
    string Run,
    string? Slot,
    string? Mode,
    DateTimeOffset At)
{
    /// <summary>How far back a document may say it ran: a lane that was off for a week posts its last seven days.</summary>
    public static readonly TimeSpan Oldest = TimeSpan.FromDays(7);

    /// <summary>How far ahead of this process's clock a document may say it ran: a lane's clock may be a little ahead.</summary>
    public static readonly TimeSpan Skew = TimeSpan.FromMinutes(5);

    /// <summary>The UTC day the run started, the document's partition.</summary>
    public string Day => At.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>True for a dry run: kept and shown, and left out of every average.</summary>
    public bool Dry => Mode is { } mode && mode.EndsWith("-dry", StringComparison.Ordinal);

    /// <summary>The one sentence that is wrong with this document, or null when it may be kept.</summary>
    /// <param name="now">This process's clock.</param>
    public string? Validate(DateTimeOffset now) =>
        string.IsNullOrEmpty(Lane) || Lane.Length > 40 || !LaneWords().IsMatch(Lane) ? "lane is 1 to 40 letters, digits, spaces, commas or dashes."
        : !Enum.IsDefined(Kind) ? "kind is plan or api."
        : string.IsNullOrEmpty(Model) || Model.Length > 80 || !Token().IsMatch(Model) ? "model is 1 to 80 letters, digits, dots, dashes or underscores."
        : TokensIn < 0 || CacheWrite < 0 || CacheRead < 0 || TokensOut < 0 || WallMs < 0 ? "every count is zero or more."
        : CostUsd is < 0 ? "cost_usd is zero or more."
        : string.IsNullOrEmpty(Run) || Run.Length > 80 || !Token().IsMatch(Run) ? "run is 1 to 80 letters, digits, dots, dashes or underscores."
        : Slot is not null && !SlotWords().IsMatch(Slot) ? "slot is HH:MM."
        : Mode is not null && (Mode.Length is 0 or > 30 || !ModeWords().IsMatch(Mode)) ? "mode is 1 to 30 lower-case letters or dashes."
        : At > now + Skew ? "at is in the future."
        : At < now - Oldest ? "at is more than seven days ago."
        : null;

    [GeneratedRegex(@"^[A-Za-z0-9 ,\-]+$")]
    private static partial Regex LaneWords();

    [GeneratedRegex(@"^[A-Za-z0-9._\-]+$")]
    private static partial Regex Token();

    [GeneratedRegex(@"^([01][0-9]|2[0-3]):[0-5][0-9]$")]
    private static partial Regex SlotWords();

    [GeneratedRegex(@"^[a-z\-]+$")]
    private static partial Regex ModeWords();
}
// #endregion usage-record
