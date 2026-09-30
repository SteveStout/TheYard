using System.Globalization;
using System.Net;
using System.Text.Json;
using TheYard.Application;

namespace TheYard.Api;

// #region cost-query
/// <summary>
/// The two questions this site asks Azure Cost Management, and the shape the
/// answers are cut down to (ADR: What Azure charges). One query answers the
/// line and the donut: the actual cost, a row per resource per day. The second
/// is the forecast to the end of the month. Both go to the subscription, which
/// is where the portal's own overview reads them from.
/// </summary>
public static class CostQuery
{
    /// <summary>Where both questions go. An identifier, not a secret: the identity is the credential.</summary>
    public static string Scope(string subscriptionId) =>
        $"https://management.azure.com/subscriptions/{subscriptionId}/providers/Microsoft.CostManagement";

    /// <summary>The version both calls name; the one the Cost Management documentation leads with.</summary>
    public const string ApiVersion = "2023-11-01";

    /// <summary>The actual cost from <paramref name="from"/> to <paramref name="to"/>, a row per resource per day.</summary>
    public static object Actuals(DateOnly from, DateOnly to) => new
    {
        type = "ActualCost",
        timeframe = "Custom",
        timePeriod = Period(from, to),
        dataset = new
        {
            granularity = "Daily",
            aggregation = new { totalCost = new { name = "Cost", function = "Sum" } },
            grouping = new[] { new { type = "Dimension", name = "ResourceId" } },
        },
    };

    /// <summary>The forecast for the month from <paramref name="from"/> to <paramref name="to"/>, a row per day, the whole subscription at once, with the billed days in it so the rows add up to the month.</summary>
    public static object Forecast(DateOnly from, DateOnly to) => new
    {
        type = "ActualCost",
        timeframe = "Custom",
        timePeriod = Period(from, to),
        dataset = new
        {
            granularity = "Daily",
            aggregation = new { totalCost = new { name = "Cost", function = "Sum" } },
        },
        includeActualCost = true,
        includeFreshPartialCost = false,
    };

    private static object Period(DateOnly from, DateOnly to) => new
    {
        from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T00:00:00Z",
        to = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "T23:59:59Z",
    };
    // #endregion cost-query

