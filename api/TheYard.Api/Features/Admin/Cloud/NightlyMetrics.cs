// The Azure Monitor metrics the nightly card keeps, by Azure's names on Microsoft.Web/sites as
// the Flex Consumption cost page lists them (learn.microsoft.com/azure/azure-functions/functions-consumption-costs):
// the on-demand executions, and the on-demand execution units in megabyte-milliseconds, which is
// what the free grant is counted in. A gigabyte-second is 1,024,000 of them.
namespace TheYard.Api.Features.Admin.Cloud;

/// <summary>The nightly card's metrics.</summary>
public static class NightlyMetrics
{
    /// <summary>Azure's names: the Function's on-demand executions, and its on-demand execution units.</summary>
    public const string Executions = "OnDemandFunctionExecutionCount", Units = "OnDemandFunctionExecutionUnits";

    /// <summary>Megabyte-milliseconds in one gigabyte-second, as the cost page converts them.</summary>
    public const double MbMsPerGbSecond = 1_024_000;

    public static readonly IReadOnlyList<AzureMetric> All =
    [
        new(Executions, "total", "executions"),
        new(Units, "total", "units_mbms"),
    ];

    /// <summary>The names the card keeps the two under.</summary>
    public static IReadOnlyList<string> Readings => [.. All.Select(m => m.Reading)];
}
