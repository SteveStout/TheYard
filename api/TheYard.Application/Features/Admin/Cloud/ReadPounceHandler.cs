// Time to first touch on the Admin tab, as a query (ADR-109, the addendum on time to first touch; ADR-115): the
// Function's process:PounceDay lines over the last seven Central days, the newest line a day, each day judged
// against a median of 60 minutes. The lines come through the IPounceReader port, which Application Insights
// answers on the host, so the handler is tested with a fake reader and no host. Counts only: no company, no title,
// no person is on a line or on the wire.
using TheYard.Application.Shared;
using TheYard.Domain.Features.Admin;

namespace TheYard.Application.Features.Admin.Cloud;

// #region pounce
/// <summary>Port: the Function's process:PounceDay lines, read from Application Insights.</summary>
public interface IPounceReader
{
    /// <summary>Where the lines are read, in words, for the card's source line and for the note when they cannot be.</summary>
    string Source { get; }

    /// <summary>Whether there is anything to read: a workspace and a role to read it for.</summary>
    bool Available { get; }

    /// <summary>Every PounceDay line written in the window, oldest first, each with when it was written.</summary>
    /// <param name="window">How far back.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    Task<IReadOnlyList<(DateTimeOffset At, PounceDay Day)>> LinesAsync(TimeSpan window, CancellationToken cancellation);
}

/// <summary>The newest seven Central days of time to first touch, each judged.</summary>
/// <param name="reader">The lines, reading side.</param>
/// <param name="clock">The server's clock; the week ends on its Central day.</param>
public sealed class ReadPounceHandler(IPounceReader reader, TimeProvider clock) : IQueryHandler<ReadPounceQuery, PounceReport>
{
    // Eight days back, so the oldest of the seven Central days is covered whatever the hour.
    private static readonly TimeSpan Read = TimeSpan.FromDays(PounceRules.WeekDays + 1);

    /// <summary>Seven days, newest first; a reader with nothing to read answers seven unread days and says why.</summary>
    /// <param name="query">No parameters: the week is always the newest seven days.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<PounceReport> HandleAsync(ReadPounceQuery query, CancellationToken cancellation)
    {
        var now = clock.GetUtcNow();
        if (!reader.Available)
        {
            return PounceReport.Unread(now, reader.Source, "time to first touch is not read here: " + reader.Source);
        }
        IReadOnlyList<(DateTimeOffset At, PounceDay Day)> lines;
        try
        {
            lines = await reader.LinesAsync(Read, cancellation);
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested)
        {
            // A workspace that refuses or times out is the card's unread, never the route's 500.
            return PounceReport.Unread(now, reader.Source, $"time to first touch could not be read ({ex.GetType().Name})");
        }
        var week = PounceRules.Week(lines, now);
        return new PounceReport(
            true,
            week.Any(day => day.Line is not null) ? null : "the Function has not written a PounceDay line in the last seven days",
            [.. week.Select(day => PounceDayRow.Of(day.Day, day.Line))],
            PounceRules.TargetMinutes,
            lines.Count == 0 ? null : lines.Max(line => line.At).ToUnixTimeMilliseconds(),
            now.ToUnixTimeMilliseconds(),
            reader.Source);
    }
}

/// <summary>The card's week; it takes no parameters.</summary>
public sealed record ReadPounceQuery;

/// <summary>One day on the wire: the newest line's six counts, whether a line was read, and its judgement.</summary>
/// <param name="Day">The Central day, yyyy-MM-dd.</param>
/// <param name="Read">False for a day no line names; its counts are zero.</param>
/// <param name="Caught">Roles that passed the code gate and went live that day.</param>
/// <param name="Touched">Of those, the ones a first message has reached.</param>
/// <param name="MedianMin">Median minutes from going live to the first message, over the touched.</param>
/// <param name="WorstMin">The longest of those minutes.</param>
/// <param name="Waiting">The ones no message has reached.</param>
/// <param name="OldestWaitMin">The longest of those waits when the line was written.</param>
/// <param name="Judgement">on_target, late, waiting, quiet, or unread for a day with no line.</param>
public sealed record PounceDayRow(string Day, bool Read, long Caught, long Touched, long MedianMin, long WorstMin, long Waiting, long OldestWaitMin, string Judgement)
{
    /// <summary>A day from its newest line, or the day unread when there is none.</summary>
    /// <param name="day">The Central day.</param>
    /// <param name="line">Its newest line, or null.</param>
    public static PounceDayRow Of(string day, PounceDay? line) => line is null
        ? new(day, false, 0, 0, 0, 0, 0, 0, PounceRules.Judge(null))
        : new(day, true, line.Caught, line.Touched, line.MedianMin, line.WorstMin, line.Waiting, line.OldestWaitMin, PounceRules.Judge(line));
}

/// <summary>The card's wire: seven days newest first, the 60-minute target, and when the lines were read.</summary>
/// <param name="Available">False when nothing could be read; the note says why.</param>
/// <param name="Note">Why nothing was read, or that no day has a line; null when a day has one.</param>
/// <param name="Days">Seven Central days, newest first.</param>
/// <param name="TargetMin">The target, 60 minutes.</param>
/// <param name="NewestMs">When the newest line was written, or null.</param>
/// <param name="ReadMs">When the lines were read.</param>
/// <param name="Source">Where the lines are read.</param>
public sealed record PounceReport(bool Available, string? Note, IReadOnlyList<PounceDayRow> Days, int TargetMin, long? NewestMs, long ReadMs, string Source)
{
    /// <summary>A report with nothing read, seven unread days, and why.</summary>
    /// <param name="now">The moment the week ends.</param>
    /// <param name="source">Where the lines would be read.</param>
    /// <param name="note">Why nothing was read.</param>
    public static PounceReport Unread(DateTimeOffset now, string source, string note) =>
        new(false, note, [.. PounceRules.Week([], now).Select(day => PounceDayRow.Of(day.Day, null))], PounceRules.TargetMinutes, null, now.ToUnixTimeMilliseconds(), source);
}
// #endregion pounce