    // #region cost-shape
    /// <summary>
    /// A page of actual cost, as the days the store keeps. Cost Management
    /// answers with columns and rows, and the columns are read by name, never
    /// by position, because the order is the service's to change. The resource
    /// path is cut to its name and type here, so the subscription's id never
    /// leaves this method, let alone reaches the wire.
    /// </summary>
    public static IReadOnlyList<CostDay> DaysFrom(JsonElement properties)
    {
        var columns = ColumnsOf(properties);
        int cost = Find(columns, "Cost", "PreTaxCost", "CostUSD");
        int date = Find(columns, "UsageDate");
        int resource = Find(columns, "ResourceId");
        int currency = Find(columns, "Currency", "BillingCurrency");
        if (cost < 0 || date < 0)
        {
            return [];
        }

        var days = new List<CostDay>();
        foreach (var row in RowsOf(properties))
        {
            string? day = DayOf(row[date]);
            if (day is null)
            {
                continue;
            }

            var (name, type) = ResourceOf(resource < 0 ? null : row[resource].GetString());
            days.Add(new CostDay(day, name, type, Number(row[cost]), currency < 0 ? "USD" : row[currency].GetString() ?? "USD"));
        }

        // Two rows for one resource on one day are one charge: a grouped query
        // should not return them, and the cards count each pair once.
        return days
            .GroupBy(day => (day.Day, day.Resource, day.Type, day.Currency))
            .Select(group => new CostDay(group.Key.Day, group.Key.Resource, group.Key.Type, Math.Round(group.Sum(day => day.Cost), 4), group.Key.Currency))
            .OrderBy(day => day.Day, StringComparer.Ordinal)
            .ThenBy(day => day.Resource, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A page of forecast: every day the answer carries, each marked billed or forecast by the service's own status column.</summary>
    public static IReadOnlyList<CostForecastDay> ForecastFrom(JsonElement properties)
    {
        var columns = ColumnsOf(properties);
        int cost = Find(columns, "Cost", "PreTaxCost", "CostUSD");
        int date = Find(columns, "UsageDate");
        int status = Find(columns, "CostStatus");
        int currency = Find(columns, "Currency", "BillingCurrency");
        if (cost < 0 || date < 0)
        {
            return [];
        }

        return RowsOf(properties)
            .Select(row => (
                Day: DayOf(row[date]),
                Cost: Number(row[cost]),
                Currency: currency < 0 ? "USD" : row[currency].GetString() ?? "USD",
                Billed: status >= 0 && string.Equals(row[status].GetString(), "Actual", StringComparison.OrdinalIgnoreCase)))
            .Where(row => row.Day is not null)
            .GroupBy(row => (row.Day!, row.Currency, row.Billed))
            .Select(group => new CostForecastDay(group.Key.Item1, Math.Round(group.Sum(row => row.Cost), 4), group.Key.Currency, group.Key.Billed))
            .OrderBy(day => day.Day, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// A resource path as a name and a type. The type is every type segment
    /// after the last provider, so a database is microsoft.sql/servers/databases
    /// and not a server; the name is the last segment. A charge with no
    /// resource (a tax line, a marketplace line) is kept under a name that says
    /// so rather than dropped, because it is still on the bill.
    /// </summary>
    public static (string Name, string Type) ResourceOf(string? resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return ("not tied to a resource", "other");
        }

        string path = resourceId.Trim().ToLowerInvariant();
        int provider = path.LastIndexOf("/providers/", StringComparison.Ordinal);
        if (provider < 0)
        {
            return (path.TrimEnd('/').Split('/')[^1], "other");
        }

        string[] parts = path[(provider + "/providers/".Length)..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            return (parts.Length == 0 ? "not tied to a resource" : parts[^1], "other");
        }

        // namespace, then type and name in pairs: the types are the odd places.
        var types = new List<string> { parts[0] };
        for (int i = 1; i < parts.Length; i += 2)
        {
            types.Add(parts[i]);
        }

        return (parts[^1], string.Join('/', types));
    }

    private static Dictionary<string, int> ColumnsOf(JsonElement properties) =>
        properties.TryGetProperty("columns", out var columns)
            ? columns.EnumerateArray()
                .Select((column, index) => (Name: column.GetProperty("name").GetString() ?? "", Index: index))
                .GroupBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<JsonElement> RowsOf(JsonElement properties) =>
        properties.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array
            ? rows.EnumerateArray()
            : [];

    private static int Find(Dictionary<string, int> columns, params string[] names)
    {
        foreach (string name in names)
        {
            if (columns.TryGetValue(name, out int index))
            {
                return index;
            }
        }

        return -1;
    }

    private static double Number(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDouble(),
        JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) => parsed,
        _ => 0,
    };

    /// <summary>The service writes a day as the number 20260930 in one answer and a date string in another; both are read.</summary>
    private static string? DayOf(JsonElement value)
    {
        string text = value.ValueKind == JsonValueKind.Number ? value.GetInt64().ToString(CultureInfo.InvariantCulture) : value.GetString() ?? "";
        if (text.Length == 8 && DateOnly.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var compact))
        {
            return compact.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        return text.Length >= 10 && DateOnly.TryParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dashed)
            ? dashed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }
    // #endregion cost-shape
}

// #region cost-read
/// <summary>How a read of Cost Management went: read, refused for want of a role, refused by the billing account, or failed.</summary>
public enum CostOutcome
{
    Read,
    NoRole,
    Refused,
    Failed,
}

/// <summary>One hour's read of Cost Management, or why there was none.</summary>
/// <param name="Outcome">How the read went.</param>
/// <param name="Note">Why there is nothing, in words the card can show; null on a clean read.</param>
/// <param name="Days">The charges by resource by day; empty unless the read went through.</param>
/// <param name="Forecast">The forecast days; empty when the forecast could not be had, which does not spoil the actuals.</param>
public sealed record CostRead(CostOutcome Outcome, string? Note, IReadOnlyList<CostDay> Days, IReadOnlyList<CostForecastDay> Forecast);

/// <summary>
/// Reads Cost Management with the site's own identity, the way the telemetry
/// reader reads Application Insights (ADR-024): a token asked of whichever door
/// this host has, for the management endpoint, and no key anywhere. The
/// identity needs Cost Management Reader on the subscription and nothing more.
/// </summary>
public sealed class CostReader(string subscriptionId, string clientId, bool configured)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>What the card says on a run that is not on Azure.</summary>
    public const string NotConfigured = "the cost reader runs only on Azure, where the site has an identity to ask with; a local run reads nothing";

