using System.Globalization;
using TheYard.Application;

namespace TheYard.Api;

// #region cost-view
/// <summary>
/// The last read, shaped for the three cost cards (ADR: What Azure charges):
/// the spend over the window with the forecast to the month's end, and what
/// each resource and each type of resource cost, in the window and as a month
/// at the window's rate. Pure, so a fixed set of days in gives a fixed answer
/// out and a test can hold it.
/// </summary>
public static class CostView
{
    /// <summary>How many resources the donut names before it folds the rest into Others.</summary>
    public const int Named = 4;

    public static CostReport Build(string? window, CostKept kept, DateTimeOffset now, CostHistoryAvailability availability, (DateTimeOffset? At, bool Read, string? Note) last)
    {
        var (name, length) = CostWindows.Parse(window);
        string today = CostWindows.DayOf(now);
        string first = CostWindows.DayOf(now.AddDays(-(length - 1)));
        string month = CostWindows.MonthOf(today);
        string currency = kept.Days.FirstOrDefault()?.Currency ?? kept.Forecast.FirstOrDefault()?.Currency ?? "USD";
        long? readAt = last.At?.ToUnixTimeMilliseconds();

        if (!availability.Available || kept.Days.Count == 0)
        {
            // Absent is said in a sentence, never drawn as a zero: a card that
            // showed $0.00 for "no role yet" would be a false statement about the bill.
            string note = !availability.Available
                ? availability.Reason
                : last.Note ?? (last.At is null ? "the first hour's read has not landed yet; the recorder reads Azure half a minute after the site starts" : "Azure reported no charges for this subscription yet");
            return CostReport.Absent(name, note, currency, readAt);
        }

        string newest = kept.Days.Max(day => day.Day)!;
        // Azure lags the day by eight to twenty four hours, so the newest day
        // it has reported is still being added to until it is two days old.
        bool partial = string.CompareOrdinal(newest, CostWindows.DayOf(now.AddDays(-1))) >= 0;

        var inWindow = kept.Days.Where(day => string.CompareOrdinal(day.Day, first) >= 0 && string.CompareOrdinal(day.Day, today) <= 0).ToList();
        var byDay = inWindow.GroupBy(day => day.Day).ToDictionary(group => group.Key, group => group.Sum(day => day.Cost), StringComparer.Ordinal);

        // Every day from the window's first to the newest Azure has reported:
        // a day before the newest with no rows cost nothing, and a day after
        // it has not been reported, so it is left off rather than drawn as zero.
        var points = new List<CostPoint>();
        double running = 0;
        for (var day = DateOnly.ParseExact(first, "yyyy-MM-dd", CultureInfo.InvariantCulture); ; day = day.AddDays(1))
        {
            string key = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (string.CompareOrdinal(key, newest) > 0 || string.CompareOrdinal(key, today) > 0)
            {
                break;
            }

            double cost = byDay.GetValueOrDefault(key);
            running += cost;
            points.Add(new CostPoint(key, Money(cost), Money(running), partial && key == newest));
        }

        double monthToDate = kept.Days.Where(day => CostWindows.MonthOf(day.Day) == month).Sum(day => day.Cost);
        // The month's forecast is the forecast answer added up, billed days and
        // days to come, which is the portal's own figure. The dashed line is
        // only the days to come after the newest day the bill has reported.
        var answer = kept.Forecast.Where(day => CostWindows.MonthOf(day.Day) == month).ToList();
        var ahead = answer
            .Where(day => !day.Billed && string.CompareOrdinal(day.Day, newest) > 0)
            .OrderBy(day => day.Day, StringComparer.Ordinal)
            .ToList();
        var forecast = new List<CostPoint>();
        double projected = running;
        foreach (var day in ahead)
        {
            projected += day.Cost;
            forecast.Add(new CostPoint(day.Day, Money(day.Cost), Money(projected), false));
        }

        double? forecastMonth = answer.Count > 0 ? Money(answer.Sum(day => day.Cost)) : null;
        var rate = MonthRate.For(points, today);

        return new CostReport(
            name,
            CostWindows.Names,
            true,
            last.Read ? null : last.Note,
            currency,
            readAt,
            newest,
            partial,
            month,
            Money(monthToDate),
            forecastMonth,
            Money(inWindow.Sum(day => day.Cost)),
            rate.BilledDays,
            rate.MonthDays,
            points,
            forecast,
            Slices(inWindow, rate),
            Kinds(inWindow, rate));
    }

    /// <summary>
    /// What each resource cost in the window and what that comes to in a month,
    /// largest first, the top four by name and the rest as Others.
    /// </summary>
    public static IReadOnlyList<CostSlice> Slices(IReadOnlyList<CostDay> days, MonthRate rate)
    {
        double total = days.Sum(day => day.Cost);
        var ranked = days
            .GroupBy(day => (day.Resource, day.Type))
            .Select(group => (group.Key.Resource, group.Key.Type, Cost: group.Sum(day => day.Cost), Monthly: rate.Of(group)))
            .Where(entry => entry.Cost > 0)
            .OrderByDescending(entry => entry.Cost)
            .ThenBy(entry => entry.Resource, StringComparer.Ordinal)
            .ToList();

        var slices = ranked.Take(Named)
            .Select(entry => new CostSlice(entry.Resource, entry.Type, Money(entry.Cost), Money(entry.Monthly), Share(entry.Cost, total), 1))
            .ToList();
        var rest = ranked.Skip(Named).ToList();
        if (rest.Count > 0)
        {
            double others = rest.Sum(entry => entry.Cost);
            double othersMonthly = rest.Sum(entry => entry.Monthly);
            slices.Add(new CostSlice("Others", "others", Money(others), Money(othersMonthly), Share(others, total), rest.Count));
        }

        return slices;
    }

