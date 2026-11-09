// The job hunt's funnel lines (ADR-109, the addendum on the funnel), read from the Log Analytics workspace the
// Function's Application Insights sends to, by its app name as the cloud role: one run line a Central day,
// "process:Funnel outcome=success took_ms=... started=... detail=<the Central day> scanned=212 ready=14 applied=9
// reached=18 replied=2 booked=1", the RunLine shape every process of the Function writes.
using System.Globalization;
using System.Text.RegularExpressions;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Api.Features.Admin.Cloud;

// #region insights-funnel
/// <summary>The funnel reader over the workspace's AppTraces.</summary>
/// <param name="azure">Resource Manager, which forwards the query to the workspace.</param>
/// <param name="workspaceId">The workspace's resource id.</param>
/// <param name="role">The Function's cloud role name: its app name.</param>
public sealed partial class InsightsFunnel(AzureMetrics azure, string workspaceId, string role) : IFunnelReader
{
    /// <summary>The process name the Function writes its funnel line under.</summary>
    public const string Process = "Funnel";

    /// <inheritdoc />
    public string Source => $"the Function's daily funnel line (process:{Process}), from Application Insights";

    /// <inheritdoc />
    public bool Available => azure.Configured;

    /// <summary>The query: every funnel line of the role in the window, oldest first.</summary>
    /// <param name="window">How far back.</param>
    public string Kusto(TimeSpan window) =>
        $"AppTraces | where TimeGenerated > ago({(int)Math.Ceiling(window.TotalHours)}h) and AppRoleName == '{role.Replace("'", "", StringComparison.Ordinal)}'"
        + $" and Message startswith 'process:{Process} ' | project At = TimeGenerated, Line = Message | order by At asc";

    /// <inheritdoc />
    public async Task<IReadOnlyList<(DateTimeOffset At, FunnelDay Day)>> LinesAsync(TimeSpan window, CancellationToken cancellation)
    {
        var rows = await azure.QueryAsync(workspaceId, Kusto(window), cancellation);
        var lines = new List<(DateTimeOffset, FunnelDay)>();
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
    public static FunnelDay? Parse(string? line)
    {
        if (line is null || DayPattern().Match(line) is not { Success: true } day)
        {
            return null;
        }
        long Count(string name) =>
            Regex.Match(line, $@"(?:^| ){name}=(\d+)(?: |$)", RegexOptions.CultureInvariant) is { Success: true } found
                ? long.Parse(found.Groups[1].Value, CultureInfo.InvariantCulture)
                : 0;
        return new FunnelDay(day.Groups[1].Value, Count("scanned"), Count("ready"), Count("applied"), Count("reached"), Count("replied"), Count("booked"));
    }

    [GeneratedRegex(@"(?:^| )detail=(\d{4}-\d{2}-\d{2})(?: |$)", RegexOptions.CultureInvariant)]
    private static partial Regex DayPattern();
}
// #endregion insights-funnel