    /// <summary>What the card says when the identity has no role to read costs with.</summary>
    public const string NoRoleNote = "this site's identity does not hold Cost Management Reader on the subscription yet, so Azure will not tell it what it costs";

    /// <summary>True only on Azure, where an identity exists to ask with.</summary>
    public bool Configured => configured && !string.IsNullOrWhiteSpace(subscriptionId);

    /// <summary>The actuals from <paramref name="from"/> to <paramref name="to"/>, and the forecast for the month <paramref name="to"/> falls in.</summary>
    public async Task<CostRead> ReadAsync(DateOnly from, DateOnly to, CancellationToken cancellation)
    {
        if (!Configured)
        {
            return new CostRead(CostOutcome.Failed, NotConfigured, [], []);
        }

        string token = await IdentityTokens.AcquireAsync(Http, "https://management.azure.com/", clientId);
        var actuals = await PostAsync(token, "query", CostQuery.Actuals(from, to), cancellation);
        if (actuals.Outcome != CostOutcome.Read)
        {
            return new CostRead(actuals.Outcome, actuals.Note, [], []);
        }

        var days = new List<CostDay>();
        foreach (var page in actuals.Pages)
        {
            days.AddRange(CostQuery.DaysFrom(page));
        }

        // The forecast is a second opinion beside the bill: if it fails, the
        // days still go in and the card says only that there is no forecast.
        // It is asked for the whole month, so its billed days and its days to
        // come add up to the figure the portal shows.
        var monthStart = new DateOnly(to.Year, to.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var forecast = new List<CostForecastDay>();
        var ahead = await PostAsync(token, "forecast", CostQuery.Forecast(monthStart, monthEnd), cancellation);
        if (ahead.Outcome == CostOutcome.Read)
        {
            foreach (var page in ahead.Pages)
            {
                forecast.AddRange(CostQuery.ForecastFrom(page));
            }
        }

        return new CostRead(CostOutcome.Read, null, days, forecast);
    }

    private async Task<(CostOutcome Outcome, string? Note, List<JsonElement> Pages)> PostAsync(string token, string action, object body, CancellationToken cancellation)
    {
        var pages = new List<JsonElement>();
        string? address = $"{CostQuery.Scope(subscriptionId)}/{action}?api-version={CostQuery.ApiVersion}";
        // A month of daily rows by resource is a few hundred rows, one page;
        // the cap is there so a service that keeps answering with a next page
        // cannot hold the recorder.
        for (int page = 0; address is not null && page < 5; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, address) { Content = JsonContent.Create(body) };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var response = await Http.SendAsync(request, cancellation);
            if (!response.IsSuccessStatusCode)
            {
                return (OutcomeOf(response.StatusCode), NoteFor(response.StatusCode), pages);
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
            var properties = document.RootElement.GetProperty("properties");
            pages.Add(properties.Clone());
            address = properties.TryGetProperty("nextLink", out var next) && next.ValueKind == JsonValueKind.String ? next.GetString() : null;
        }

        return (CostOutcome.Read, null, pages);
    }

    /// <summary>Which of the four a status is.</summary>
    public static CostOutcome OutcomeOf(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => CostOutcome.NoRole,
        HttpStatusCode.BadRequest or HttpStatusCode.Conflict or HttpStatusCode.TooManyRequests => CostOutcome.Refused,
        _ => CostOutcome.Failed,
    };

    /// <summary>
    /// What to say on a public card about a read that did not happen. The
    /// service's own words are not printed: an error from Cost Management names
    /// the scope it refused, and the scope is the subscription's path.
    /// </summary>
    public static string NoteFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => NoRoleNote,
        HttpStatusCode.TooManyRequests => "Cost Management asked this site to slow down; the next read is in an hour",
        HttpStatusCode.BadRequest or HttpStatusCode.Conflict => "Cost Management refused the read, which it does while a billing account is being set up; the next read is in an hour",
        _ => $"Cost Management answered {(int)status}; the next read is in an hour",
    };
}

