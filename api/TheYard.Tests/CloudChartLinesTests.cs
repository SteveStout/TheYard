using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// How a resource card's chart is sent for drawing (ADR: One frame for every resource, the addendum on how a card
/// draws time): each series carries the amber and red lines its dial reads, so a chart of two series is two panels
/// each against its own lines; each point carries its bucket's peak, so an hour's average still shows the spike
/// inside it; and a bucket's tally of throttled requests is a counted tick, not a full-height line.
/// </summary>
public sealed class CloudChartLinesTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A week's inputs with one hourly bucket whose mean and peak differ.</summary>
    private static ResourceCardInputs Week(string reading, double mean, double peak, IReadOnlyDictionary<string, string?> facts, IReadOnlyDictionary<string, double?>? sums = null)
    {
        var bucket = new ResourceBucket(Now.AddHours(-2), 60, new Dictionary<string, double?> { [reading] = mean }, new Dictionary<string, double?> { [reading] = peak }, sums ?? new Dictionary<string, double?>(), "1.0.4.95");
        var minutes = new List<ResourceMinute> { new(Now.AddMinutes(-1), new Dictionary<string, double?> { [reading] = mean }, "1.0.4.95") };
        var hours = new List<ResourceHour> { new(Now.AddHours(-1), "/subscriptions/s/resourceGroups/RG-THEYARD-SS/providers/T/x", "tenant", facts, []) };
        return new ResourceCardInputs("7d", TimeSpan.FromDays(7), Now, new PlanHistoryAvailability(true, "kept in Cosmos DB, 31 days"), minutes, [bucket], [bucket], hours, 1.23);
    }

    [Fact]
    public void The_cosmos_chart_draws_each_series_against_its_dials_lines_with_the_hours_peak()
    {
        var view = CosmosCard.View(Week("ru_per_s", 100, 700, new Dictionary<string, string?> { ["throughput"] = "1000", ["free_tier"] = "on" }, new Dictionary<string, double?> { ["throttled"] = 4, ["requests"] = 100 }));
        var (ru, storage) = (view.Chart.Series[0], view.Chart.Series[1]);

        Assert.Equal((view.Dials[0].Amber, view.Dials[0].Red), (ru.Amber!.Value, ru.Red!.Value));
        Assert.Equal((view.Dials[1].Amber, view.Dials[1].Red), (storage.Amber!.Value, storage.Red!.Value));
        Assert.Equal(10d, ru.Points[0].Value!.Value, 6);
        Assert.Equal(70d, ru.Points[0].Peak!.Value, 6);
        Assert.Single(view.Chart.Marks, mark => mark.Kind == "count");
    }

    [Fact]
    public void Every_card_in_the_frame_gives_its_lines_to_the_series_its_old_red_line_belonged_to()
    {
        var sql = SqlCard.View(Week("dtu", 30, 95, new Dictionary<string, string?> { ["dtu"] = "5", ["max_gb"] = "2" }));
        Assert.Equal((60d, 90d), (sql.Chart.Series[0].Amber!.Value, sql.Chart.Series[0].Red!.Value));
        Assert.Equal((70d, 85d), (sql.Chart.Series[1].Amber!.Value, sql.Chart.Series[1].Red!.Value));
        Assert.Equal(sql.Chart.RedLine, sql.Chart.Series[0].Red);
        Assert.Equal(95d, sql.Chart.Series[0].Points[0].Peak!.Value, 6);

        var registry = AcrCard.View(Week("storage_gb", 1, 2, new Dictionary<string, string?> { ["size_limit_gb"] = "10", ["tier"] = "Basic" }));
        Assert.Equal(registry.Chart.RedLine, registry.Chart.Series[0].Red);
        Assert.Equal((CloudThresholds.RegistryPulls.Amber, CloudThresholds.RegistryPulls.Red), (registry.Chart.Series[1].Amber!.Value, registry.Chart.Series[1].Red!.Value));
    }
}
