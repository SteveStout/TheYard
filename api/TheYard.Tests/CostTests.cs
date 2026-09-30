using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using TheYard.Api;
using TheYard.Application;

namespace TheYard.Tests;

/// <summary>
/// What Azure charges (ADR: What Azure charges): a fixed Cost Management answer
/// becomes the expected days with no subscription path left in them, the donut
/// folds everything past the fourth resource into Others, the newest day is
/// flagged while Azure is still adding to it, an absent reading is a sentence
/// and never a zero, the held read is replaced whole, and the reader's minute
/// of cache holds.
/// </summary>
public class CostTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Subscription = "/subscriptions/00000000-1111-2222-3333-444444444444/resourcegroups/rg-theyard-ss/providers";

    // #region cost-payload
    /// <summary>
    /// The shape Cost Management answers a daily query grouped by resource
    /// with: columns by name, a row per resource per day, the day as a number.
    /// The column order is deliberately not the order the code asks for.
    /// </summary>
    private static readonly string Payload = $$"""
        {
          "columns": [
            { "name": "UsageDate", "type": "Number" },
            { "name": "ResourceId", "type": "String" },
            { "name": "Cost", "type": "Number" },
            { "name": "Currency", "type": "String" }
          ],
          "rows": [
            [20260928, "{{Subscription}}/Microsoft.ContainerInstance/containerGroups/ACI-THEYARD-SS", 0.7325, "USD"],
            [20260928, "{{Subscription}}/Microsoft.Sql/servers/sql-theyard-ss/databases/theyard", 0.16, "USD"],
            [20260929, "{{Subscription}}/Microsoft.ContainerInstance/containerGroups/aci-theyard-ss", 0.5, "USD"],
            [20260929, "", 0.01, "USD"]
          ]
        }
        """;
    // #endregion cost-payload

    [Fact]
    public void A_cost_management_answer_becomes_days_with_a_name_and_a_type_and_no_path()
    {
        using var answer = JsonDocument.Parse(Payload);
        var days = CostQuery.DaysFrom(answer.RootElement);

        Assert.Equal(4, days.Count);
        Assert.Equal(new CostDay("2026-09-28", "aci-theyard-ss", "microsoft.containerinstance/containergroups", 0.7325, "USD"), days[0]);
        Assert.Contains(days, day => day is { Resource: "theyard", Type: "microsoft.sql/servers/databases", Day: "2026-09-28" });
        Assert.Contains(days, day => day is { Resource: "not tied to a resource", Type: "other", Day: "2026-09-29" });
        // The subscription's id is in every path Azure sends and in none of the days kept.
        Assert.DoesNotContain(days, day => (day.Resource + day.Type).Contains("subscriptions", StringComparison.Ordinal)
            || (day.Resource + day.Type).Contains("00000000", StringComparison.Ordinal));
    }

    [Fact]
    public void A_forecast_answer_keeps_each_day_marked_billed_or_forecast_by_Azure_itself()
    {
        using var answer = JsonDocument.Parse("""
            {
              "columns": [ { "name": "Cost" }, { "name": "UsageDate" }, { "name": "CostStatus" }, { "name": "Currency" } ],
              "rows": [ [1.5, "2026-09-29T00:00:00", "Actual", "USD"], [1.61, "2026-09-30T00:00:00", "Forecast", "USD"] ]
            }
            """);

        var forecast = CostQuery.ForecastFrom(answer.RootElement);

        Assert.Equal(new[] { new CostForecastDay("2026-09-29", 1.5, "USD", Billed: true), new CostForecastDay("2026-09-30", 1.61, "USD") }, forecast);
    }

    [Theory]
    [InlineData("/subscriptions/x/resourceGroups/rg/providers/Microsoft.Web/sites/theyard-ss", "theyard-ss", "microsoft.web/sites")]
    [InlineData("/subscriptions/x/resourceGroups/rg/providers/Microsoft.Sql/servers/s/databases/d", "d", "microsoft.sql/servers/databases")]
    [InlineData("", "not tied to a resource", "other")]
    public void A_resource_path_is_read_as_its_name_and_its_whole_type(string path, string name, string type)
    {
        Assert.Equal((name, type), CostQuery.ResourceOf(path));
    }

    // #region cost-view-tests
    [Fact]
    public void The_donut_names_four_resources_and_folds_the_rest_into_Others()
    {
        var days = new[] { 9.0, 5.0, 4.0, 3.0, 2.0, 1.0 }
            .Select((cost, i) => new CostDay("2026-09-29", $"r{i}", "microsoft.web/sites", cost, "USD"))
            .ToList();

        var slices = CostView.Slices(days);

        Assert.Equal(new[] { "r0", "r1", "r2", "r3", "Others" }, slices.Select(slice => slice.Name));
        Assert.Equal(3.0, slices[^1].Cost);
        Assert.Equal(2, slices[^1].Count);
        Assert.Equal(100.0, Math.Round(slices.Sum(slice => slice.Share), 0));
    }

    [Fact]
    public void The_newest_day_is_flagged_partial_and_a_quiet_day_before_it_is_a_zero_not_a_gap()
    {
        var now = new DateTimeOffset(2026, 9, 30, 15, 0, 0, TimeSpan.Zero);
        var kept = new CostKept(
            [
                new CostDay("2026-09-27", "aci-theyard-ss", "microsoft.containerinstance/containergroups", 1.0, "USD"),
                new CostDay("2026-09-29", "aci-theyard-ss", "microsoft.containerinstance/containergroups", 0.5, "USD"),
            ],
            // The forecast answer as Azure gives it: the billed days, then the days to come.
            [new CostForecastDay("2026-09-27", 1.0, "USD", Billed: true), new CostForecastDay("2026-09-29", 0.5, "USD", Billed: true), new CostForecastDay("2026-09-30", 0.25, "USD")]);

        var report = CostView.Build("7d", kept, now, new CostHistoryAvailability(true, "kept"), (now, true, null));

        Assert.True(report.Available);
        Assert.Equal("2026-09-29", report.NewestDay);
        Assert.True(report.NewestPartial);
        // The window's first day is 24 September and the newest reported is the 29th: six days, the 28th a zero.
        Assert.Equal(new[] { "2026-09-24", "2026-09-25", "2026-09-26", "2026-09-27", "2026-09-28", "2026-09-29" }, report.Days.Select(day => day.Day));
        Assert.Equal(0, report.Days.Single(day => day.Day == "2026-09-28").Cost);
        Assert.True(report.Days[^1].Partial);
        Assert.False(report.Days[^2].Partial);
        Assert.Equal(1.5, report.Days[^1].Total);
        Assert.Equal(1.5, report.MonthToDate);
        Assert.Equal(1.75, report.ForecastMonth);
        Assert.Equal(new[] { "2026-09-30" }, report.Forecast.Select(day => day.Day));
        Assert.Equal(1.75, report.Forecast[0].Total);
        Assert.Equal("Container instances", Assert.Single(report.Types).Label);
    }

    [Fact]
    public void An_absent_reading_says_why_in_a_sentence_and_draws_nothing()
    {
        var now = new DateTimeOffset(2026, 9, 30, 15, 0, 0, TimeSpan.Zero);

        var noStore = CostView.Build("30d", CostKept.Empty, now, new CostHistoryAvailability(false, "no document store"), (null, false, null));
        var noRole = CostView.Build("30d", CostKept.Empty, now, new CostHistoryAvailability(true, "kept"), (now, false, CostReader.NoRoleNote));
        var notYet = CostView.Build("24h", CostKept.Empty, now, new CostHistoryAvailability(true, "kept"), (null, false, null));

        Assert.Equal("no document store", noStore.Note);
        Assert.Equal(CostReader.NoRoleNote, noRole.Note);
        Assert.StartsWith("the first hour's read has not landed yet", notYet.Note);
        foreach (var report in new[] { noStore, noRole, notYet })
        {
            Assert.False(report.Available);
            Assert.Empty(report.Days);
            Assert.Empty(report.Resources);
            Assert.Null(report.ForecastMonth);
        }
    }
    // #endregion cost-view-tests

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, CostOutcome.NoRole)]
    [InlineData(HttpStatusCode.Unauthorized, CostOutcome.NoRole)]
    [InlineData(HttpStatusCode.TooManyRequests, CostOutcome.Refused)]
    [InlineData(HttpStatusCode.InternalServerError, CostOutcome.Failed)]
    public void A_refusal_is_named_for_what_it_means_and_never_quotes_the_scope(HttpStatusCode status, CostOutcome outcome)
    {
        Assert.Equal(outcome, CostReader.OutcomeOf(status));
        Assert.DoesNotContain("subscriptions", CostReader.NoteFor(status), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CostOutcome.Read, 60)]
    [InlineData(CostOutcome.NoRole, 60)]
    [InlineData(CostOutcome.Refused, 60)]
    [InlineData(CostOutcome.Failed, 5)]
    public void A_read_that_did_not_finish_is_tried_again_in_five_minutes_and_anything_else_in_an_hour(CostOutcome outcome, int minutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(minutes), CostRecorder.WaitAfter(outcome));
    }

    [Fact]
    public async Task The_reader_answers_a_minute_from_its_cache_and_reads_again_after()
    {
        var clock = new SteppedClock(new DateTimeOffset(2026, 9, 30, 15, 0, 0, TimeSpan.Zero));
        var history = new CountingHistory();
        var reader = new CostHistoryReader(history, new CostReader("sub", "client", configured: true), new CostStatus(), clock);

        await reader.ReadAsync("7d", CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(59));
        await reader.ReadAsync("7d", CancellationToken.None);
        Assert.Equal(1, history.Reads);

        clock.Advance(TimeSpan.FromSeconds(2));
        await reader.ReadAsync("7d", CancellationToken.None);
        Assert.Equal(2, history.Reads);
    }

    [Fact]
    public async Task Off_Azure_the_recorder_asks_nobody_and_the_card_says_why()
    {
        var clock = new SteppedClock(new DateTimeOffset(2026, 9, 30, 15, 0, 0, TimeSpan.Zero));
        var history = new CountingHistory();
        var status = new CostStatus();
        var reader = new CostReader("sub", "client", configured: false);

        using var recorder = new CostRecorder(reader, history, status, clock, NullLogger<CostRecorder>.Instance);
        await recorder.RecordOnceAsync(CancellationToken.None);
        var report = await new CostHistoryReader(history, reader, status, clock).ReadAsync("30d", CancellationToken.None);

        Assert.Equal(0, history.Keeps);
        Assert.Equal(0, history.Reads);
        Assert.Equal(CostReader.NotConfigured, status.Note);
        Assert.False(report.Available);
        Assert.Equal(CostReader.NotConfigured, report.Note);
    }

    [Fact]
    public async Task The_endpoint_answers_every_window_with_a_shape_and_a_reason_locally()
    {
        using var client = factory.CreateClient();
        foreach (string window in new[] { "24h", "7d", "30d", "a-year" })
        {
            var response = await client.GetAsync($"/api/admin/costs?window={window}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            // A window the list does not know is the month, never a year of reads.
            Assert.Equal(window == "a-year" ? "30d" : window, body.RootElement.GetProperty("window").GetString());
            Assert.False(body.RootElement.GetProperty("available").GetBoolean());
            Assert.Equal(CostReader.NotConfigured, body.RootElement.GetProperty("note").GetString());
            Assert.Equal(3, body.RootElement.GetProperty("windows").GetArrayLength());
        }
    }

    [Fact]
    public async Task The_held_read_is_replaced_whole_by_the_next_and_read_back_from_a_day()
    {
        var held = new CostsInMemory();
        await held.KeepAsync([new CostDay("2026-09-28", "a", "other", 1, "USD"), new CostDay("2026-09-29", "a", "other", 2, "USD")], [], CancellationToken.None);
        await held.KeepAsync([new CostDay("2026-09-29", "a", "other", 2.5, "USD")], [new CostForecastDay("2026-09-30", 1, "USD")], CancellationToken.None);

        var read = await held.ReadAsync("2026-09-29", CancellationToken.None);

        // A day Azure revised is the revised figure, and a day the newer read no longer carries is gone with the older read.
        Assert.Equal(2.5, Assert.Single(read.Days).Cost);
        Assert.Single(read.Forecast);
    }

    /// <summary>A clock the test moves by hand.</summary>
    private sealed class SteppedClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public void Advance(TimeSpan by) => _now += by;

        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>A store that keeps nothing and counts how often it is asked.</summary>
    private sealed class CountingHistory : ICostHistory
    {
        public int Reads { get; private set; }

        public int Keeps { get; private set; }

        public Task<CostHistoryAvailability> AvailabilityAsync(CancellationToken cancellation) =>
            Task.FromResult(new CostHistoryAvailability(true, "kept in a test"));

        public Task KeepAsync(IReadOnlyList<CostDay> days, IReadOnlyList<CostForecastDay> forecast, CancellationToken cancellation)
        {
            Keeps++;
            return Task.CompletedTask;
        }

        public Task<CostKept> ReadAsync(string fromDay, CancellationToken cancellation)
        {
            Reads++;
            return Task.FromResult(CostKept.Empty);
        }
    }
}
