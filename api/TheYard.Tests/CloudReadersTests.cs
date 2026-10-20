using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using TheYard.Api;
using TheYard.Application;

namespace TheYard.Tests;

// #region recorded-cloud
/// <summary>
/// Azure Resource Manager, Azure Monitor, Log Analytics, the registry and Netlify as recorded
/// answers, in the shapes each service gives. The readers' calls are routed here through
/// OutboundHttp's seam for the test's own flow, so every reader runs its real request building,
/// its real parsing and its real failure paths without a credential or a network.
/// </summary>
internal sealed class RecordedCloud : HttpMessageHandler
{
    public const string Subscription = "sub-1";
    public const string Group = "/subscriptions/sub-1/resourceGroups/RG-THEYARD-SS";
    public const string Plan = Group + "/providers/Microsoft.Web/serverfarms/asp-theyard-ss";
    public const string Site = Group + "/providers/Microsoft.Web/sites/app-theyard-ss";
    public const string CosmosSite = Group + "/providers/Microsoft.Web/sites/app-theyard-cosmos";
    public const string Sql = Group + "/providers/Microsoft.Sql/servers/sql-theyard/databases/sqldb-theyard-ss-basic";
    public const string Cosmos = Group + "/providers/Microsoft.DocumentDB/databaseAccounts/cosmos-theyard-ss";
    public const string Registry = Group + "/providers/Microsoft.ContainerRegistry/registries/crtheyard";
    public const string Component = Group + "/providers/Microsoft.Insights/components/appi-theyard-ss";
    public const string Workspace = Group + "/providers/Microsoft.OperationalInsights/workspaces/log-theyard-ss";
    public const string Mail = Group + "/providers/Microsoft.Communication/CommunicationServices/acs-theyard-ss";
    public const string Domain = Mail + "/domains/AzureManagedDomain";

    /// <summary>A metric Azure refuses by name with a 400 at every grain but five minutes, the way AppConnections and FileSystemUsage were refused.</summary>
    public string? Refused { get; init; }

    /// <summary>Whether the Log Analytics query answers 403, the way it did before the reader role was granted.</summary>
    public bool QueryForbidden { get; init; }

    /// <summary>Whether Netlify's site list holds the edge's site.</summary>
    public bool EdgeListed { get; init; } = true;

    /// <summary>Whether the edge was just purged: an address's first ask is a miss it stores, every later ask a hit.</summary>
    public bool JustPurged { get; init; }

    /// <summary>Every address asked for, in order.</summary>
    public List<string> Asked { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Asked.Add(uri.ToString());
        return uri.Host switch
        {
            "169.254.169.254" => Json(new { access_token = Jwt() }),
            "management.azure.com" => request.Method == HttpMethod.Post ? Query(body) : Arm(uri),
            "crtheyard.azurecr.io" => Registry_(uri),
            "api.netlify.com" => Netlify(uri),
            "stevenstout.biz" => JustPurged ? Purged(uri, Asked.Count(a => a == uri.ToString())) : Edge(uri),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private static HttpResponseMessage Json(object answer, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(JsonSerializer.Serialize(answer), Encoding.UTF8, "application/json") };

    /// <summary>A token whose payload names a tenant, the one claim the readers read.</summary>
    private static string Jwt() =>
        "e30." + Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"tid\":\"tenant-1\"}")).TrimEnd('=').Replace('+', '-').Replace('/', '_') + ".sig";