    /// <summary>
    /// What each type of resource cost in the window, what that comes to in a
    /// month, and how many of it are on the bill, the costliest first. A type the
    /// bill carries at $0.00, such as App Service apps whose plan pays for them,
    /// goes last, so the longest bar is where the money went.
    /// </summary>
    public static IReadOnlyList<CostKind> Kinds(IReadOnlyList<CostDay> days, MonthRate rate) =>
        days
            .GroupBy(day => day.Type)
            .Select(group => new CostKind(
                group.Key,
                LabelOf(group.Key),
                group.Select(day => day.Resource).Distinct(StringComparer.Ordinal).Count(),
                Money(group.Sum(day => day.Cost)),
                Money(rate.Of(group))))
            .OrderByDescending(kind => kind.Cost)
            .ThenByDescending(kind => kind.Resources)
            .ThenBy(kind => kind.Label, StringComparer.Ordinal)
            .ToList();

    /// <summary>A resource type in the words the portal uses for it; a type this list does not know is shown as Azure names it.</summary>
    public static string LabelOf(string type) => type switch
    {
        "microsoft.web/sites" => "App Service apps",
        "microsoft.web/serverfarms" => "App Service plans",
        "microsoft.containerinstance/containergroups" => "Container instances",
        "microsoft.containerregistry/registries" => "Container registries",
        "microsoft.sql/servers/databases" => "SQL databases",
        "microsoft.sql/servers" => "SQL servers",
        "microsoft.documentdb/databaseaccounts" => "Azure Cosmos DB accounts",
        "microsoft.insights/components" => "Application Insights",
        "microsoft.operationalinsights/workspaces" => "Log Analytics workspaces",
        "microsoft.communication/communicationservices" => "Communication Services",
        "microsoft.communication/emailservices" => "Email Communication Services",
        "microsoft.storage/storageaccounts" => "Storage accounts",
        "microsoft.managedidentity/userassignedidentities" => "Managed identities",
        "other" => "Not tied to a resource",
        _ => type,
    };

    /// <summary>Money to the cent, which is what the bill shows.</summary>
    public static double Money(double value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static double Share(double part, double total) => total <= 0 ? 0 : Math.Round(part / total * 100, 1);
}

/// <summary>
/// The last read, served to the cards, each window cached for a minute: the
/// Admin tab is a page somebody leaves open, and the read under it changes
/// once an hour (ADR: What Azure charges).
/// </summary>
public sealed class CostHistoryReader(ICostHistory history, CostReader reader, CostStatus status, TimeProvider clock)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (DateTimeOffset At, CostReport View)> _cached = new(StringComparer.Ordinal);

    public async Task<CostReport> ReadAsync(string? window, CancellationToken cancellation)
    {
        var now = clock.GetUtcNow();
        string name = CostWindows.Parse(window).Name;
        lock (_gate)
        {
            if (_cached.TryGetValue(name, out var hit) && now - hit.At < TimeSpan.FromMinutes(1))
            {
                return hit.View;
            }
        }

        CostReport view;
        if (!reader.Configured)
        {
            view = CostReport.Absent(name, CostReader.NotConfigured, "USD", null);
        }
        else
        {
            var availability = await history.AvailabilityAsync(cancellation);
            var kept = availability.Available
                ? await history.ReadAsync(CostWindows.DayOf(now.AddDays(-(CostWindows.ReadDays - 1))), cancellation)
                : CostKept.Empty;
            view = CostView.Build(name, kept, now, availability, status.Snapshot());
        }

        lock (_gate)
        {
            _cached[name] = (now, view);
        }

        return view;
    }
}
// #endregion cost-view

// #region cost-rate
/// <summary>
/// How a window's cost becomes a month's: what the finished days cost, divided by
/// how many there are, times the days in the month. The newest day is left out
/// while Azure is still adding to it, because a part of a day counted as a whole
/// one would pull the month down. A window with no finished day uses the days it
/// has, so the card still shows a figure rather than nothing.
/// </summary>
/// <param name="PartialDay">The day left out, as yyyy-MM-dd, or null when every day counts.</param>
/// <param name="BilledDays">How many days the month figure is scaled from.</param>
/// <param name="MonthDays">How many days the month has.</param>
public sealed record MonthRate(string? PartialDay, int BilledDays, int MonthDays)
{
    /// <summary>The rate for a window's days, in the month that holds today.</summary>
    public static MonthRate For(IReadOnlyList<CostPoint> points, string today)
    {
        var day = DateOnly.ParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        int monthDays = DateTime.DaysInMonth(day.Year, day.Month);
        int finished = points.Count(point => !point.Partial);
        string? partial = points.FirstOrDefault(point => point.Partial)?.Day;
        return finished > 0
            ? new MonthRate(partial, finished, monthDays)
            : new MonthRate(null, points.Count, monthDays);
    }

    /// <summary>What these days come to in a month at this rate.</summary>
    public double Of(IEnumerable<CostDay> days) =>
        BilledDays == 0 ? 0 : days.Where(day => day.Day != PartialDay).Sum(day => day.Cost) / BilledDays * MonthDays;
}
// #endregion cost-rate

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