/// <summary>What the recorder last found, which is how the card tells "no role" from "nothing kept yet".</summary>
public sealed class CostStatus
{
    private readonly object _gate = new();

    /// <summary>When the last read was tried, or null before the first.</summary>
    public DateTimeOffset? At { get; private set; }

    /// <summary>True when the last read went through.</summary>
    public bool Read { get; private set; }

    /// <summary>Why the last read did not go through, or null when it did.</summary>
    public string? Note { get; private set; }

    public void Set(DateTimeOffset at, bool read, string? note)
    {
        lock (_gate)
        {
            At = at;
            Read = read;
            Note = note;
        }
    }

    /// <summary>The three at once, so a reader never sees one read's time beside another's note.</summary>
    public (DateTimeOffset? At, bool Read, string? Note) Snapshot()
    {
        lock (_gate)
        {
            return (At, Read, Note);
        }
    }
}
// #endregion cost-read

// #region cost-recorder
/// <summary>
/// Reads Cost Management once an hour and holds the answer (ADR: What Azure
/// charges). Never on a request: the service is rate limited, lags the day by
/// eight to twenty four hours, and a card that asked it on every open would
/// spend the allowance on the same answer. Thirty-five days each time, the
/// month window and five more, because a day is revised after it ends and the
/// next read is how the revision lands.
/// </summary>
public sealed class CostRecorder(
    CostReader reader,
    ICostHistory history,
    CostStatus status,
    TimeProvider clock,
    ILogger<CostRecorder> logger) : BackgroundService
{
    /// <summary>How often the bill is read.</summary>
    public static readonly TimeSpan Every = TimeSpan.FromHours(1);

    /// <summary>One read, public so the suite can run one without waiting an hour.</summary>
    public async Task RecordOnceAsync(CancellationToken cancellation)
    {
        var now = clock.GetUtcNow();
        if (!reader.Configured)
        {
            status.Set(now, false, CostReader.NotConfigured);
            return;
        }

        var availability = await history.AvailabilityAsync(cancellation);
        if (!availability.Available)
        {
            status.Set(now, false, availability.Reason);
            return;
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var read = await reader.ReadAsync(today.AddDays(-(CostWindows.ReadDays - 1)), today, cancellation);
        if (read.Outcome == CostOutcome.Read)
        {
            await history.KeepAsync(read.Days, read.Forecast, cancellation);
        }

        status.Set(now, read.Outcome == CostOutcome.Read, read.Note);
    }

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        // Half a minute after the start, so the first read never competes
        // with the catalogue this process loads before it answers anybody.
        await Task.Delay(TimeSpan.FromSeconds(30), stopping).ContinueWith(_ => { }, TaskScheduler.Default);
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                await RecordOnceAsync(stopping);
                if (!reader.Configured)
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The type, never the message: a message from the identity
                // endpoint or the service names the resource it refused.
                logger.LogWarning("The bill could not be read ({Exception})", ex.GetType().Name);
                status.Set(clock.GetUtcNow(), false, $"the last read did not finish ({ex.GetType().Name}); the next is in an hour");
            }

            await Task.Delay(Every, stopping).ContinueWith(_ => { }, TaskScheduler.Default);
        }
    }
}
// #endregion cost-recorder
