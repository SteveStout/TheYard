using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

/// <summary>
/// Memory follows traffic (ADR: Memory follows traffic): the processor rises with the requests,
/// memory stays roughly where it is, and memory that climbs while traffic is flat is red. The two
/// real windows below are the SQL site's own kept readings, four hours a point, read off
/// /api/admin/machines?window=30d on 18 October 2026: the week the hundred thousand vehicles moved
/// into each process, and a busy week in September when memory rose with the traffic and came back.
/// </summary>
public class MemoryTrafficTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>The limit those weeks ran under: the runtime's own reading of the B2 share, before the 30 per cent heap limit.</summary>
    private const double LimitThen = 2466.1;

    /// <summary>
    /// 6 to 11 October, every four hours: working set in MB, process CPU in percent, requests in
    /// the four hours. On the 8th the expansion of a hundred thousand vehicles moved into the
    /// process, the working set went from about 400 MB to about 650 and stayed there, and the
    /// traffic fell back to a few hundred requests a bucket. The App Service plan card found it
    /// ten days later at 79 per cent of the plan (ADR-101).
    /// </summary>
    private static readonly (string At, double Mb, double Cpu, int Requests)[] CatalogueWeek =
    [
        ("10-06T00", 403, 0.7, 77), ("10-06T04", 406, 0.7, 66), ("10-06T08", 406, 0.8, 72), ("10-06T12", 409, 2.6, 1691), ("10-06T16", 400, 2.2, 1452), ("10-06T20", 408, 1.3, 739),
        ("10-07T00", 419, 0.7, 73), ("10-07T04", 421, 0.7, 131), ("10-07T08", 413, 0.9, 71), ("10-07T12", 404, 1.7, 1287), ("10-07T16", 400, 2.0, 3138), ("10-07T20", 403, 1.6, 4978),
        ("10-08T00", 394, 2.3, 6663), ("10-08T04", 390, 3.5, 8874), ("10-08T08", 412, 0.8, 80), ("10-08T12", 406, 2.9, 5755), ("10-08T16", 586, 1.8, 19563), ("10-08T20", 672, 0.3, 440),
        ("10-09T00", 642, 0.3, 408), ("10-09T04", 621, 0.3, 278), ("10-09T08", 609, 0.4, 310), ("10-09T12", 622, 0.4, 369), ("10-09T16", 639, 0.9, 9833), ("10-09T20", 687, 1.4, 17294),
        ("10-10T00", 678, 1.1, 7878), ("10-10T04", 698, 0.3, 291), ("10-10T08", 640, 0.4, 270), ("10-10T12", 657, 0.9, 8979), ("10-10T16", 677, 1.6, 13428), ("10-10T20", 728, 0.3, 281),
        ("10-11T00", 689, 0.3, 329), ("10-11T04", 664, 0.3, 266), ("10-11T08", 677, 0.4, 290), ("10-11T12", 668, 0.4, 398), ("10-11T16", 665, 0.3, 260), ("10-11T20", 631, 0.3, 279),
    ];

    /// <summary>24 to 26 September, the same readings: a load test lifted the traffic forty times, memory went with it and came back down, and the processor followed the requests.</summary>
    private static readonly (string At, double Mb, double Cpu, int Requests)[] BusyWeek =
    [
        ("09-24T00", 117, 0.4, 329), ("09-24T04", 148, 0.4, 403), ("09-24T08", 111, 0.5, 345), ("09-24T12", 275, 2.5, 2837), ("09-24T16", 347, 6.2, 5517), ("09-24T20", 342, 3.5, 9049),
        ("09-25T00", 317, 3.3, 9881), ("09-25T04", 288, 3.2, 12471), ("09-25T08", 184, 0.6, 347), ("09-25T12", 339, 3.6, 14562), ("09-25T16", 326, 3.1, 14207), ("09-25T20", 330, 2.8, 13020),
        ("09-26T00", 206, 0.9, 433), ("09-26T04", 127, 0.3, 107), ("09-26T08", 111, 0.4, 70), ("09-26T12", 145, 0.4, 65), ("09-26T16", 139, 0.3, 183), ("09-26T20", 217, 0.2, 79),
    ];

    private static List<TrafficPoint> Points((string At, double Mb, double Cpu, int Requests)[] rows) =>
        [.. rows.Select(row => new TrafficPoint(
            DateTimeOffset.ParseExact($"2026-{row.At}", "yyyy-MM-dd'T'HH", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal),
            row.Requests / 240d,
            row.Cpu,
            row.Mb))];

    // #region catalogue-week
    /// <summary>The card would have flagged the catalogue in each process (ADR-101) the week it arrived: memory climbed while traffic fell.</summary>
    [Fact]
    public void The_week_the_catalogue_moved_into_the_process_reads_red()
    {
        var verdict = MemoryTraffic.Judge(Points(CatalogueWeek), LimitThen);

        Assert.Equal(PlanTone.Alarm, verdict.Tone);
        Assert.True(verdict.MemoryToMb - verdict.MemoryFromMb >= verdict.ClimbThresholdMb);
        Assert.True(verdict.RequestsTo <= verdict.RequestsFrom);
        Assert.Contains("a leak or a cache with no bound", verdict.Words, StringComparison.Ordinal);
    }
    // #endregion catalogue-week

    /// <summary>A busy week is not a leak: memory went up with the traffic and came back, and the processor followed the requests closely.</summary>
    [Fact]
    public void A_busy_week_where_memory_came_back_down_reads_green_and_the_processor_follows_traffic()
    {
        var verdict = MemoryTraffic.Judge(Points(BusyWeek), LimitThen);

        Assert.Equal(PlanTone.Good, verdict.Tone);
        Assert.True(verdict.CpuFollowsTraffic > 0.7, $"the processor should follow the requests; it read {verdict.CpuFollowsTraffic}");
    }

    [Fact]
    public void Memory_that_rises_with_traffic_is_watched_but_not_red()
    {
        var at = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
        var points = Enumerable.Range(0, 12).Select(i => new TrafficPoint(at.AddMinutes(i), 10 + i * 20, 1 + i, 300 + i * 20)).ToList();

        var verdict = MemoryTraffic.Judge(points, 1_000);

        Assert.Equal(PlanTone.Good, verdict.Tone);
        Assert.Contains("as traffic rose", verdict.Words, StringComparison.Ordinal);
    }

    [Fact]
    public void Steady_memory_under_steady_traffic_reads_green_and_too_few_points_are_not_judged()
    {
        var at = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
        var steady = Enumerable.Range(0, 30).Select(i => new TrafficPoint(at.AddMinutes(i), 40 + (i % 3), 2, 400 + (i % 5))).ToList();

        Assert.Equal(PlanTone.Good, MemoryTraffic.Judge(steady, 1_000).Tone);
        Assert.Equal(PlanTone.Info, MemoryTraffic.Judge(steady.Take(MemoryTraffic.FewestPoints - 1).ToList(), 1_000).Tone);
        Assert.Equal(50, MemoryTraffic.Judge(steady, 900).ClimbThresholdMb);
        Assert.Equal(100, MemoryTraffic.Judge(steady, 2_000).ClimbThresholdMb);
    }

    [Fact]
    public void Memory_climbing_a_megabyte_a_minute_at_flat_traffic_reads_red()
    {
        var at = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.Zero);
        var leaking = Enumerable.Range(0, 60).Select(i => new TrafficPoint(at.AddMinutes(i), 30, 2, 300 + i * 2)).ToList();

        var verdict = MemoryTraffic.Judge(leaking, 986);

        Assert.Equal(PlanTone.Alarm, verdict.Tone);
        Assert.Null(verdict.CpuFollowsTraffic);
    }

    /// <summary>The endpoint answers every window with the wire the card reads, and an unknown window as the hour.</summary>
    [Theory]
    [InlineData("1h", "1h")]
    [InlineData("24h", "24h")]
    [InlineData("nonsense", "1h")]
    public async Task The_endpoint_answers_the_window_its_points_and_a_verdict(string asked, string answered)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/admin/follows?window={asked}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = json.RootElement;
        Assert.Equal(answered, root.GetProperty("window").GetString());
        Assert.True(root.GetProperty("memory_limit_mb").GetDouble() > 0);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("points").ValueKind);
        string tone = root.GetProperty("verdict").GetProperty("tone").GetString()!;
        Assert.Contains(tone, new[] { "good", "info", "alarm" });
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("verdict").GetProperty("words").GetString()));
    }
}
