// The rules that watch the bill and its meters, read from Azure once an hour beside the bill (ADR: What Azure
// charges, the addendum on the spike guard): the metric and log alerts in the two resource groups, the
// subscription's budgets and its anomaly alert, each by name with whether it is on and its line. Its own file
// because asking Azure for rules is a different read from asking it for costs, which is CostReader.cs; what the
// card says about each meter is MeterRows.cs.
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace TheYard.Api.Shared.CostReadings;

/// <summary>One rule found in Azure: its name, whether it is on, and its line in words.</summary>
/// <param name="Name">The rule's name as Azure holds it.</param>
/// <param name="Enabled">True when the rule is on.</param>
/// <param name="Line">What it watches for, in words: the metric and its threshold, the query's cadence, or the budget's amount.</param>
public sealed record FoundRule(string Name, bool Enabled, string Line);

/// <summary>One read of the rules: what was found by name, the reasons any part could not be read, and when.</summary>
/// <param name="Found">Every rule found, by name.</param>
/// <param name="Notes">Why a part could not be read, one sentence each; empty on a clean read.</param>
/// <param name="At">When the read was made.</param>
public sealed record AlertRulesRead(IReadOnlyDictionary<string, FoundRule> Found, IReadOnlyList<string> Notes, DateTimeOffset At);

/// <summary>
/// Reads the alert rules, budgets and anomaly alert with the site's own identity, the way
/// <see cref="CostReader"/> reads the bill, and holds the last read in memory. Reader on each
/// resource group lists its rules; Cost Management Reader on the subscription lists its budgets.
/// The groups are named by configuration, the site's own and the job hunt's, never in code.
/// </summary>
public sealed class AlertRules(string subscriptionId, string clientId, bool configured, IReadOnlyList<string> groups, HostIdentity? identity = null)
{
    /// <summary>The one client the reads go out on.</summary>
    private static Outbound Http => OutboundHttp.Client(TimeSpan.FromSeconds(30));

    /// <summary>The last read, or null before the first.</summary>
    private volatile AlertRulesRead? _latest;

    /// <summary>The last read, or null before the first; a run off Azure never reads.</summary>
    public AlertRulesRead? Latest => _latest;

    /// <summary>
    /// One read of every rule, held as <see cref="Latest"/>. A part Azure refuses becomes a sentence naming
    /// the role it needs; a read that throws keeps the last good answer, since rules change seldom.
    /// </summary>
    public async Task RefreshAsync(DateTimeOffset now, CancellationToken cancellation)
    {
        if (!configured || string.IsNullOrWhiteSpace(subscriptionId))
        {
            return;
        }

        string token = await IdentityTokens.AcquireAsync(Http, "https://management.azure.com/", clientId, identity ?? HostIdentity.None, cancellation);
        var found = new Dictionary<string, FoundRule>(StringComparer.OrdinalIgnoreCase);
        var notes = new List<string>();
        string sub = $"/subscriptions/{subscriptionId}";
        foreach (string group in groups)
        {
            await ReadIntoAsync(token, $"{sub}/resourceGroups/{group}/providers/Microsoft.Insights/metricAlerts?api-version=2018-03-01", MetricRule, found, notes, $"the alert rules in {group}", "Reader on the group", cancellation);
            await ReadIntoAsync(token, $"{sub}/resourceGroups/{group}/providers/Microsoft.Insights/scheduledQueryRules?api-version=2023-03-15-preview", QueryRule, found, notes, $"the log alert rules in {group}", "Reader on the group", cancellation);
        }

        await ReadIntoAsync(token, $"{sub}/providers/Microsoft.Consumption/budgets?api-version=2023-05-01", BudgetRule, found, notes, "the budgets", "Cost Management Reader on the subscription", cancellation);
        await ReadIntoAsync(token, $"{sub}/providers/Microsoft.CostManagement/scheduledActions?api-version=2023-11-01", AnomalyRule, found, notes, "the anomaly alerts", "Cost Management Reader on the subscription", cancellation);
        _latest = new AlertRulesRead(found, notes, now);
    }

