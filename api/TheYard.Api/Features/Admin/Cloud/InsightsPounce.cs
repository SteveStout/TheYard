// Time to first touch (ADR-109, the addendum on time to first touch), read from the Log Analytics workspace the
// Function's Application Insights sends to, by its app name as the cloud role: many run lines a Central day,
// "process:PounceDay outcome=success took_ms=... started=... detail=<the Central day> caught=3 touched=2
// median_min=41 worst_min=75 waiting=1 oldest_wait_min=130", the RunLine shape every process of the Function writes.
using System.Globalization;
using System.Text.RegularExpressions;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Api.Features.Admin.Cloud;

// #region insights-pounce
/// <summary>The PounceDay reader over the workspace's AppTraces.</summary>
/// <param name="azure">Resource Manager, which forwards the query to the workspace.</param>
/// <param name="workspaceId">The workspace's resource id.</param>
/// <param name="role">The Function's cloud role name: its app name.</param>
public sealed partial class InsightsPounce(AzureMetrics azure, string workspaceId, string role) : IPounceReader
{
    /// <summary>The process name the Function writes its day lines under.</summary>
    public const string Process = "PounceDay";

    /// <inheritdoc />
    public string Source => $"the Function's day lines (process:{Process}), from Application Insights";

    /// <inheritdoc />
    public bool Available => azure.Configured;

    /// <summary>The query: every successful day line of the role in the window, oldest first.</summary>
    /// <param name="window">How far back.</param>
    public string Kusto(TimeSpan window) =>
        $"AppTraces | where TimeGenerated > ago({(int)Math.Ceiling(window.TotalHours)}h) and AppRoleName == '{role.Replace("'", "", StringComparison.Ordinal)}'"
        + $" and Message startswith 'process:{Process} outcome=success ' | project At = TimeGenerated, Line = Message | order by At asc";

    /// <inheritdoc />
    public async Task<IReadOnlyList<(DateTimeOffset At, PounceDay Day)>> LinesAsync(TimeSpan window, CancellationToken cancellation)
    {
        var rows = await azure.QueryAsync(workspaceId, Kusto(window), cancellation);
        var lines = new List<(DateTimeOffset, PounceDay)>();
        foreach (var row in rows)
        {
            if (DateTimeOffset.TryParse(row.GetValueOrDefault("At"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                && Parse(row.GetValueOrDefault("Line")) is { } day)
            {
                lines.Add((at, day));
            }
        }
        return lines;
    }

    /// <summary>One line read into its day and six counts; null for a line with no day, which cannot be placed. A count the line leaves out is zero.</summary>
    /// <param name="line">The run line's text.</param>
    public static PounceDay? Parse(string? line)
    {
        if (line is null || DayPattern().Match(line) is not { Success: true } day)
        {
            return null;
        }
        long Count(string name) =>
            Regex.Match(line, $@"(?:^| ){name}=(\d+)(?: |$)", RegexOptions.CultureInvariant) is { Success: true } found
                ? long.Parse(found.Groups[1].Value, CultureInfo.InvariantCulture)
                : 0;
        return new PounceDay(day.Groups[1].Value, Count("caught"), Count("touched"), Count("median_min"), Count("worst_min"), Count("waiting"), Count("oldest_wait_min"));
    }

    [GeneratedRegex(@"(?:^| )detail=(\d{4}-\d{2}-\d{2})(?: |$)", RegexOptions.CultureInvariant)]
    private static partial Regex DayPattern();
}
// #endregion insights-pounce
