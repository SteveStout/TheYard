// The Azure bill behind the three cost cards on the Admin tab (ADR: What Azure charges).
// This file holds CostQuery: the two questions asked of Cost Management and how each answer is
// cut down to days. The rest of the cost reading lives beside it, one job per file:
//   CostReader.cs   - CostOutcome, CostRead, CostReader, CostStatus: one read and how it went
//   CostRecorder.cs - CostRecorder: the background service that reads once an hour
using System.Globalization;
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

    /// <summary>The window both questions cover, from the first day's midnight to the last day's end, UTC.</summary>
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

    /// <summary>
    /// Each column's place in a row, by the column's name in any case, so a row is read by name and
    /// not by position.
    /// </summary>
    private static Dictionary<string, int> ColumnsOf(JsonElement properties) =>
        properties.TryGetProperty("columns", out var columns)
            ? columns.EnumerateArray()
                .Select((column, index) => (Name: column.GetProperty("name").GetString() ?? "", Index: index))
                .GroupBy(column => column.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>The rows of an answer, or none when the answer carries no row array.</summary>
    private static IEnumerable<JsonElement> RowsOf(JsonElement properties) =>
        properties.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array
            ? rows.EnumerateArray()
            : [];

    /// <summary>
    /// The place of the first of <paramref name="names"/> the answer has, or -1 when it has none of
    /// them.
    /// </summary>
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

    /// <summary>
    /// A cost cell as a number; the service sends a number or a numeric string, and anything else
    /// counts as zero.
    /// </summary>
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