    /// <summary>One list read, each entry turned into a rule by <paramref name="rule"/>; a refusal becomes a note naming the role.</summary>
    private static async Task ReadIntoAsync(string token, string path, Func<JsonElement, FoundRule?> rule, Dictionary<string, FoundRule> found, List<string> notes, string what, string role, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://management.azure.com" + path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        using var response = await Http.SendAsync(request, cancellation);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            notes.Add($"this site's identity cannot list {what}; it needs {role}");
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            notes.Add($"Azure answered {(int)response.StatusCode} to the read of {what}");
            return;
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellation));
        foreach (var entry in Entries(document.RootElement))
        {
            if (rule(entry) is { } read)
            {
                found[read.Name] = read;
            }
        }
    }

    /// <summary>The entries of a list answer: its value array, or nothing.</summary>
    public static IEnumerable<JsonElement> Entries(JsonElement answer) =>
        answer.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

    /// <summary>A metric alert: its first criterion's metric, operator and threshold.</summary>
    public static FoundRule? MetricRule(JsonElement entry)
    {
        if (Text(entry, "name") is not { } name || !entry.TryGetProperty("properties", out var properties))
        {
            return null;
        }

        string line = "a metric alert";
        if (properties.TryGetProperty("criteria", out var criteria) && criteria.TryGetProperty("allOf", out var all) && all.ValueKind == JsonValueKind.Array && all.GetArrayLength() > 0)
        {
            var first = all[0];
            string window = Text(properties, "windowSize") is { } size ? $" in {Duration(size)}" : "";
            line = $"{Text(first, "metricName")} over {Number(first, "threshold")}{window}";
        }

        return new FoundRule(name, Flag(properties, "enabled"), line);
    }

    /// <summary>A log alert: how often it is checked and over what window.</summary>
    public static FoundRule? QueryRule(JsonElement entry)
    {
        if (Text(entry, "name") is not { } name || !entry.TryGetProperty("properties", out var properties))
        {
            return null;
        }

        string line = $"a log alert checked every {Duration(Text(properties, "evaluationFrequency") ?? "")} over {Duration(Text(properties, "windowSize") ?? "")}";
        return new FoundRule(name, Flag(properties, "enabled"), line);
    }

    /// <summary>A budget: its amount and the notices it sends.</summary>
    public static FoundRule? BudgetRule(JsonElement entry)
    {
        if (Text(entry, "name") is not { } name || !entry.TryGetProperty("properties", out var properties))
        {
            return null;
        }

        var notices = new List<string>();
        if (properties.TryGetProperty("notifications", out var notifications) && notifications.ValueKind == JsonValueKind.Object)
        {
            foreach (var notice in notifications.EnumerateObject().OrderBy(entry => entry.Name, StringComparer.Ordinal))
            {
                string kind = Text(notice.Value, "thresholdType") == "Forecasted" ? " of the forecast" : "";
                notices.Add($"{Number(notice.Value, "threshold")} %{kind}");
            }
        }

        string amount = Number(properties, "amount");
        return new FoundRule(name, true, $"${amount} a month, notices at {string.Join(", ", notices)}");
    }

    /// <summary>An anomaly alert: Cost Management's scheduled action of the InsightAlert kind.</summary>
    public static FoundRule? AnomalyRule(JsonElement entry)
    {
        if (Text(entry, "name") is not { } name || Text(entry, "kind") != "InsightAlert" || !entry.TryGetProperty("properties", out var properties))
        {
            return null;
        }

        return new FoundRule(name, Text(properties, "status") == "Enabled", "Cost Management's daily anomaly check, emailed");
    }

    /// <summary>A string property, or null when it is missing or not a string.</summary>
    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>A boolean property, false when missing.</summary>
    private static bool Flag(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;

    /// <summary>A number property in invariant digits, with thousands marked, or a question mark.</summary>
    private static string Number(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble().ToString("#,0.##", CultureInfo.InvariantCulture)
            : "?";

    /// <summary>An ISO duration in words: PT15M as 15 minutes, PT1H as 1 hour, P2D as 2 days.</summary>
    public static string Duration(string iso)
    {
        try
        {
            var span = System.Xml.XmlConvert.ToTimeSpan(iso);
            return span.TotalDays >= 1 && span.TotalDays % 1 == 0 ? Count(span.TotalDays, "day")
                : span.TotalHours >= 1 && span.TotalHours % 1 == 0 ? Count(span.TotalHours, "hour")
                : Count(span.TotalMinutes, "minute");
        }
        catch (FormatException)
        {
            return iso;
        }
    }

    /// <summary>A whole count and its unit, plural past one.</summary>
    private static string Count(double count, string unit) =>
        string.Create(CultureInfo.InvariantCulture, $"{count:0} {unit}{(count == 1 ? "" : "s")}");
}