    private HttpResponseMessage Arm(Uri uri)
    {
        string path = Uri.UnescapeDataString(uri.AbsolutePath);
        string query = Uri.UnescapeDataString(uri.Query);
        if (path.EndsWith("/providers/microsoft.insights/metrics", StringComparison.OrdinalIgnoreCase))
        {
            return Metrics(query);
        }
        if (path.EndsWith("/eventtypes/management/values", StringComparison.OrdinalIgnoreCase))
        {
            return Json(new
            {
                value = new object[]
                {
                    new { operationName = new { value = "Microsoft.Web/sites/restart/action", localizedValue = "Restart Web App" }, eventTimestamp = "2026-10-18T20:10:00Z", status = new { value = "Succeeded" } },
                    new { operationName = new { value = "Microsoft.Web/sites/config/write", localizedValue = "Update web app config" }, eventTimestamp = "2026-10-18T20:20:00Z", status = new { value = "Succeeded" }, caller = "deploy@github" },
                    new { operationName = new { value = "Microsoft.Web/sites/config/write", localizedValue = "Update web app config" }, eventTimestamp = "2026-10-18T20:19:00Z", status = new { value = "Started" }, caller = "deploy@github" },
                    new { operationName = new { value = "Microsoft.ResourceHealth/healthevent/action" }, eventTimestamp = "not a time" },
                },
            });
        }
        return path switch
        {
            Group + "/resources" => Json(new
            {
                value = new object[]
                {
                    new { id = Sql, name = "sql-theyard/sqldb-theyard-ss-basic", type = ResourceCatalog.SqlType },
                    new { id = Group + "/providers/Microsoft.Sql/servers/sql-theyard/databases/master", name = "sql-theyard/master", type = ResourceCatalog.SqlType },
                    new { id = Group + "/providers/Microsoft.Sql/servers/sql-theyard/databases/sqldb-theyard-ss", name = "sql-theyard/sqldb-theyard-ss", type = ResourceCatalog.SqlType },
                    new { id = Cosmos, name = "cosmos-theyard-ss", type = ResourceCatalog.CosmosType },
                    new { id = Registry, name = "crtheyard", type = ResourceCatalog.AcrType },
                    new { id = Component, name = "appi-theyard-ss", type = ResourceCatalog.InsightsType },
                    new { id = Workspace, name = "log-theyard-ss", type = ResourceCatalog.WorkspaceType },
                    new { id = Mail, name = "acs-theyard-ss", type = ResourceCatalog.MailType },
                    new { id = "", name = "no id", type = "nothing" },
                },
            }),
            // The SQL site's record carries its kind, its image and its names; the Cosmos DB site's leaves them out.
            Site => Json(new { kind = "app,linux,container", properties = new { serverFarmId = Plan, state = "Running", siteConfig = new { alwaysOn = true, linuxFxVersion = "DOCKER|crtheyard.azurecr.io/theyard:v1.0.3.186" }, hostNames = new[] { "theshed.example", "app-theyard-ss.azurewebsites.net" }, lastModifiedTimeUtc = "2026-10-18T20:21:00Z" } }),
            CosmosSite => Json(new { properties = new { serverFarmId = Plan, state = "Running", siteConfig = new { alwaysOn = true }, lastModifiedTimeUtc = "2026-10-18T20:21:00Z" } }),
            Plan + "/sites" => Json(new { value = new[] { new { id = Site }, new { id = CosmosSite } } }),
            Plan => Json(new { sku = new { name = "B2", capacity = 1 }, location = "West US 3", kind = "linux", properties = new { maximumNumberOfWorkers = 3 } }),
            Sql => Json(new { sku = new { name = "Basic", tier = "Basic", capacity = 5 }, properties = new { maxSizeBytes = 2_147_483_648L }, location = "westus3" }),
            Group + "/providers/Microsoft.Sql/servers/sql-theyard/databases/sqldb-theyard-ss" => Json(new { sku = new { name = "GP_S_Gen5_1", tier = "GeneralPurpose" }, properties = new { status = "Paused", useFreeLimit = true } }),
            Cosmos => Json(new { properties = new { enableFreeTier = true }, location = "West US 3" }),
            Cosmos + "/sqlDatabases/theyard/throughputSettings/default" => Json(new { properties = new { resource = new { throughput = 1000 } } }),
            Cosmos + "/sqlDatabases" => Json(new { value = new[] { new { name = "theyard" } } }),
            Cosmos + "/sqlDatabases/theyard/containers" => Json(new
            {
                value = new object[]
                {
                    new { name = "plan", properties = new { resource = new { defaultTtl = 2_678_400 } } },
                    new { name = "vehicles", properties = new { resource = new { } } },
                },
            }),
            Registry => Json(new { sku = new { name = "Basic" }, properties = new { loginServer = "crtheyard.azurecr.io" }, location = "westus3" }),
            Component => Json(new { properties = new { WorkspaceResourceId = Workspace, SamplingPercentage = 100 }, location = "westus3" }),
            Component + "/currentbillingfeatures" => Json(new { DataVolumeCap = new { Cap = 0.1 } }),
            Workspace => Json(new { properties = new { workspaceCapping = new { dailyQuotaGb = -1 }, retentionInDays = 30 } }),
            Mail => Json(new { properties = new { dataLocation = "United States", linkedDomains = new[] { Domain } } }),
            Domain => Json(new { name = "AzureManagedDomain", properties = new { domainManagement = "AzureManaged", mailFromSenderDomain = "ae4d.azurecomm.net" } }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    /// <summary>
    /// Azure Monitor's answer for the names asked: each one's points at the interval asked, across
    /// the span, carrying every aggregation; a refused name answers 400 for the whole request, as
    /// Azure does, unless it is asked alone at five minutes.
    /// </summary>
    private HttpResponseMessage Metrics(string query)
    {
        var parts = query.TrimStart('?').Split('&').Select(part => part.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair.Length > 1 ? pair[1] : "", StringComparer.OrdinalIgnoreCase);
        string[] names = parts["metricnames"].Split(',');
        string interval = parts["interval"];
        if (Refused is { } refused && names.Contains(refused) && !(names.Length == 1 && interval == "PT5M"))
        {
            return Json(new { code = "BadRequest" }, HttpStatusCode.BadRequest);
        }
        var span = parts["timespan"].Split('/').Select(t => DateTimeOffset.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        var step = interval switch { "PT5M" => TimeSpan.FromMinutes(5), "PT1H" => TimeSpan.FromHours(1), "PT6H" => TimeSpan.FromHours(6), _ => TimeSpan.FromMinutes(1) };
        var points = new List<object>();
        var first = new DateTimeOffset(span[0].UtcDateTime.Ticks - (span[0].UtcDateTime.Ticks % step.Ticks), TimeSpan.Zero);
        int i = 0;
        for (var at = first; at < span[1] && i < 12; at += step, i++)
        {
            points.Add(new { timeStamp = at.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture), average = 20.0 + i, total = 120.0 + i, maximum = 30.0 + i, count = 3.0 + i });
        }
        points.Add(new { timeStamp = span[1].ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture) });
        return Json(new
        {
            value = names.Select(name => new
            {
                name = new { value = name },
                timeseries = name == "Http5xx" ? Array.Empty<object>() : new object[] { new { data = points } },
            }),
        });
    }

    private HttpResponseMessage Query(string body)
    {
        if (QueryForbidden)
        {
            return new HttpResponseMessage(HttpStatusCode.Forbidden);
        }
        (string[] Columns, object[][] Rows) table =
            body.Contains("summarize gb", StringComparison.Ordinal) ? (["gb"], [[0.098]])
            : body.Contains("summarize kept", StringComparison.Ordinal) ? (["kept", "seen"], [[50, 100]])
            : body.Contains("_LogOperation", StringComparison.Ordinal) ? (["capped"], [[0]])
            : (["AppRoleName", "items"], [["app-theyard-ss", 1_200], [null!, 3]]);
        return Json(new { Tables = new[] { new { TableName = "PrimaryResult", Columns = table.Columns.Select(c => new { ColumnName = c, DataType = "String" }), Rows = table.Rows } } });
    }

    private static HttpResponseMessage Registry_(Uri uri) => uri.AbsolutePath switch
    {
        "/oauth2/exchange" => Json(new { refresh_token = "refresh" }),
        "/oauth2/token" => Json(new { access_token = "access" }),
        "/v2/_catalog" => Json(new { repositories = new[] { "theyard", "render" } }),
        "/acr/v1/theyard/_manifests" => Json(new
        {
            manifests = new object[]
            {
                new { tags = new[] { "1.0.3.176", "latest" }, imageSize = 300_000_000L },
                new { tags = Array.Empty<string>(), imageSize = 1_073_741_824L },
                new { imageSize = 5L },
            },
        }),
        "/acr/v1/render/_manifests" => Json(new { manifests = new[] { new { tags = new[] { "1.0.3.176" }, imageSize = 90_000_000L } } }),
        _ => new HttpResponseMessage(HttpStatusCode.NotFound),
    };

    private HttpResponseMessage Netlify(Uri uri)
    {
        object[] deploys =
        [
            new { created_at = "2026-10-18T21:09:00Z", state = "ready", context = "production" },
            new { created_at = "2026-10-18T20:40:00Z", state = "error", error_message = "Canceled build due to no content change", context = "production" },
            new { created_at = "2026-10-18T19:35:00Z", state = "error", error_message = "Build script returned non-zero exit code: 2", context = "deploy-preview" },
            new { created_at = "2026-10-18T19:00:00Z", state = "building", context = "production" },
            new { created_at = "2026-09-30T19:00:00Z", state = "ready", context = "production" },
        ];
        return uri.AbsolutePath switch
        {
            "/api/v1/sites" => Json(EdgeListed
                ? new object[] { new { name = "someone-else", id = "x" }, new { name = "theyard-edge", id = "site-1", account_slug = "stevestout" } }
                : new object[] { new { name = "someone-else", id = "x" } }),
            "/api/v1/accounts/stevestout/bandwidth" => Json(new { used = 1_073_741_824L, period_start_date = "2026-10-01T00:00:00Z" }),
            "/api/v1/sites/site-1/deploys" => Json(deploys),
            "/api/v1/sites/site-1" => Json(new { custom_domain = "stevenstout.biz", plan = "nf_team_starter", admin_url = "https://app.netlify.com/projects/theyard-edge" }),
            "/api/v1/sites/site-1/ssl" => Json(new { domains = new[] { "stevenstout.biz", "theyard.stevenstout.biz" }, expires_at = "2026-12-08T00:00:00Z" }),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
    }

    private static HttpResponseMessage Purged(Uri uri, int asks)
    {
        var answer = new HttpResponseMessage(HttpStatusCode.OK);
        answer.Headers.TryAddWithoutValidation("Cache-Status", asks == 1 ? "\"Netlify Edge\"; fwd=miss; fwd-status=200; stored" : "\"Netlify Edge\"; hit");
        return answer;
    }

    private static HttpResponseMessage Edge(Uri uri)
    {
        if (uri.AbsolutePath == "/og.png")
        {
            throw new HttpRequestException("the edge did not answer");
        }
        var answer = new HttpResponseMessage(HttpStatusCode.OK);
        // The page is asked only for who answered it; of the scored addresses, the document is a hit.
        answer.Headers.TryAddWithoutValidation("Cache-Status", uri.AbsolutePath is "/" or "/api/docs/readme" ? "\"Netlify Edge\"; hit" : "\"Netlify Edge\"; fwd=miss");
        // The page names the web app that drew it; the API's answer here names none.
        if (uri.AbsolutePath == "/")
        {
            answer.Headers.TryAddWithoutValidation(NetlifyEdge.SiteHeader, "APP-THEYARD-RENDER-SS");
        }
        return answer;
    }
}
// #endregion recorded-cloud

/// <summary>
/// Holds the readers behind the plan card and the six resource cards against Azure's and
/// Netlify's recorded answers (ADR: One frame for every resource): what each one asks for, what
/// it makes of the answer, and what it does when a service refuses part of it.
/// </summary>
public sealed class CloudReadersTests
{
    private static readonly BuildInfo Build = new("1.0.3.177", "abc1234");

    /// <summary>The deployment's names as appsettings.json gives them, for the cards built by hand.</summary>
    private static readonly AzureResources Names = new("RG-THEYARD-SS", "app-theyard-ss", "sqldb-theyard-ss-basic", "cosmos-theyard-ss", "crtheyard", "log-theyard-ss", "acs-theyard-ss", "theyard-edge", new Uri("https://theyard.stevenstout.biz/"), new DateOnly(2026, 10, 30));

    private static string Text(object report) => JsonSerializer.Serialize(report);

    // #region plan-reader
    [Fact]
    public async Task The_plan_reader_reads_the_plan_and_every_app_a_minute_at_a_time()
    {
        var cloud = new RecordedCloud();
        using var route = OutboundHttp.RouteTo(cloud);
        var reader = new PlanReader("client-1", RecordedCloud.Site, configured: true);
        var now = DateTimeOffset.UtcNow;

        var minutes = await reader.ReadMinutesAsync(now.AddMinutes(-5), now, "1.0.3.177", CancellationToken.None);

        Assert.NotEmpty(minutes);
        Assert.All(minutes, minute => Assert.Equal(2, minute.Apps.Count));
        Assert.Contains(minutes, minute => minute.CpuPercent is > 0);
        Assert.Equal("asp-theyard-ss", reader.PlanName);
        Assert.Contains(cloud.Asked, address => address.Contains("FileSystemUsage", StringComparison.Ordinal) && address.Contains("PT6H", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_plan_reader_reads_the_hour_the_size_the_apps_and_what_Azure_did()
    {
        using var route = OutboundHttp.RouteTo(new RecordedCloud());
        var reader = new PlanReader("client-1", RecordedCloud.Site, configured: true, machine: new MachineSize(2, 3.5));
        var hour = new DateTimeOffset(2026, 10, 18, 20, 0, 0, TimeSpan.Zero);

        var read = await reader.ReadHourAsync(hour, hour.AddHours(-1), CancellationToken.None);

        // The size and the instances are Azure's; the cores and memory are the recording process's own machine.
        Assert.Equal(("B2", 1, 3, 2.0, 3.5), (read.Sku, read.Instances, read.MaxWorkers, read.Cores, read.MemoryGb));
        Assert.Equal("tenant-1", read.TenantId);
        Assert.Equal(2, read.Apps.Count);
        Assert.All(read.Apps, app => Assert.True(app.AlwaysOn));
        // What each app is, runs and answers to is read off its record, and left null where the record has none.
        var sql = read.Apps.Single(app => app.ResourceId == RecordedCloud.Site);
        Assert.Equal(("app,linux,container", "DOCKER|crtheyard.azurecr.io/theyard:v1.0.3.186"), (sql.Kind, sql.Image));
        Assert.Equal(["theshed.example", "app-theyard-ss.azurewebsites.net"], sql.Hosts);
        var cosmos = read.Apps.Single(app => app.ResourceId == RecordedCloud.CosmosSite);
        Assert.Equal((null, null, null), (cosmos.Kind, cosmos.Image, cosmos.Hosts));
        Assert.Contains(read.Events, e => e.Kind == "restart" && !e.Planned);
        Assert.Contains(read.Events, e => e.Kind == "config" && e.Planned);
        Assert.DoesNotContain(read.Events, e => e.Title.Contains("Health", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_plan_keep_reads_through_the_recorder_when_it_is_empty()
    {
        using var route = OutboundHttp.RouteTo(new RecordedCloud());
        var history = new MemoryPlanHistory("no document store in the test");
        var recorder = new PlanRecorder(new PlanReader("client-1", RecordedCloud.Site, configured: true), history, Build, TimeProvider.System, NullLogger<PlanRecorder>.Instance);
        var reader = new PlanHistoryReader(history, recorder, _ => Task.FromResult<double?>(11.14), TimeProvider.System);

        var report = await reader.ReadAsync("24h", CancellationToken.None);
        var again = await reader.ReadAsync("24h", CancellationToken.None);

        Assert.True(report.Available);
        Assert.Same(report, again);
        Assert.NotEmpty(await history.NewestAsync(60, CancellationToken.None));
        Assert.Contains("B2", Text(report), StringComparison.Ordinal);
    }
    // #endregion plan-reader

    // #region resource-cards
    private static (CloudReader Reader, CloudRecorder Recorder, IReadOnlyList<(ResourceCard Card, IResourceHistory History)> Cards) Cards(NetlifyEdge? edge = null)
    {
        var azure = new AzureMetrics("client-1", configured: true);
        var directory = new ResourceDirectory(azure, RecordedCloud.Group, TimeProvider.System);
        var cards = ResourceCatalog.Cards(azure, directory, edge ?? new NetlifyEdge("netlify-token", "theyard-edge", new Uri("https://stevenstout.biz")), (7, "StandingPerVehicle"), Names)
            .Select(card => (card, (IResourceHistory)new MemoryResourceHistory("no document store in the test")))
            .ToList();
        var recorder = new CloudRecorder(cards, Build, TimeProvider.System, NullLogger<CloudRecorder>.Instance);
        return (new CloudReader(cards, recorder, (_, _) => Task.FromResult<double?>(1.5), TimeProvider.System), recorder, cards);
    }

    [Fact]
    public async Task The_cards_read_what_they_say_from_the_resources_themselves()
    {
        using var route = OutboundHttp.RouteTo(new RecordedCloud());
        var (reader, _, _) = Cards();

        // The database's own record, and no other database on the server.
        string sql = Text(await reader.ReadAsync("sqldb", "24h", CancellationToken.None));
        Assert.Contains("BASIC 5 DTU 2 GB", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Paused", sql, StringComparison.Ordinal);
        // The database's shared throughput as Azure has it.
        Assert.Contains("1,000 RU/S SHARED", Text(await reader.ReadAsync("cosmosdb", "24h", CancellationToken.None)), StringComparison.Ordinal);
        // Who answered the page and the API, off the sampled answers' own header.
        string edge = Text(await reader.ReadAsync("edge", "24h", CancellationToken.None));
        Assert.Contains("the page answered by APP-THEYARD-RENDER-SS", edge, StringComparison.Ordinal);
        Assert.Contains("/api answered without naming its app", edge, StringComparison.Ordinal);
        // The project's page is the site record's own admin_url.
        Assert.Equal("https://app.netlify.com/projects/theyard-edge", (await reader.ReadAsync("edge", "24h", CancellationToken.None)).Resource!.Href);
    }

    [Theory]
    [InlineData("sqldb", "sqldb-theyard-ss", "BASIC 5 DTU 2 GB WESTUS3")]
    [InlineData("cosmosdb", "2 containers", "FREE TIER")]
    [InlineData("registry", "2 images, 3 tags", "2 untagged")]
    [InlineData("insights", "app-theyard-ss", "log-theyard-ss")]
    [InlineData("mail", "ae4d.azurecomm.net", "United States")]
    [InlineData("edge", "skipped, no edge change", "stevenstout.biz")]
    public async Task Each_card_reads_its_resource_and_answers_in_the_frame(string key, string expected, string alsoExpected)
    {
        using var route = OutboundHttp.RouteTo(new RecordedCloud());
        var (reader, _, _) = Cards();

        var report = await reader.ReadAsync(key, "24h", CancellationToken.None);
        string text = Text(report);

        Assert.True(report.Available);
        Assert.NotEmpty(report.Rows);
        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.Contains(alsoExpected, text, StringComparison.Ordinal);
        Assert.True(reader.Knows(key));
    }

    [Fact]
    public async Task A_card_read_twice_inside_a_minute_is_the_same_answer_and_reads_Azure_once()
    {
        var cloud = new RecordedCloud();
        using var route = OutboundHttp.RouteTo(cloud);
        var (reader, _, _) = Cards();

        var first = await reader.ReadAsync("sqldb", "7d", CancellationToken.None);
        int asked = cloud.Asked.Count;
        var second = await reader.ReadAsync("sqldb", "7d", CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(asked, cloud.Asked.Count);
    }

    [Fact]
    public async Task An_hourly_card_is_read_once_an_hour_and_a_minute_card_every_minute()
    {
        var cloud = new RecordedCloud();
        using var route = OutboundHttp.RouteTo(cloud);
        var (_, recorder, cards) = Cards();

        await recorder.RecordOnceAsync("edge", CancellationToken.None);
        await recorder.RecordOnceAsync("edge", CancellationToken.None);
        await recorder.RecordOnceAsync("cosmosdb", CancellationToken.None);
        await recorder.RecordOnceAsync("cosmosdb", CancellationToken.None);

        var edge = cards.Single(entry => entry.Card.Key == "edge").History;
        var cosmos = cards.Single(entry => entry.Card.Key == "cosmosdb").History;
        Assert.Single(await edge.HoursAsync(DateTimeOffset.UtcNow.AddHours(-2), CancellationToken.None));
        Assert.Single(await cosmos.HoursAsync(DateTimeOffset.UtcNow.AddHours(-2), CancellationToken.None));
        Assert.Equal(1, cloud.Asked.Count(address => address.Contains("/bandwidth", StringComparison.Ordinal)));
    }
    // #endregion resource-cards

    // #region refusals
    /// <summary>
    /// Azure refuses a whole metrics request for one name it will not serve at that grain. The
    /// reader asks again one name at a time, at the grains Azure keeps, and keeps the rest.
    /// </summary>
    [Fact]
    public async Task A_metric_Azure_refuses_is_read_alone_at_a_grain_it_keeps_and_the_rest_still_read()
    {
        using var route = OutboundHttp.RouteTo(new RecordedCloud { Refused = "TotalRequestUnits" });
        var azure = new AzureMetrics("client-1", configured: true);
        var now = DateTimeOffset.UtcNow;

        var minutes = await azure.MinutesAsync(RecordedCloud.Cosmos, CosmosCard.Metrics, now.AddMinutes(-5), now, "1.0.3.177", CancellationToken.None);

        Assert.NotEmpty(minutes);
        Assert.Contains(minutes, minute => minute.Values["ru_per_s"] is not null);
        Assert.Contains(minutes, minute => minute.Values["latency_ms"] is not null);
        Assert.Contains(minutes, minute => minute.Values["documents"] is not null);
    }

    [Fact]
    public async Task A_refused_query_says_refused_rather_than_failing_the_card()
    {
        using var route = OutboundHttp.RouteTo(new RecordedCloud { QueryForbidden = true });
        var (reader, _, cards) = Cards();

        var report = await reader.ReadAsync("insights", "24h", CancellationToken.None);
        var hour = Assert.Single(await cards.Single(entry => entry.Card.Key == "insights").History.HoursAsync(DateTimeOffset.UtcNow.AddHours(-2), CancellationToken.None));

        Assert.True(report.Available);
        Assert.Equal("refused", hour.Facts["query"]);
        Assert.Equal("nothing reported in a day", hour.Facts["reporters"]);
    }

    [Fact]
    public async Task An_edge_token_that_cannot_see_the_site_says_so_and_an_absent_one_says_missing()
    {
        using var route = OutboundHttp.RouteTo(new RecordedCloud { EdgeListed = false });
        var hour = new DateTimeOffset(2026, 10, 18, 20, 0, 0, TimeSpan.Zero);
        var unseen = new NetlifyEdge("netlify-token", "theyard-edge", new Uri("https://stevenstout.biz"));
        var absent = new NetlifyEdge("__NETLIFY_TOKEN__", "theyard-edge", new Uri("https://stevenstout.biz"));

        var seen = await unseen.HourAsync(hour, CancellationToken.None);
        var minutes = await unseen.MinutesAsync(hour, "1.0.3.177", CancellationToken.None);
        var missing = await absent.HourAsync(hour, CancellationToken.None);

        Assert.Contains("none is theyard-edge", seen.Facts["site"], StringComparison.Ordinal);
        Assert.Empty(minutes);
        Assert.Equal("missing", missing.Facts["token"]);
        Assert.Empty(await absent.MinutesAsync(hour, "1.0.3.177", CancellationToken.None));
    }

    [Fact]
    public async Task The_edge_counts_this_cycles_live_deploys_and_the_samples_that_hit_its_cache()
    {
        using var route = OutboundHttp.RouteTo(new RecordedCloud());
        var edge = new NetlifyEdge("netlify-token", "theyard-edge", new Uri("https://stevenstout.biz"));
        var now = new DateTimeOffset(2026, 10, 18, 21, 30, 0, TimeSpan.Zero);

        var hour = await edge.HourAsync(now, CancellationToken.None);
        var minute = Assert.Single(await edge.MinutesAsync(now, "1.0.3.177", CancellationToken.None));

        Assert.Equal(("2", "1", "1"), (hour.Facts["deploys_live"], hour.Facts["deploys_skipped"], hour.Facts["deploys_failed"]));
        Assert.Equal(1, minute.Values["deploys"]);
        Assert.Equal(1.0, minute.Values["bandwidth_gb"]);
        Assert.Equal(35.0, minute.Values["credits"]);
        // Scored: /og.png did not answer and is left out, /api/facets missed, /api/docs/readme hit.
        Assert.Equal(50.0, minute.Values["cache_hit"]);
        Assert.Equal("/og.png, /api/facets, /api/docs/readme", hour.Facts["sampled"]);
        Assert.Contains("/og.png did not answer", hour.Facts["cache_status"], StringComparison.Ordinal);
    }

    /// <summary>
    /// Just after a deploy's purge every first ask is a miss the edge stores; the sample scores the
    /// second ask, so it reads whether the edge kept each address, not the purge (ADR: One frame for
    /// every resource, the addendum on asking twice).
    /// </summary>
    [Fact]
    public async Task The_edge_sample_scores_the_second_ask_so_a_purge_does_not_read_as_a_broken_edge()
    {
        var cloud = new RecordedCloud { JustPurged = true };
        using var route = OutboundHttp.RouteTo(cloud);
        var edge = new NetlifyEdge("netlify-token", "theyard-edge", new Uri("https://stevenstout.biz"));
        var now = new DateTimeOffset(2026, 10, 20, 22, 0, 0, TimeSpan.Zero);

        var hour = await edge.HourAsync(now, CancellationToken.None);
        var minute = Assert.Single(await edge.MinutesAsync(now, "1.0.4.2", CancellationToken.None));

        Assert.Equal(100.0, minute.Values["cache_hit"]);
        Assert.DoesNotContain("stored", hour.Facts["cache_status"], StringComparison.Ordinal);
        foreach (string path in NetlifyEdge.Sampled)
        {
            Assert.Equal(2, cloud.Asked.Count(a => a == "https://stevenstout.biz" + path));
        }
    }
    // #endregion refusals

    // #region shapes
    /// <summary>Azure's answers with pieces missing: nothing is invented for what is not there, and nothing throws.</summary>
    [Fact]
    public void An_answer_with_pieces_missing_reads_as_less_and_never_throws()
    {
        using var noValue = JsonDocument.Parse("{}");
        using var odd = JsonDocument.Parse("""
            {"value":[
              {"name":{"value":"NotAsked"},"timeseries":[]},
              {"name":{"value":"CpuPercentage"},"timeseries":[{"data":[
                {"timeStamp":"2026-10-18T20:00:00Z","average":null},
                {"timeStamp":"2026-10-18T20:01:00Z","average":"high"},
                {"timeStamp":"not a time","average":3},
                {"timeStamp":"2026-10-18T20:02:00Z","average":4.5}]}]}]}
            """);
        using var events = JsonDocument.Parse("""
            {"value":[
              {},
              {"eventTimestamp":"2026-10-18T20:00:00Z"},
              {"eventTimestamp":"2026-10-18T20:00:00Z","operationName":{"value":"Microsoft.Web/serverfarms/write"},"status":{"value":"Failed"},"caller":" "},
              {"eventTimestamp":"2026-10-18T20:00:00Z","operationName":{"localizedValue":"x"},"status":{}},
              {"eventTimestamp":"2026-10-18T20:00:00Z","operationName":{"value":"Microsoft.ResourceHealth/events/action"},"status":{"value":"Active"},"caller":"azure"},
              {"eventTimestamp":"2026-10-18T20:00:00Z","operationName":{"value":"Microsoft.Web/sites/write"},"status":{"value":"Resolved"}}]}
            """);

        Assert.Empty(PlanShapes.Series(noValue.RootElement, PlanReader.PlanMetrics));
        Assert.Empty(PlanShapes.Events(noValue.RootElement, "plan"));
        var series = PlanShapes.Series(odd.RootElement, PlanReader.PlanMetrics);
        Assert.Equal([4.5], series["CpuPercentage"].Values);
        Assert.False(series.ContainsKey("NotAsked"));
        var read = PlanShapes.Events(events.RootElement, "plan");
        Assert.Equal(["other", "scale", "other", "maintenance", "config"], read.Select(e => e.Kind));
        Assert.Equal([false, false, false, true, false], read.Select(e => e.Planned));
    }

    [Fact]
    public void This_process_reads_its_own_machine_rather_than_a_table_of_sizes()
    {
        var machine = MachineSize.OfThisProcess();
        Assert.Equal(Environment.ProcessorCount, machine.Cores);
        Assert.True(machine.MemoryGb > 0);
    }

    [Theory]
    [InlineData("4294967296\n", null, 4.0)]
    [InlineData("max\n", "MemTotal:        3670016 kB\nMemFree:  1 kB\n", 3.5)]
    [InlineData(null, "MemTotal:        7340032 kB\n", 7.0)]
    [InlineData("max", null, null)]
    public void The_machine_s_memory_is_the_cgroup_limit_or_the_machine_total_never_the_heap_cap(string? cgroupMax, string? meminfo, double? expected) =>
        Assert.Equal(expected, MachineSize.MemoryGbOf(cgroupMax, meminfo));

    [Fact]
    public void A_token_that_is_not_a_token_has_no_tenant()
    {
        Assert.Null(PlanReader.TenantOf("not-a-token"));
        Assert.Null(PlanReader.TenantOf("a.!!!.c"));
        Assert.Null(PlanReader.TenantOf("a." + Convert.ToBase64String(Encoding.UTF8.GetBytes("not json")) + ".c"));
    }
    // #endregion shapes
}
