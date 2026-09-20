using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using TheYard.Api;
using TheYard.Application;
using TheYard.Infrastructure.Cosmos;

namespace TheYard.Tests;

/// <summary>
/// What the machines were doing, kept (ADR: What the machines are doing, the
/// addendum on the windows): a minute folded out of the rings, minutes folded
/// into the buckets a window is drawn in, the document a minute becomes, and
/// the endpoint that says which window it answered with. A figure nobody read
/// stays absent all the way through, which is most of what these hold.
/// </summary>
public class MachineHistoryTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset Nine = new(2026, 9, 20, 9, 7, 0, TimeSpan.Zero);

    private static MachineSample Sample(DateTimeOffset at, double workingSet, double managed, double? cpu) =>
        new(at, workingSet, managed, managed, cpu, 20, 1, 0);

    // #region windows-and-buckets
    [Theory]
    [InlineData("24h", MachineGrain.FiveMinutes, 24)]
    [InlineData("7d", MachineGrain.Hour, 24 * 7)]
    [InlineData("30d", MachineGrain.FourHours, 24 * 30)]
    public void A_kept_window_names_its_length_and_the_bucket_it_is_drawn_in(string window, MachineGrain grain, int hours)
    {
        var chosen = MachineWindows.Parse(window);

        Assert.NotNull(chosen);
        Assert.Equal(TimeSpan.FromHours(hours), chosen.Value.Length);
        Assert.Equal(grain, chosen.Value.Grain);
        Assert.Equal(window, chosen.Value.Name);
    }

    [Theory]
    [InlineData("1h")]
    [InlineData(null)]
    [InlineData("a fortnight")]
    public void The_hour_and_anything_unrecognised_are_not_kept_windows(string? window) =>
        Assert.Null(MachineWindows.Parse(window));

    [Fact]
    public void A_minute_falls_in_one_bucket_of_each_size_and_the_keys_sort_as_text()
    {
        var at = new DateTimeOffset(2026, 9, 20, 9, 7, 42, TimeSpan.Zero);

        Assert.Equal("2026-09-20T09:05", MachineWindows.KeyOf(at, MachineGrain.FiveMinutes));
        Assert.Equal("2026-09-20T09:00", MachineWindows.KeyOf(at, MachineGrain.Hour));
        Assert.Equal("2026-09-20T08:00", MachineWindows.KeyOf(at, MachineGrain.FourHours));
        Assert.Equal("2026-09-20", MachineWindows.DayOf(at));
        // An offset is a way of writing the same instant, not a different bucket.
        Assert.Equal("2026-09-20T09:05", MachineWindows.KeyOf(at.ToOffset(TimeSpan.FromHours(-5)), MachineGrain.FiveMinutes));
    }
    // #endregion windows-and-buckets

    // #region fold-a-minute
    [Fact]
    public void A_minute_is_folded_from_the_samples_rows_and_operations_inside_it_and_nothing_outside()
    {
        var samples = new List<MachineSample>
        {
            Sample(Nine.AddSeconds(-5), 900, 800, 90),
            Sample(Nine.AddSeconds(10), 300, 120, null),
            Sample(Nine.AddSeconds(25), 320, 130, 10),
            Sample(Nine.AddSeconds(40), 340, 140, 30),
            Sample(Nine.AddSeconds(60), 900, 800, 90),
        };
        var rows = new List<ResourceStatRow>
        {
            // The view's end_time arrives with no kind on it, and is UTC.
            new(new DateTime(2026, 9, 20, 9, 7, 15, DateTimeKind.Unspecified), 2, 1, 0, 40, 3),
            new(new DateTime(2026, 9, 20, 9, 7, 45, DateTimeKind.Unspecified), 4, 3, 0, 44, 3),
            new(new DateTime(2026, 9, 20, 9, 8, 0, DateTimeKind.Unspecified), 90, 90, 0, 90, 90),
        };
        var operations = new List<StoreOperation>
        {
            Operation(Nine.AddSeconds(5), 2.5),
            Operation(Nine.AddSeconds(50), 3.5),
            Operation(Nine.AddSeconds(61), 100),
        };

        var requests = new List<RequestEntry>
        {
            new(Nine.AddSeconds(-1), "GET", "/api/vehicles", 500, 9000),
            new(Nine.AddSeconds(3), "GET", "/api/vehicles", 200, 40),
            new(Nine.AddSeconds(20), "GET", "/api/facets", 200, 2),
            new(Nine.AddSeconds(30), "POST", "/api/vehicles/abc/bids", 409, 30),
            new(Nine.AddSeconds(59), "GET", "/api/vehicles/abc", 500, 120),
        };

        var minute = MachineRecorder.Fold(Nine.AddSeconds(30), "sql", 1185.6, samples, rows, operations, requests);

        Assert.NotNull(minute);
        Assert.Equal(Nine, minute.At);
        Assert.Equal("sql", minute.Site);
        Assert.Equal(1185.6, minute.MemoryLimitMb);
        Assert.Equal(320, minute.WorkingSetMb);
        Assert.Equal(340, minute.WorkingSetMaxMb);
        Assert.Equal(130, minute.ManagedMb);
        // Two of the three samples carry a processor share; the mean is of those two.
        Assert.Equal(20, minute.CpuPercent);
        Assert.Equal(30, minute.CpuMaxPercent);
        Assert.Equal(3, minute.SqlCpuPercent);
        Assert.Equal(42, minute.SqlMemoryPercent);
        Assert.Equal(2, minute.SqlDataIoPercent);
        Assert.Equal(6, minute.RequestUnits);
        Assert.Equal(2, minute.Operations);
        // Four requests inside the minute: nearest rank puts the median on the
        // second of them and the ninety-fifth on the slowest.
        Assert.Equal(4, minute.Requests);
        Assert.Equal(30, minute.P50Ms);
        Assert.Equal(120, minute.P95Ms);
        Assert.Equal(1, minute.ServerErrors);
        Assert.Equal(1, minute.ClientErrors);
    }

    [Fact]
    public void A_minute_with_no_sample_in_it_is_not_kept_and_one_with_no_store_readings_keeps_them_absent()
    {
        Assert.Null(MachineRecorder.Fold(Nine, "sql", 1000, [Sample(Nine.AddMinutes(3), 300, 100, 5)], [], []));

        var minute = MachineRecorder.Fold(Nine, "cosmos", 1000, [Sample(Nine.AddSeconds(1), 300, 100, null)], [], []);

        Assert.NotNull(minute);
        Assert.Null(minute.CpuPercent);
        Assert.Null(minute.CpuMaxPercent);
        Assert.Null(minute.SqlCpuPercent);
        Assert.Null(minute.SqlMemoryPercent);
        Assert.Equal(0, minute.RequestUnits);
        Assert.Equal(0, minute.Operations);
        // Nobody asked for anything, so there is no median to keep: absent, not zero.
        Assert.Equal(0, minute.Requests);
        Assert.Null(minute.P50Ms);
        Assert.Null(minute.P95Ms);
    }
    // #endregion fold-a-minute

    [Fact]
    public void Minutes_fold_into_buckets_oldest_first_averaging_what_was_read_and_adding_what_was_charged()
    {
        var minutes = new List<MachineMinute>
        {
            new(Nine.AddMinutes(4), "sql", 1000, 330, 350, 140, 30, 40, null, null, null, 4, 2),
            new(Nine, "sql", 1000, 300, 310, 120, null, null, 2, 40, 1, 1, 1, 10, 20, 200, 1, 2),
            new(Nine.AddMinutes(1), "sql", 1000, 320, 345, 130, 10, 15, 4, 44, 3, 2.5, 3, 30, 40, 90, 0, 1),
        };

        var buckets = MachineFolding.Buckets(minutes, MachineGrain.FiveMinutes);

        Assert.Equal(2, buckets.Count);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 9, 5, 0, TimeSpan.Zero), buckets[0].At);
        Assert.Equal(2, buckets[0].Minutes);
        Assert.Equal(310, buckets[0].WorkingSetMb);
        Assert.Equal(345, buckets[0].WorkingSetMaxMb);
        Assert.Equal(125, buckets[0].ManagedMb);
        // One of the two minutes read a processor share: the average is that one, not half of it.
        Assert.Equal(10, buckets[0].CpuPercent);
        Assert.Equal(15, buckets[0].CpuMaxPercent);
        Assert.Equal(3, buckets[0].SqlCpuPercent);
        Assert.Equal(3.5, buckets[0].RequestUnits);
        Assert.Equal(4, buckets[0].Operations);
        // Requests and errors add up; the typical answer is the mean of the
        // minutes' medians, and the slow one is the worst minute's.
        Assert.Equal(40, buckets[0].Requests);
        Assert.Equal(30, buckets[0].P50Ms);
        Assert.Equal(200, buckets[0].P95Ms);
        Assert.Equal(1, buckets[0].ServerErrors);
        Assert.Equal(3, buckets[0].ClientErrors);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 9, 10, 0, TimeSpan.Zero), buckets[1].At);
        Assert.Null(buckets[1].SqlCpuPercent);
        Assert.Equal(0, buckets[1].Requests);
        Assert.Null(buckets[1].P50Ms);
    }

    // #region minute-document
    [Fact]
    public void A_minute_becomes_a_document_keyed_by_site_and_minute_and_a_figure_nobody_read_is_left_out_of_it()
    {
        var minute = new MachineMinute(Nine, "cosmos", 1185.6, 300, 310, 120, null, null, null, null, null, 1.5, 1);

        var document = MachineMinuteDocument.From(minute);
        string json = JsonSerializer.Serialize(document, CosmosStore.Json);

        Assert.Equal("cosmos:2026-09-20T09:07", document.Id);
        Assert.Equal("2026-09-20", document.Day);
        Assert.Equal("2026-09-20T09:05", document.B5);
        Assert.Equal("2026-09-20T09:00", document.H1);
        Assert.Equal("2026-09-20T08:00", document.H4);
        Assert.Contains("\"ws_mb\":300", json, StringComparison.Ordinal);
        Assert.Contains("\"ru\":1.5", json, StringComparison.Ordinal);
        Assert.Contains("\"errors_5xx\":0", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"p50_ms\"", json, StringComparison.Ordinal);
        // Absent, not null: an average in the store skips what is absent, and
        // a null would make it answer nothing at all.
        Assert.DoesNotContain("\"cpu\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"sql_cpu\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("null", json, StringComparison.Ordinal);
    }
    // #endregion minute-document

    [Fact]
    public async Task A_kept_window_is_asked_of_the_store_once_a_minute_however_often_the_card_asks()
    {
        var history = new CountingHistory();
        var reader = new MachineHistoryReader(history, "sql");

        await reader.ReadAsync("7d", Nine, CancellationToken.None);
        await reader.ReadAsync("7d", Nine.AddSeconds(30), CancellationToken.None);
        Assert.Equal(1, history.Queries);

        await reader.ReadAsync("7d", Nine.AddSeconds(61), CancellationToken.None);
        await reader.ReadAsync("30d", Nine.AddSeconds(61), CancellationToken.None);
        Assert.Equal(3, history.Queries);
        Assert.Equal(MachineGrain.FourHours, history.LastGrain);
        Assert.Equal(Nine.AddSeconds(61) - TimeSpan.FromDays(30), history.LastSince);

        // The hour is this process's own memory and never reaches the store.
        await reader.ReadAsync("1h", Nine.AddMinutes(5), CancellationToken.None);
        Assert.Equal(3, history.Queries);
    }

    /// <summary>
    /// The suite runs with no document store, which is a developer's machine:
    /// the hour is answered as always, and a kept window says it is not kept
    /// here and why, rather than drawing an empty chart.
    /// </summary>
    [Fact]
    public async Task The_endpoint_says_which_window_it_answered_and_a_kept_window_with_no_store_says_so()
    {
        var client = factory.CreateClient();

        var hour = await client.GetAsync("/api/admin/machines");
        Assert.Equal(HttpStatusCode.OK, hour.StatusCode);
        using var hourJson = JsonDocument.Parse(await hour.Content.ReadAsStringAsync());
        Assert.Equal("1h", hourJson.RootElement.GetProperty("history").GetProperty("window").GetString());
        Assert.False(hourJson.RootElement.GetProperty("history").GetProperty("kept").GetBoolean());
        Assert.Equal(
            MachineWindows.Names,
            hourJson.RootElement.GetProperty("windows").EnumerateArray().Select(name => name.GetString()!).ToList());
        Assert.True(hourJson.RootElement.GetProperty("container").TryGetProperty("samples", out _));

        var day = await client.GetAsync("/api/admin/machines?window=24h");
        Assert.Equal(HttpStatusCode.OK, day.StatusCode);
        using var dayJson = JsonDocument.Parse(await day.Content.ReadAsStringAsync());
        var history = dayJson.RootElement.GetProperty("history");
        Assert.Equal("24h", history.GetProperty("window").GetString());
        Assert.True(history.GetProperty("kept").GetBoolean());
        Assert.Equal(5, history.GetProperty("bucket_minutes").GetInt32());
        if (Environment.GetEnvironmentVariable("Cosmos__AccountEndpoint") is null)
        {
            Assert.False(history.GetProperty("available").GetBoolean());
            Assert.False(string.IsNullOrWhiteSpace(history.GetProperty("note").GetString()));
            Assert.Empty(history.GetProperty("buckets").EnumerateArray());
        }
    }

    private static StoreOperation Operation(DateTimeOffset at, double charge) =>
        new(at, "bids", StoreOperationKind.PointWrite, "upsert", [], "one", 1, charge, 4, "ok", null, null);

    private sealed class CountingHistory : IMachineHistory
    {
        public int Queries { get; private set; }

        public MachineGrain LastGrain { get; private set; }

        public DateTimeOffset LastSince { get; private set; }

        public Task<MachineHistoryAvailability> AvailabilityAsync(CancellationToken cancellation) =>
            Task.FromResult(new MachineHistoryAvailability(true, "kept by the test"));

        public Task AppendAsync(MachineMinute minute, CancellationToken cancellation) => Task.CompletedTask;

        public Task<IReadOnlyList<MachineBucket>> QueryAsync(string site, DateTimeOffset since, MachineGrain grain, CancellationToken cancellation)
        {
            Queries++;
            LastGrain = grain;
            LastSince = since;
            return Task.FromResult<IReadOnlyList<MachineBucket>>([]);
        }
    }
}

