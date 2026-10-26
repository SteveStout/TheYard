using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The Application Insights card reads a cap against what that cap governs (ADR: One frame for every resource, the
/// addendum on the Insights cap): a workspace's quota against everything the workspace took, a component's cap against
/// what that component alone sent. On 25 October the card read 173 % of the component's 0.1 GB cap, red on the rail,
/// because it set the whole workspace's ingestion, three apps, against one component's cap, on a day no hour was capped.
/// </summary>
public sealed class InsightsCapTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 25, 19, 30, 0, TimeSpan.Zero);

    private static ResourceCardInputs Inputs(string capFrom)
    {
        var minute = new Dictionary<string, double?>
        {
            ["ingest_today_gb"] = 0.173,
            ["ingest_component_today_gb"] = 0.021,
            ["ingest_month_gb"] = 2.7,
            ["sampling"] = 100,
            ["capped"] = 0,
        };
        var lastHour = Enumerable.Range(0, 60).Select(i => new ResourceMinute(Now.AddMinutes(i - 60), minute, "test")).ToList();
        var bucket = new ResourceBucket(Now.AddMinutes(-60), 60, minute, minute, minute, "test");
        var facts = new Dictionary<string, string?> { ["cap_gb"] = "0.1", ["cap_from"] = capFrom, ["workspace_name"] = "log-test" };
        return new ResourceCardInputs("24h", TimeSpan.FromDays(1), Now, new PlanHistoryAvailability(true, "kept in the test"), lastHour, [bucket], [bucket],
            [new ResourceHour(Now.AddHours(-1), "/subscriptions/s/resourceGroups/g/providers/microsoft.insights/components/appi-test", "tenant", facts, [])], null);
    }

    [Fact]
    public void A_component_s_cap_is_read_against_what_the_component_sent_and_the_workspace_s_total_is_shown_beside_it()
    {
        var card = InsightsCard.View(Inputs("the component"));

        Assert.Equal("Ingested today, this component", card.Dials[0].Name);
        Assert.Equal("0.021 of 0.1 GB cap", card.Dials[0].Absolute);
        Assert.Equal("good", card.Dials[0].Tone);
        Assert.Equal("yes", card.Rows.Single(row => row.Key == "cap").Answer);
        Assert.Equal("0.021 GB", card.Readout.Lines.Single(line => line.Label == "Today, this component").Value);
        Assert.Equal("0.173 GB", card.Readout.Lines.Single(line => line.Label == "Today, the whole workspace").Value);
    }

    [Fact]
    public void A_workspace_s_quota_is_still_read_against_everything_the_workspace_took()
    {
        var card = InsightsCard.View(Inputs("the workspace"));

        Assert.Equal("Ingested today", card.Dials[0].Name);
        Assert.Equal("0.173 of 0.1 GB cap", card.Dials[0].Absolute);
        Assert.Equal("alarm", card.Dials[0].Tone);
        Assert.Equal(("close: today is 173 % of it", "alarm"), (card.Rows.Single(row => row.Key == "cap").Answer, card.Rows.Single(row => row.Key == "cap").Tone));
    }

    [Fact]
    public void A_component_near_its_cap_says_how_near_and_names_the_two_ways_out_without_changing_the_cap()
    {
        // After 1.0.4.84 the site's own ingestion read about 0.085 GB a day against its 0.1 GB cap.
        var cap = InsightsCard.CapRow(Inputs("the component"), 0.1, 0.085, "ingest_component_today_gb", true, PlanTone.Good);

        Assert.Equal(("close: today is 85 % of it", "warning"), (cap.Answer, cap.Tone));
        Assert.Contains("two ways out: raise the component's daily cap, a setting this card never changes, or send less from the job hunt's Function", cap.Detail[0].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("two ways out", InsightsCard.CapRow(Inputs("the component"), 0.1, 0.021, "ingest_component_today_gb", true, PlanTone.Good).Detail[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_component_s_own_query_names_the_component_by_its_resource_id()
    {
        string kusto = new InsightsSource(new AzureMetrics("client", configured: false), "/subscriptions/s/resourceGroups/g/providers/microsoft.insights/components/appi-test", "/w").ComponentTodayKusto;
        Assert.Contains("_ResourceId =~ '/subscriptions/s/resourceGroups/g/providers/microsoft.insights/components/appi-test'", kusto, StringComparison.Ordinal);
        Assert.Contains("startofday(now())", kusto, StringComparison.Ordinal);
        Assert.Contains("sum(_BilledSize) / 1073741824.0", kusto, StringComparison.Ordinal);
    }
}
