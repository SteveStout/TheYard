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
public partial class CloudViewTests(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
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
        Assert.EndsWith("/sqldb-theyard-ss-basic", ResourceDirectory.Find(all, ResourceCatalog.SqlType, "/sqldb-theyard-ss-basic")!.Id, StringComparison.Ordinal);
        Assert.Equal("crtheyardsszmnetj67bn5h2", ResourceDirectory.Find(all, ResourceCatalog.AcrType, null)!.Name);
        Assert.Null(ResourceDirectory.Find(all, ResourceCatalog.MailType, null));
        // Two accounts of one type in the group: the configured name selects, not the alphabet
        // (1.0.4.0 asked for the first of the type, the review of 1.0.4.0, finding 2).
        using var two = JsonDocument.Parse("""
            {"value":[
              {"id":"/s/x/providers/Microsoft.DocumentDB/databaseAccounts/cosmos-jobhunt-ss","name":"cosmos-jobhunt-ss","type":"Microsoft.DocumentDB/databaseAccounts"},
              {"id":"/s/x/providers/Microsoft.DocumentDB/databaseAccounts/cosmos-theyard-ss","name":"cosmos-theyard-ss","type":"Microsoft.DocumentDB/databaseAccounts"}
            ]}
            """);
        var both = ResourceDirectory.Parse(two.RootElement);
        Assert.Equal("cosmos-jobhunt-ss", ResourceDirectory.Find(both, ResourceCatalog.CosmosType, null)!.Name);
        Assert.Equal("cosmos-theyard-ss", ResourceDirectory.Find(both, ResourceCatalog.CosmosType, "cosmos-theyard-ss")!.Name);
        // Each card reading the configured one of two is held by a group of two of each type below.
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
        // The same query through Resource Manager answers in Pascal case, as read live on 18 October.
        using var pascal = JsonDocument.Parse("""{"Tables":[{"Columns":[{"ColumnName":"n","ColumnType":"long"},{"ColumnName":"gb","ColumnType":"real"}],"Rows":[[58,0.098]],"TableName":"Table_0"}]}""");
        Assert.Equal("0.098", AzureMetrics.QueryRows(pascal.RootElement)[0]["gb"]);
    }


    [Fact]
    [Covers("GET", "/api/admin/cloud/{key}", 200)]
    [Covers("GET", "/api/admin/cloud/{key}", 404)]
    public async Task The_route_answers_for_the_seven_keys_and_404s_for_any_other()
    {
        using var client = factory.CreateClient();
        foreach (string key in new[] { "sqldb", "cosmosdb", "registry", "insights", "mail", "edge", "nightly" })
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
    [Fact]
    public void A_build_the_ignore_rule_skipped_is_skipped_never_failed()
    {
        // As Netlify listed them on 18 October: the ignore rule's cancel is filed as an error.
        using var deploys = JsonDocument.Parse("""
            [
              {"state":"error","error_message":"Canceled build due to no content change","created_at":"2026-10-18T19:34:47Z"},
              {"state":"ready","created_at":"2026-10-08T19:33:09Z"},
              {"state":"error","error_message":"Build script returned non-zero exit code: 2","created_at":"2026-10-07T10:00:00Z"}
            ]
            """);
        var (log, live, skipped, failed) = NetlifyEdge.DeployLog(deploys.RootElement);
        Assert.Equal((1, 1, 1), (live, skipped, failed));
        Assert.StartsWith("Oct 18 19:34 UTC skipped, no edge change", log, StringComparison.Ordinal);
        Assert.DoesNotContain("failed", log.Split(" · ")[0], StringComparison.Ordinal);
    }
    [Fact]
    public void A_list_a_row_answers_with_is_a_table_and_says_what_type_each_resource_is()
    {
        string fact = CloudTable.ToFact([["plan", "Cosmos DB container", "31 days"], ["users", "Cosmos DB container", "kept\tuntil\ndeleted"]]);
        var table = CloudTable.FromFact(fact, "Container", "Type", "Kept for");
        Assert.Equal(["Container", "Type", "Kept for"], table.Columns);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("kept until deleted", table.Rows[1][2].Text);
        Assert.Empty(CloudTable.FromFact(null, "A").Rows);
        Assert.Empty(new CloudRow("k", "q", "a", [], "good").Table.Columns);

        // The plan's apps are described from Azure's own read of each site, never from its name.
        Assert.Equal("Web app, Linux container", PlanRows.TypeOf("app,linux,container"));
        Assert.Equal("Web app, Linux", PlanRows.TypeOf("app,linux"));
        Assert.Equal("Web app", PlanRows.TypeOf("app"));
        Assert.Equal("not read", PlanRows.TypeOf(null));
        Assert.Equal("theyard:v1.0.3.186", PlanRows.ImageOf("DOCKER|crtheyardsszmnetj67bn5h2.azurecr.io/theyard:v1.0.3.186"));
        Assert.Equal("theyard-render:v1.0.3.186", PlanRows.ImageOf("DOCKER|crtheyardsszmnetj67bn5h2.azurecr.io/theyard-render:v1.0.3.186"));
        Assert.Equal("NODE 20-lts", PlanRows.ImageOf("NODE|20-lts"));
        Assert.Equal("not read", PlanRows.ImageOf(""));
        // The edge card names who answered a page and the API from the header each web app sends.
        Assert.Equal("the page answered by APP-THEYARD-RENDER-SS-ZMNETJ67BN5H2", NetlifyEdge.AnsweredBy("the page", "APP-THEYARD-RENDER-SS-ZMNETJ67BN5H2"));
        Assert.Equal("/api answered without naming its app", NetlifyEdge.AnsweredBy("/api", null));
    }

    [Fact]
    public void The_registry_storage_dial_is_read_against_the_size_limit_its_usages_state()
    {
        using var usages = JsonDocument.Parse("""{"value":[{"name":"Webhooks","limit":2,"currentValue":0,"unit":"Count"},{"name":"Size","limit":10737418240,"currentValue":536870912,"unit":"Bytes"}]}""");
        Assert.Equal(10, AcrCard.SizeLimitGb(usages.RootElement));
        using var none = JsonDocument.Parse("{}");
        Assert.Null(AcrCard.SizeLimitGb(none.RootElement));
        Assert.Null(AcrCard.SizeLimitGb(null));

        // Unread, the dial says so rather than reading against a typed size.
        var report = AcrCard.View(Held("storage_gb", 1));
        Assert.Contains("not read yet", report.Dials[0].Absolute, StringComparison.Ordinal);
    }
    // #endregion cloud-view-tests
}