/// <summary>
/// The grouped query, against the real account's test container. The folding
/// above is what a window should come back as; this holds the document store
/// to it, because the two things it leans on are the store's own behaviour and
/// not this repository's: that an average skips a figure a document does not
/// carry, and that a GROUP BY over a bucket key answers one row a bucket.
/// </summary>
[Trait(CosmosLive.Trait, CosmosLive.Value)]
public class MachineHistoryLiveTests
{
    // #region live-window
    [Fact]
    public async Task A_window_read_back_from_the_store_is_the_window_the_folding_says_it_should_be()
    {
        var (store, _) = CosmosLive.Open();
        var history = new CosmosMachineHistory(store);
        var availability = await history.AvailabilityAsync(CancellationToken.None);
        Assert.True(availability.Available, availability.Reason);

        // A site name nobody else writes under, so a run reads only its own minutes.
        string site = $"test-{Guid.NewGuid():N}";
        var start = MachineFolding.MinuteOf(DateTimeOffset.UtcNow).AddMinutes(-30);
        start = start.AddMinutes(-(start.Minute % 5));
        var minutes = new List<MachineMinute>
        {
            new(start, site, 1000, 300, 310, 120, null, null, 2, 40, 1, 1, 1, 10, 20, 200, 1, 2),
            new(start.AddMinutes(1), site, 1000, 320, 345, 130, 10, 15, 4, 44, 3, 2.5, 3, 30, 40, 90, 0, 1),
            new(start.AddMinutes(6), site, 1000, 330, 350, 140, 30, 40, null, null, null, 4, 2),
        };
        foreach (var minute in minutes)
        {
            await history.AppendAsync(minute, CancellationToken.None);
        }

        var kept = await history.QueryAsync(site, start.AddMinutes(-1), MachineGrain.FiveMinutes, CancellationToken.None);

        Assert.Equal(MachineFolding.Buckets(minutes, MachineGrain.FiveMinutes), kept);
        Assert.Equal(0, history.Cost.Failures);
    }
    // #endregion live-window
}
