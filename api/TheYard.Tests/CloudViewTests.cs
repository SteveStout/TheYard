using System.Net;
using System.Text.Json;
using TheYard.Api;
using TheYard.Application;

namespace TheYard.Tests;

/// <summary>
/// The six cards after the plan (ADR: One frame for every resource): every portal link built from
/// what Azure returned, the resource group's list each card finds its resource in, a Log Analytics
/// answer read by column, and the one route that serves them, only for the keys the catalogue names.
/// </summary>
public class CloudViewTests(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    : IClassFixture<Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A card's inputs with one reading held for fifteen minutes and an hour of facts.</summary>
    private static ResourceCardInputs Held(string reading, double value, IReadOnlyDictionary<string, string?>? facts = null, IReadOnlyDictionary<string, double?>? sums = null, int minutes = 15)
    {
        var lastHour = Enumerable.Range(0, minutes)
            .Select(i => new ResourceMinute(Now.AddMinutes(i - minutes), new Dictionary<string, double?> { [reading] = value }, "1.0.3.171"))
            .ToList();
        var bucket = new ResourceBucket(Now.AddMinutes(-15), minutes, new Dictionary<string, double?> { [reading] = value }, new Dictionary<string, double?> { [reading] = value }, sums ?? new Dictionary<string, double?>(), "1.0.3.171");
        var hours = facts is null ? new List<ResourceHour>() : [new ResourceHour(Now.AddHours(-1), "/subscriptions/s/resourceGroups/RG-THEYARD-SS/providers/T/x", "tenant", facts, [])];
        return new ResourceCardInputs("24h", TimeSpan.FromHours(24), Now, new PlanHistoryAvailability(true, "kept in Cosmos DB, 31 days"), lastHour, [bucket], [bucket], hours, 1.23);
    }

    // #region cloud-view-tests
    [Fact]
    public void Every_portal_link_is_built_from_the_id_and_tenant_Azure_returned()
    {
        var view = AcrCard.View(Held("storage_gb", 1, new Dictionary<string, string?>()));
        Assert.Equal("https://portal.azure.com/#@tenant/resource/subscriptions/s/resourceGroups/RG-THEYARD-SS/providers/T/x", view.Resource!.Href);
        Assert.Null(AcrCard.View(Held("storage_gb", 1)).Resource);
    }


    [Fact]
    public void Each_card_finds_its_resource_in_the_group_by_type_and_name()
    {
        using var listed = JsonDocument.Parse("""
            {"value":[
              {"id":"/s/x/providers/Microsoft.Sql/servers/sql-theyard-ss-westus3/databases/sqldb-theyard-ss","name":"sql-theyard-ss-westus3/sqldb-theyard-ss","type":"Microsoft.Sql/servers/databases"},
              {"id":"/s/x/providers/Microsoft.Sql/servers/sql-theyard-ss-westus3/databases/sqldb-theyard-ss-basic","name":"sql-theyard-ss-westus3/sqldb-theyard-ss-basic","type":"Microsoft.Sql/servers/databases"},
              {"id":"/s/x/providers/Microsoft.ContainerRegistry/registries/crtheyardsszmnetj67bn5h2","name":"crtheyardsszmnetj67bn5h2","type":"Microsoft.ContainerRegistry/registries"}
            ]}
            """);
        var all = ResourceDirectory.Parse(listed.RootElement);
        Assert.EndsWith("/sqldb-theyard-ss-basic", ResourceDirectory.Find(all, ResourceCatalog.SqlType, "/" + ResourceCatalog.SqlDatabase)!.Id, StringComparison.Ordinal);
        Assert.Equal("crtheyardsszmnetj67bn5h2", ResourceDirectory.Find(all, ResourceCatalog.AcrType, null)!.Name);
        Assert.Null(ResourceDirectory.Find(all, ResourceCatalog.MailType, null));
        using var empty = JsonDocument.Parse("{}");
        Assert.Empty(ResourceDirectory.Parse(empty.RootElement));
    }


    [Fact]
    public void A_log_analytics_answer_comes_back_as_rows_by_column_name()
    {
        using var answer = JsonDocument.Parse("""{"tables":[{"name":"PrimaryResult","columns":[{"name":"AppRoleName","type":"string"},{"name":"items","type":"long"}],"rows":[["theyard",42],[null,3]]}]}""");
        var rows = AzureMetrics.QueryRows(answer.RootElement);
        Assert.Equal("theyard", rows[0]["AppRoleName"]);
        Assert.Equal("42", rows[0]["items"]);
        Assert.Null(rows[1]["AppRoleName"]);
        using var none = JsonDocument.Parse("{}");
        Assert.Empty(AzureMetrics.QueryRows(none.RootElement));
    }


    [Fact]
    public async Task The_route_answers_for_the_six_keys_and_404s_for_any_other()
    {
        using var client = factory.CreateClient();
        foreach (string key in new[] { "sqldb", "cosmosdb", "registry", "insights", "mail", "edge" })
        {
            var response = await client.GetAsync($"/api/admin/cloud/{key}?window=24h");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.False(body.RootElement.GetProperty("available").GetBoolean());
            Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("rows").ValueKind);
        }
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/admin/cloud/plan")).StatusCode);
    }
    [Fact]
    public void Every_metric_a_card_asks_for_is_one_its_resource_reports()
    {
        // Azure refuses a whole request that names a metric the resource does not report, which
        // is how the plan card's first roll lost every minute. The names below are what each
        // resource's metric definitions listed through the runner on 18 October.
        var reports = new Dictionary<string, string[]>
        {
            ["sql"] = ["dtu_consumption_percent", "storage", "log_write_percent", "sessions_count", "workers_percent", "blocked_by_firewall", "deadlock", "connection_failed"],
            ["cosmos"] = ["TotalRequestUnits", "NormalizedRUConsumption", "DataUsage", "DocumentCount", "ServerSideLatency", "TotalRequests"],
            ["registry"] = ["StorageUsed", "TotalPullCount", "SuccessfulPullCount", "SuccessfulPushCount", "RunDuration"],
            ["mail"] = ["ApiRequests", "DeliveryStatusUpdate", "UserEngagement"],
        };
        Assert.All(SqlCard.Metrics, m => Assert.Contains(m.Azure, reports["sql"]));
        Assert.All(CosmosCard.Metrics, m => Assert.Contains(m.Azure, reports["cosmos"]));
        Assert.All(AcrCard.Metrics, m => Assert.Contains(m.Azure, reports["registry"]));
        Assert.All(MailCard.Metrics, m => Assert.Contains(m.Azure, reports["mail"]));
        // The slow ones are asked at the grain Azure keeps them at.
        Assert.Equal("PT1H", AcrCard.Metrics.Single(m => m.Azure == "StorageUsed").Interval);
        Assert.All(CosmosCard.Metrics.Where(m => m.Azure is "DataUsage" or "DocumentCount"), m => Assert.Equal("PT5M", m.Interval));
    }
    // #endregion cloud-view-tests
}
