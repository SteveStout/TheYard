// The count of people who have visited, all time (ADR-116: People who have visited, counted once): each
// person once, with no time limit, on the first screen and the Admin tab, beside the visits by people the kept
// activity holds from before the count began (its addendum on the kept visits). One query, read by the Admin
// tab's readouts and by the first screen's pulse, so the two can never say different numbers.
using System.Globalization;
using TheYard.Application.Shared;

namespace TheYard.Application.Features.Visitors;

// #region read-people-count
/// <summary>
/// Reads the count from the kept visitors: all time, today, this month, the scanners left out, and the last thirty
/// days; and the visits by people from the kept activity, since the first day kept.
/// </summary>
/// <param name="visitors">The kept visitors, reading side.</param>
/// <param name="clock">The server's clock; the days are UTC days.</param>
/// <param name="history">The kept visits; none when the host keeps no activity.</param>
public sealed class ReadPeopleCountHandler(IVisitorReader visitors, TimeProvider clock, IVisitHistory? history = null) : IQueryHandler<ReadPeopleCountQuery, PeopleCount>
{
    /// <summary>How many days the by-day run covers, the first screen's bar run.</summary>
    public const int Days = 30;

    /// <summary>The count as of now.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<PeopleCount> HandleAsync(ReadPeopleCountQuery query, CancellationToken cancellation)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var first = today.AddDays(1 - Days);
        var kept = history is null ? null : await history.ReadAsync(cancellation);
        var visits = kept is null
            ? null
            : new KeptVisitsRun(
                kept.Visits,
                kept.From,
                [.. Enumerable.Range(0, Days).Select(offset => first.AddDays(offset)).Select(day => new VisitsOnDay(Day(day), kept.ByDay.GetValueOrDefault(Day(day))))]);
        if (!await visitors.AvailableAsync(cancellation))
        {
            return PeopleCount.Unread(visitors.Source) with { Visits = visits };
        }
        var month = new DateOnly(today.Year, today.Month, 1);
        var since = month < first ? month : first;
        var tally = await visitors.TallyAsync(Day(since), cancellation);
        int On(DateOnly day) => tally.FirstSeenByDay.GetValueOrDefault(Day(day));
        int monthCount = Enumerable.Range(0, today.Day).Sum(offset => On(month.AddDays(offset)));
        return new PeopleCount(
            tally.People,
            On(today),
            monthCount,
            tally.Scanners,
            tally.CountedFrom,
            [.. Enumerable.Range(0, Days).Select(offset => first.AddDays(offset)).Select(day => new PeopleOnDay(Day(day), On(day)))],
            visitors.Source,
            visits);
    }

    private static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

/// <summary>Asks for the count of people who have visited; it carries nothing.</summary>
public sealed record ReadPeopleCountQuery;
// #endregion read-people-count

/// <summary>The count of people who have visited, every figure with the source it was read from.</summary>
/// <param name="PeopleAllTime">People, all time, from the day counting began.</param>
/// <param name="PeopleNewToday">People first seen today (UTC).</param>
/// <param name="PeopleNewMonth">People first seen this calendar month (UTC).</param>
/// <param name="ScannersLeftOut">Visitors that only ever read as scanners or crawlers, left out of every figure above.</param>
/// <param name="CountedFrom">The first day a person was kept, yyyy-MM-dd (a scanner kept earlier does not start the count), or null before the first person.</param>
/// <param name="PeopleNewByDay">People first seen on each of the last thirty UTC days, oldest first.</param>
/// <param name="Source">Where the visitors are kept, or why nothing is.</param>
/// <param name="Visits">The visits by people the kept activity holds, since the first day kept, or null before the first count.</param>
public sealed record PeopleCount(int? PeopleAllTime, int PeopleNewToday, int PeopleNewMonth, int ScannersLeftOut, string? CountedFrom, IReadOnlyList<PeopleOnDay> PeopleNewByDay, string Source, KeptVisitsRun? Visits = null)
{
    /// <summary>No count: the store cannot keep visitors, and the source says why.</summary>
    public static PeopleCount Unread(string why) => new(null, 0, 0, 0, null, [], why);
}

/// <summary>People first seen on one UTC day.</summary>
/// <param name="Day">The day, yyyy-MM-dd.</param>
/// <param name="People">How many people were first seen that day.</param>
public sealed record PeopleOnDay(string Day, int People);

/// <summary>The kept visits as the count answers them: the total, the first day, and the last thirty days.</summary>
/// <param name="AllTime">Visits by people across every kept day, one a person a UTC day.</param>
/// <param name="From">The first UTC day a person's visit was kept, yyyy-MM-dd.</param>
/// <param name="ByDay">Visits by people on each of the last thirty UTC days, oldest first.</param>
public sealed record KeptVisitsRun(int AllTime, string From, IReadOnlyList<VisitsOnDay> ByDay);

/// <summary>Visits by people on one UTC day.</summary>
/// <param name="Day">The day, yyyy-MM-dd.</param>
/// <param name="Visits">How many people visited that day, each once.</param>
public sealed record VisitsOnDay(string Day, int Visits);
