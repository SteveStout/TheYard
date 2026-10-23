// The count of people who have visited, all time (ADR-116: People who have visited, counted once): each
// person once, with no time limit, on the first screen and the Admin tab. One query, read by the Admin tab's readouts and by the first screen's pulse,
// so the two can never say different numbers.
using System.Globalization;
using TheYard.Application.Shared;

namespace TheYard.Application.Features.Visitors;

// #region read-people-count
/// <summary>Reads the count from the kept visitors: all time, today, this month, the scanners left out, and the last thirty days.</summary>
/// <param name="visitors">The kept visitors, reading side.</param>
/// <param name="clock">The server's clock; the days are UTC days.</param>
public sealed class ReadPeopleCountHandler(IVisitorReader visitors, TimeProvider clock) : IQueryHandler<ReadPeopleCountQuery, PeopleCount>
{
    /// <summary>How many days the by-day run covers, the first screen's bar run.</summary>
    public const int Days = 30;

    /// <summary>The count as of now.</summary>
    /// <param name="query">The query; it carries nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<PeopleCount> HandleAsync(ReadPeopleCountQuery query, CancellationToken cancellation)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        if (!await visitors.AvailableAsync(cancellation))
        {
            return PeopleCount.Unread(visitors.Source);
        }
        var first = today.AddDays(1 - Days);
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
            visitors.Source);
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
/// <param name="CountedFrom">The first day anything was kept, yyyy-MM-dd, or null before the first.</param>
/// <param name="PeopleNewByDay">People first seen on each of the last thirty UTC days, oldest first.</param>
/// <param name="Source">Where the visitors are kept, or why nothing is.</param>
public sealed record PeopleCount(int? PeopleAllTime, int PeopleNewToday, int PeopleNewMonth, int ScannersLeftOut, string? CountedFrom, IReadOnlyList<PeopleOnDay> PeopleNewByDay, string Source)
{
    /// <summary>No count: the store cannot keep visitors, and the source says why.</summary>
    public static PeopleCount Unread(string why) => new(null, 0, 0, 0, null, [], why);
}

/// <summary>People first seen on one UTC day.</summary>
/// <param name="Day">The day, yyyy-MM-dd.</param>
/// <param name="People">How many people were first seen that day.</param>
public sealed record PeopleOnDay(string Day, int People);
