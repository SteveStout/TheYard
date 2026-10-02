// The cost cards' answer as the wire carries it: the report and the three kinds of row it holds
// (a day on the line, a slice of the donut, a bar per type). Plain records with no behaviour
// beyond the empty answer, kept apart from CostReport.cs so the shape reads on its own.
using TheYard.Application;

namespace TheYard.Api;

/// <summary>The three cost cards' answer: the window, the spend and forecast, the resources and the types, or why there are none.</summary>
/// <param name="Window">The window this answer covers: 24h, 7d or 30d.</param>
/// <param name="Windows">Every window the cards offer.</param>
/// <param name="Available">True when there are days to draw.</param>
/// <param name="Note">Why there are no days, or what the last read said when it failed; null when all is well.</param>
/// <param name="Currency">The billing currency.</param>
/// <param name="ReadAtMs">When the recorder last asked Azure, in milliseconds since the epoch, UTC; null before its first read.</param>
/// <param name="NewestDay">The newest day Azure has reported, as yyyy-MM-dd.</param>
/// <param name="NewestPartial">True when the newest day is one Azure is still adding to.</param>
/// <param name="Month">The UTC month the month-to-date figure covers, as yyyy-MM.</param>
/// <param name="MonthToDate">What the month has cost so far.</param>
/// <param name="ForecastMonth">What Azure forecasts the month will cost, or null when it has not said.</param>
/// <param name="WindowTotal">What the window cost.</param>
/// <param name="RateDays">How many finished days the month figures are scaled from.</param>
/// <param name="MonthDays">How many days the month figures are scaled to.</param>
/// <param name="Days">Each day in the window up to the newest reported, with its cost and the running total.</param>
/// <param name="Forecast">Each forecast day after the newest reported, to the month's end, with the running total carried on.</param>
/// <param name="Resources">What each resource cost in the window and in a month at that rate: the top four, then Others.</param>
/// <param name="Types">What each type of resource cost in the window and in a month at that rate, the costliest first, with how many of it are on the bill.</param>
public sealed record CostReport(
    string Window,
    IReadOnlyList<string> Windows,
    bool Available,
    string? Note,
    string Currency,
    long? ReadAtMs,
    string? NewestDay,
    bool NewestPartial,
    string? Month,
    double MonthToDate,
    double? ForecastMonth,
    double WindowTotal,
    int RateDays,
    int MonthDays,
    IReadOnlyList<CostPoint> Days,
    IReadOnlyList<CostPoint> Forecast,
    IReadOnlyList<CostSlice> Resources,
    IReadOnlyList<CostKind> Types)
{
    /// <summary>An answer with nothing to draw and the reason why.</summary>
    public static CostReport Absent(string window, string note, string currency, long? readAtMs) =>
        new(window, CostWindows.Names, false, note, currency, readAtMs, null, false, null, 0, null, 0, 0, 0, [], [], [], []);
}

/// <summary>One day on the spend line.</summary>
/// <param name="Day">The UTC day, as yyyy-MM-dd.</param>
/// <param name="Cost">What the day cost.</param>
/// <param name="Total">The running total from the window's first day through this one.</param>
/// <param name="Partial">True for the newest day while Azure is still adding to it.</param>
public sealed record CostPoint(string Day, double Cost, double Total, bool Partial);

/// <summary>One slice of the donut: a resource, or Others.</summary>
/// <param name="Name">The resource's name, or Others.</param>
/// <param name="Type">The resource's type, or others.</param>
/// <param name="Cost">What it cost in the window.</param>
/// <param name="Monthly">What it comes to in a month at the window's rate.</param>
/// <param name="Share">Its share of the window's cost, in percent.</param>
/// <param name="Count">How many resources the slice stands for: one, or the number folded into Others.</param>
public sealed record CostSlice(string Name, string Type, double Cost, double Monthly, double Share, int Count);

/// <summary>One bar: a resource type, what it cost, and how many of it are on the bill.</summary>
/// <param name="Type">The type as Azure names it, lower case.</param>
/// <param name="Label">The type in the portal's words.</param>
/// <param name="Resources">How many resources of the type appear on the bill in the window.</param>
/// <param name="Cost">What they cost in the window.</param>
/// <param name="Monthly">What they come to in a month at the window's rate.</param>
public sealed record CostKind(string Type, string Label, int Resources, double Cost, double Monthly);
