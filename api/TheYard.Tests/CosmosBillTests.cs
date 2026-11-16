using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// Why a free-tier Cosmos DB account costs anything, on its own card (ADR: What Azure charges, the addendum on Cosmos
/// DB's meter): read on the bill by meter, its whole charge is the data it sends out of its region, because the account
/// and the site that reads it are in two, so the card names both regions from the account's record and the site's own.
/// </summary>
public sealed class CosmosBillTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 16, 0, 0, TimeSpan.Zero);

    private static ResourceCardInputs Inputs(string? account, string? site, double? cost) =>
        new("24h", TimeSpan.FromHours(24), Now, new PlanHistoryAvailability(true, "kept in the test"), [], [], [],
            [new ResourceHour(Now.AddHours(-1), "/subscriptions/s/resourceGroups/g/providers/T/x", "tenant", new Dictionary<string, string?> { ["location"] = account, ["site_region"] = site, ["throughput"] = "1000" }, [])], cost);

    [Fact]
    public void Two_regions_name_the_meter_and_say_fewer_reads_cost_less()
    {
        var row = CosmosCard.View(Inputs("westus2", "West US 3", 3.38)).Rows.Single(r => r.Key == "bill");

        Assert.Equal("$3.38 month to date", row.Answer);
        Assert.Equal("info", row.Tone);
        Assert.Contains("out of westus2 to the site in West US 3, the meter Inter Region Data Transfer Out", row.Detail[0].Text, StringComparison.Ordinal);
        Assert.Contains("fewer reads cost less", row.Detail[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void One_region_written_two_ways_is_one_region()
    {
        Assert.Equal(CosmosCard.Region("West US 3"), CosmosCard.Region("westus3"));
        Assert.NotEqual(CosmosCard.Region("westus2"), CosmosCard.Region("westus3"));
        Assert.Contains("both in West US 3", CosmosCard.View(Inputs("westus3", "West US 3", 0.1)).Rows.Single(r => r.Key == "bill").Detail[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_region_not_read_says_so_and_a_bill_not_read_is_not_a_dollar_figure()
    {
        var row = CosmosCard.View(Inputs("westus2", null, null)).Rows.Single(r => r.Key == "bill");

        Assert.Equal("not on the bill yet", row.Answer);
        Assert.Contains("the site's is not read yet", row.Detail[0].Text, StringComparison.Ordinal);
    }
}
