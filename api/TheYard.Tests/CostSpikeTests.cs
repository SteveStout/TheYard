using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

/// <summary>
/// The spike guard and the meters (ADR: What Azure charges, the addendum on the spike guard): each judged day
/// beside the median of the finished week before it and its biggest line, the October incident read the way the
/// card reads it, a short month's median over what there is, every named meter joined to the rule Azure holds
/// for it, the rules read from Azure's own answers, and both on the wire as lists, never null.
/// </summary>
public class CostSpikeTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>October's days, one line a day, then the incident; the 23rd carries a second, smaller line.</summary>
    private static List<CostDay> October()
    {
        double[] quiet = [1.43, 1.46, 1.41, 1.45, 1.44, 1.49, 1.42, 1.45, 1.41, 1.44, 1.47];
        var days = quiet.Select((cost, at) => new CostDay($"2026-10-{9 + at:00}", "plan-theyard-ss", "microsoft.web/serverfarms", cost, "USD")).ToList();
        days.Add(new CostDay("2026-10-20", "stjobhuntss", "microsoft.storage/storageaccounts", 3.07, "USD"));
        days.Add(new CostDay("2026-10-21", "stjobhuntss", "microsoft.storage/storageaccounts", 7.82, "USD"));
        days.Add(new CostDay("2026-10-22", "stjobhuntss", "microsoft.storage/storageaccounts", 13.41, "USD"));
        days.Add(new CostDay("2026-10-23", "stjobhuntss", "microsoft.storage/storageaccounts", 24.99, "USD"));
        days.Add(new CostDay("2026-10-23", "plan-theyard-ss", "microsoft.web/serverfarms", 0.75, "USD"));
        return days;
    }

    [Fact]
    public void The_october_incident_reads_seventeen_times_the_weeks_median_driven_by_the_share()
    {
        var spike = DaySpike.For(October(), "2026-10-23", partial: false);

        Assert.Equal(DaySpike.Shown, spike.Count);
        Assert.Equal(["2026-10-23", "2026-10-22", "2026-10-21", "2026-10-20", "2026-10-19", "2026-10-18", "2026-10-17", "2026-10-16"], spike.Select(day => day.Day));
        var worst = spike[0];
        Assert.Equal(25.74, worst.Cost);
        // The seven before the 23rd: four quiet days and three of the incident, so the median is the quiet top.
        Assert.Equal(1.47, worst.Median);
        Assert.Equal(7, worst.Days);
        Assert.Equal(17.5, worst.Multiple);
        Assert.Equal("stjobhuntss", worst.Biggest);
        Assert.Equal(24.99, worst.BiggestCost);
        Assert.False(worst.Partial);
        // The 20th, $3.07 over a quiet week: about twice, under the $5 floor the page applies.
        Assert.Equal(1.44, spike[3].Median);
        Assert.Equal(2.1, spike[3].Multiple);
    }

    [Fact]
    public void A_short_history_takes_its_median_over_what_there_is_and_says_how_many()
    {
        var days = new[]
        {
            new CostDay("2026-11-01", "plan-theyard-ss", "microsoft.web/serverfarms", 1.33, "USD"),
            new CostDay("2026-11-02", "plan-theyard-ss", "microsoft.web/serverfarms", 1.38, "USD"),
            new CostDay("2026-11-03", "plan-theyard-ss", "microsoft.web/serverfarms", 1.59, "USD"),
            new CostDay("2026-11-04", "plan-theyard-ss", "microsoft.web/serverfarms", 0.75, "USD"),
            new CostDay("2026-11-05", "plan-theyard-ss", "microsoft.web/serverfarms", 9, "USD"),
        };

        var spike = DaySpike.For(days, "2026-11-04", partial: true);

        Assert.Equal(4, spike.Count);
        Assert.True(spike[0].Partial);
        Assert.Equal(3, spike[0].Days);
        Assert.Equal(1.38, spike[0].Median);
        Assert.Equal(2, spike[1].Days);
        Assert.Null(spike[^1].Median);
        Assert.Null(spike[^1].Multiple);
        Assert.Equal(0, spike[^1].Days);
        Assert.Empty(DaySpike.For([], "2026-11-04", partial: true));
    }

    [Fact]
    public void Every_named_meter_is_joined_to_its_rule_by_name()
    {
        Assert.All(MeterRows.From(null), row => Assert.Equal("not read yet", row.State));
        var found = new Dictionary<string, FoundRule>(StringComparer.OrdinalIgnoreCase)
        {
            ["alert-jobhunt-share-transactions"] = new("alert-jobhunt-share-transactions", true, "Transactions over 20,000 in 1 hour"),
            ["alert-jobhunt-blob-egress"] = new("alert-jobhunt-blob-egress", false, "Egress over 2,000,000,000 in 1 hour"),
        };

        var rows = MeterRows.From(new AlertRulesRead(found, [], DateTimeOffset.UnixEpoch));

        Assert.Equal(MeterRows.Named.Count, rows.Count);
        Assert.Equal(("exists", "Transactions over 20,000 in 1 hour"), (rows[0].State, rows[0].Line));
        Assert.Equal("off", rows[2].State);
        Assert.Equal(("not set", "no rule by this name: it waits for its deploy"), (rows[1].State, rows[1].Line));
        var refused = MeterRows.From(new AlertRulesRead(found, ["this site's identity cannot list the budgets; it needs Cost Management Reader on the subscription"], DateTimeOffset.UnixEpoch));
        Assert.Contains("Cost Management Reader", refused.Single(row => row.Rule == "budget-subscription-monthly").Line, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_named_meters_rule_is_written_in_a_template_under_its_own_name()
    {
        string templates = string.Concat(Directory.GetFiles(Path.Combine(Repo.Root(), "infra"), "jobhunt*.bicep").Select(File.ReadAllText));

        Assert.All(MeterRows.Named, meter => Assert.Contains($"name: '{meter.Rule}'", templates, StringComparison.Ordinal));
    }

    [Fact]
    public void The_rules_are_read_from_azures_own_answers()
    {
        using var metric = JsonDocument.Parse("""{"value":[{"name":"alert-jobhunt-blob-egress","properties":{"enabled":true,"windowSize":"PT1H","criteria":{"allOf":[{"metricName":"Egress","threshold":2000000000}]}}}]}""");
        using var query = JsonDocument.Parse("""{"value":[{"name":"alert-theyard-ingestion","properties":{"enabled":false,"evaluationFrequency":"PT1H","windowSize":"P1D"}}]}""");
        using var budget = JsonDocument.Parse("""{"value":[{"name":"budget-subscription-monthly","properties":{"amount":60,"notifications":{"actual80":{"threshold":80,"thresholdType":"Actual"},"forecast100":{"threshold":100,"thresholdType":"Forecasted"}}}}]}""");
        using var anomaly = JsonDocument.Parse("""{"value":[{"name":"anomaly-subscription-daily","kind":"InsightAlert","properties":{"status":"Enabled"}},{"name":"a-report","kind":"Email","properties":{"status":"Enabled"}}]}""");

        var egress = AlertRules.MetricRule(AlertRules.Entries(metric.RootElement).Single());
        var ingestion = AlertRules.QueryRule(AlertRules.Entries(query.RootElement).Single());
        var amount = AlertRules.BudgetRule(AlertRules.Entries(budget.RootElement).Single());
        var anomalies = AlertRules.Entries(anomaly.RootElement).Select(AlertRules.AnomalyRule).ToList();

        Assert.Equal(new FoundRule("alert-jobhunt-blob-egress", true, "Egress over 2,000,000,000 in 1 hour"), egress);
        Assert.Equal(new FoundRule("alert-theyard-ingestion", false, "a log alert checked every 1 hour over 1 day"), ingestion);
        Assert.Equal("$60 a month, notices at 80 %, 100 % of the forecast", amount?.Line);
        Assert.True(anomalies[0]?.Enabled);
        Assert.Null(anomalies[1]);
        Assert.Equal("15 minutes", AlertRules.Duration("PT15M"));
        Assert.Equal("2 days", AlertRules.Duration("P2D"));
    }

    [Fact]
    public async Task The_wire_carries_the_spike_days_and_every_meter_as_lists()
    {
        var response = await factory.CreateClient().GetAsync("/api/admin/costs?window=30d");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("spike_days").ValueKind);
        var meters = body.RootElement.GetProperty("meters");
        Assert.Equal(MeterRows.Named.Count, meters.GetArrayLength());
        Assert.Equal("not read yet", meters[0].GetProperty("state").GetString());
    }
}
