using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace TheYard.Tests;

/// <summary>
/// The first screen's one reading (ADR: One reading for the first screen). The figure it computes
/// itself is the usual five minutes at the ninety-fifth percentile, so that is what these hold,
/// against buckets written by hand, plus the route answering in the shape the band reads.
/// </summary>
public class PulseTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    /// <summary>A five-minute bucket that answered <paramref name="requests"/> requests at <paramref name="p95"/> milliseconds.</summary>
    private static MachineBucket Bucket(int requests, double? p95) =>
        new(DateTimeOffset.UnixEpoch, 5, 1024, 200, 220, 120, null, null, null, null, null, 0, 0, requests, null, p95);

    [Fact]
    public void The_usual_five_minutes_is_the_middle_one_by_request_count()
    {
        // Two hundred requests at 30 ms and ten at 900: the usual five minutes is 30, not the
        // average of the two and not the worst.
        var buckets = new[] { Bucket(200, 30), Bucket(10, 900) };
        Assert.Equal(30, PulseReader.UsualP95(buckets));
    }

    [Fact]
    public void A_quiet_five_minutes_does_not_weigh_the_same_as_a_busy_one()
    {
        // The quiet bucket is the slowest and would be the middle one unweighted; weighted by
        // requests the answer is the busy one.
        var buckets = new[] { Bucket(1, 4), Bucket(400, 25), Bucket(2, 800) };
        Assert.Equal(25, PulseReader.UsualP95(buckets));
    }

    [Fact]
    public void A_day_with_nothing_in_it_reads_null_rather_than_zero()
    {
        Assert.Null(PulseReader.UsualP95([]));
        Assert.Null(PulseReader.UsualP95([Bucket(0, 12), Bucket(0, null)]));
        Assert.Null(PulseReader.UsualP95([Bucket(40, null)]));
    }

    [Fact]
    public void The_figure_is_rounded_to_a_whole_millisecond()
    {
        Assert.Equal(31, PulseReader.UsualP95([Bucket(10, 30.5)]));
        Assert.Equal(12, PulseReader.UsualP95([Bucket(10, 12.4)]));
    }

    /// <summary>
    /// The route answers the shape the band reads, in the wire's own spelling, and says nothing
    /// about any visitor. On the test host there is no document store and no Azure identity, so the
    /// readings are null: the point of the test is that the shape and the nulls both arrive.
    /// </summary>
    [Fact]
    public async Task The_route_answers_every_field_the_band_reads()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/pulse");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        foreach (string field in new[]
        {
            "usual_p95_ms_24h", "requests_24h", "month_to_date", "forecast_month", "currency",
            "cost_read_at_ms", "uptime_seconds", "version", "commit", "read_at_ms",
            "people_all_time", "people_new_by_day", "counted_from", "visits_all_time", "visits_from", "visits_by_day",
        })
        {
            Assert.True(root.TryGetProperty(field, out _), $"/api/pulse should answer {field}");
        }

        Assert.Equal(16, root.EnumerateObject().Count());
        Assert.True(root.GetProperty("uptime_seconds").GetInt64() >= 0);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("version").GetString()));
    }
}
