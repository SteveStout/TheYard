// The job hunt's funnel on the Admin tab, as a query (ADR-109, the addendum on the funnel; ADR-115): the Function's
// daily funnel lines in a window, added up, beside the newest seven days' calls booked against two to three a week.
// The lines come through the IFunnelReader port, which Application Insights answers on the host, so the handler is
// tested with a fake reader and no host.
using TheYard.Application.Shared;
using TheYard.Application.Shared.Cards;
using TheYard.Application.Shared.History;
using TheYard.Domain.Features.Admin;
using TheYard.Domain.Shared.Cards;

namespace TheYard.Application.Features.Admin.Cloud;

// #region funnel
/// <summary>Port: the Function's daily funnel lines, read from Application Insights.</summary>
public interface IFunnelReader
{
    /// <summary>Where the lines are read, in words, for the card's source line and for the note when they cannot be.</summary>
    string Source { get; }

    /// <summary>Whether there is anything to read: a workspace and a role to read it for.</summary>
    bool Available { get; }

    /// <summary>Every funnel line written in the window, oldest first, each with when it was written.</summary>
    /// <param name="window">How far back.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    Task<IReadOnlyList<(DateTimeOffset At, FunnelDay Day)>> LinesAsync(TimeSpan window, CancellationToken cancellation);
}

/// <summary>The funnel for a window, and the tone of the newest seven days.</summary>
/// <param name="reader">The lines, reading side.</param>
/// <param name="clock">The server's clock; the window and the line's age are measured back from now.</param>
public sealed class ReadFunnelHandler(IFunnelReader reader, TimeProvider clock) : IQueryHandler<ReadFunnelQuery, FunnelReport>
{
    private static readonly TimeSpan Week = TimeSpan.FromDays(7);

    /// <summary>The window's totals, the week's calls and the card's tone; a reader with nothing to read answers an empty report that says why.</summary>
    /// <param name="query">The window.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<FunnelReport> HandleAsync(ReadFunnelQuery query, CancellationToken cancellation)
    {
        string name = CardWindows.Of(query.Window);
        var (length, _, _) = MachineWindows.Parse(name)!.Value;
        if (!reader.Available)
        {
            return FunnelReport.Unread(name, reader.Source, "the funnel is not read here: " + reader.Source);
        }
        var now = clock.GetUtcNow();
        // The week is always read, so a 24h window still carries the tone the week gives it.
        IReadOnlyList<(DateTimeOffset At, FunnelDay Day)> lines;
        try
        {
            lines = await reader.LinesAsync(length > Week ? length : Week, cancellation);
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested)
        {
            // A workspace that refuses or times out is the card's unread, never the route's 500.
            return FunnelReport.Unread(name, reader.Source, $"the funnel could not be read ({ex.GetType().Name})");
        }
        var inWindow = lines.Where(line => line.At > now - length).Select(line => line.Day).ToList();
        var week = lines.Where(line => line.At > now - Week).Select(line => line.Day).ToList();
        long? age = lines.Count == 0 ? null : (long)(now - lines[^1].At).TotalSeconds;
        var tone = FunnelRules.Grade(week, age);
        if (inWindow.Count == 0 && week.Count == 0)
        {
            return FunnelReport.Unread(name, reader.Source, "the Function has not written a funnel line in the last seven days");
        }
        var total = FunnelRules.Sum(inWindow);
        return new FunnelReport(
            name,
            true,
            null,
            [total.Scanned, total.Ready, total.Applied, total.Reached, total.Replied, total.Booked],
            inWindow.Select(day => day.Day).Distinct(StringComparer.Ordinal).Count(),
            FunnelRules.Sum(week).Booked,
            FunnelRules.CallsAWeek,
            lines.Count == 0 ? null : lines[^1].At.ToUnixTimeMilliseconds(),
            CardTones.Wire(tone),
            reader.Source);
    }
}

/// <summary>The card's funnel for one window.</summary>
/// <param name="Window">24h, 7d or 30d.</param>
public sealed record ReadFunnelQuery(string? Window);

/// <summary>The funnel card's wire: the window's six totals, the week's calls against the target, and the tone.</summary>
/// <param name="Window">24h, 7d or 30d.</param>
/// <param name="Available">False when nothing was read; the note says why.</param>
/// <param name="Note">Why nothing was read, or null.</param>
/// <param name="Counts">Scanned, READY, applied, people reached, replies from people and calls booked, in that order, over the window.</param>
/// <param name="Days">How many Central days in the window have a line.</param>
/// <param name="WeekBooked">Calls booked over the newest seven days.</param>
/// <param name="WeekTarget">The floor of two to three a week.</param>
/// <param name="NewestMs">When the newest line was written, or null.</param>
/// <param name="Tone">fine, amber, red or unread, from the newest seven days.</param>
/// <param name="Source">Where the lines are read.</param>
public sealed record FunnelReport(string Window, bool Available, string? Note, IReadOnlyList<long> Counts, int Days, long WeekBooked, int WeekTarget, long? NewestMs, string Tone, string Source)
{
    /// <summary>A report with nothing read, and why.</summary>
    /// <param name="window">The window asked for.</param>
    /// <param name="source">Where the lines would be read.</param>
    /// <param name="note">Why nothing was read.</param>
    public static FunnelReport Unread(string window, string source, string note) =>
        new(window, false, note, [0, 0, 0, 0, 0, 0], 0, 0, FunnelRules.CallsAWeek, null, CardTones.Wire(CardTone.Unread), source);
}
// #endregion funnel
