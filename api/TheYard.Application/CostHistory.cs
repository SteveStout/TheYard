namespace TheYard.Application;

// What Azure charges for this site (ADR: What Azure charges). Azure Cost
// Management answers by the day, a day late, and is rate limited, so a page
// request never asks it: a recorder reads it once an hour and holds the answer
// behind this port, and the Admin tab reads it from there. Azure keeps the
// bill, so the site does not keep a second copy of it: the port holds the last
// read, and a roll costs one read half a minute after the start. The dollar
// figure then sits on the same tab as the millisecond figure, which is the
// point of the cards.

// #region cost-day
/// <summary>
/// One resource's charge for one UTC day, as Cost Management reports it, cut
/// down to what the card needs: the resource's name and type, never its full
/// path, because the path carries the subscription's id and the card is public.
/// </summary>
/// <param name="Day">The UTC day, as yyyy-MM-dd.</param>
/// <param name="Resource">The resource's own name, the last segment of its path, lower case.</param>
/// <param name="Type">The resource's type, provider and type, such as Microsoft.Web/sites, lower case.</param>
/// <param name="Cost">What the day cost, in <paramref name="Currency"/>.</param>
/// <param name="Currency">The billing currency, such as USD.</param>
public sealed record CostDay(string Day, string Resource, string Type, double Cost, string Currency);

/// <summary>
/// One day of Cost Management's forecast answer, the whole subscription at once.
/// The answer carries the month's billed days as well as the days to come, and
/// the two added up are the month's forecast, which is how the portal's own
/// figure is made.
/// </summary>
/// <param name="Day">The UTC day, as yyyy-MM-dd.</param>
/// <param name="Cost">The day's charge, billed or forecast, in <paramref name="Currency"/>.</param>
/// <param name="Currency">The billing currency, such as USD.</param>
/// <param name="Billed">True for a day the forecast counts as already billed; false for a day it forecasts.</param>
public sealed record CostForecastDay(string Day, double Cost, string Currency, bool Billed = false);

/// <summary>The last read: the charges by resource by day, and the forecast for the days still to come.</summary>
/// <param name="Days">One row per resource per day, oldest first.</param>
/// <param name="Forecast">One row per forecast day, oldest first.</param>
public sealed record CostKept(IReadOnlyList<CostDay> Days, IReadOnlyList<CostForecastDay> Forecast)
{
    /// <summary>Nothing held yet.</summary>
    public static readonly CostKept Empty = new([], []);
}

/// <summary>Whether the days can be held and read right now, and if not, why, in words the card can show.</summary>
/// <param name="Available">True when the days can be held and read now.</param>
/// <param name="Reason">Where the days are held, or why they cannot be, in words the card can show.</param>
public sealed record CostHistoryAvailability(bool Available, string Reason);
// #endregion cost-day

// #region cost-port
/// <summary>Port: where the last read of the bill is held, and read back from. Written once an hour, never from a request thread.</summary>
public interface ICostHistory
{
    Task<CostHistoryAvailability> AvailabilityAsync(CancellationToken cancellation);

    /// <summary>Holds a read, replacing the last: a read is thirty-five whole days and a fresh forecast, so the newer read is the whole truth, revisions included.</summary>
    Task KeepAsync(IReadOnlyList<CostDay> days, IReadOnlyList<CostForecastDay> forecast, CancellationToken cancellation);

    /// <summary>Every day held from <paramref name="fromDay"/> on, and every forecast day held, oldest first.</summary>
    Task<CostKept> ReadAsync(string fromDay, CancellationToken cancellation);
}

/// <summary>
/// The last read, held in this process. Azure Cost Management is the record of
/// the bill and keeps it for as long as the subscription exists, so a copy in a
/// database here would be a second source of truth that could only drift from
/// the first. What a roll loses is one hour's read, and the recorder takes it
/// again half a minute after the start.
/// </summary>
public sealed class CostsInMemory : ICostHistory
{
    private CostKept _held = CostKept.Empty;

    public Task<CostHistoryAvailability> AvailabilityAsync(CancellationToken cancellation) =>
        Task.FromResult(new CostHistoryAvailability(true, "held in this site's memory from the last hour's read; Azure keeps the bill itself"));

    public Task KeepAsync(IReadOnlyList<CostDay> days, IReadOnlyList<CostForecastDay> forecast, CancellationToken cancellation)
    {
        // One reference swap, so a reader sees the last read or this one and never half of each.
        Volatile.Write(ref _held, new CostKept(days.ToArray(), forecast.ToArray()));
        return Task.CompletedTask;
    }

    public Task<CostKept> ReadAsync(string fromDay, CancellationToken cancellation)
    {
        var held = Volatile.Read(ref _held);
        return Task.FromResult(new CostKept(
            held.Days.Where(day => string.CompareOrdinal(day.Day, fromDay) >= 0).OrderBy(day => day.Day, StringComparer.Ordinal).ToArray(),
            held.Forecast.OrderBy(day => day.Day, StringComparer.Ordinal).ToArray()));
    }
}
// #endregion cost-port

// #region cost-windows
/// <summary>
/// The windows the cost cards offer, the same three names every kept chart on
/// the Admin tab offers. Azure bills by the day, so a window is a number of
/// days: the last day is the newest two (yesterday whole, today as far as
/// Azure has reported it), a week is seven and a month thirty.
/// </summary>
public static class CostWindows
{
    public static readonly IReadOnlyList<string> Names = ["24h", "7d", "30d"];

    /// <summary>How many days back the recorder reads each hour: the month window and five more, because Azure revises a day after it ends.</summary>
    public const int ReadDays = 35;

    /// <summary>The window by name and how many days it covers; anything else is the month.</summary>
    public static (string Name, int Days) Parse(string? window) => window switch
    {
        "24h" => ("24h", 2),
        "7d" => ("7d", 7),
        _ => ("30d", 30),
    };

    /// <summary>The UTC month a day falls in, as yyyy-MM.</summary>
    public static string MonthOf(string day) => day[..7];
}
// #endregion cost-windows
