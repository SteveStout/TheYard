using Microsoft.Extensions.Logging.Abstractions;
using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The recorder writes a kept minute only when it reads differently from the one it kept (ADR: One frame for every
/// resource, the addendum on writing a minute once). Each read goes five minutes back so a minute Azure was late with
/// still lands, and writing all five again every minute for every card was the Cosmos DB account's busiest work: on
/// 16 November its normalized peak sat at 100 % in 358 of 360 minutes, and the container these minutes go to took
/// 4,835 of the day's 10,115 throttled requests.
/// </summary>
public sealed class CloudRecorderWritesTests
{
    private static ResourceCard Card(Func<IReadOnlyList<ResourceMinute>> minutes) => new(
        "insights", "insights", "appi-test", ["ingest_today_gb"],
        (_, _, _, _) => Task.FromResult(minutes()),
        (hour, _, _) => Task.FromResult(new ResourceHour(hour, "/subscriptions/s/resourceGroups/g/providers/T/x", "tenant", new Dictionary<string, string?>(), [])),
        inputs => InsightsCard.View(inputs));

    [Fact]
    public async Task A_minute_read_again_unchanged_is_not_written_again_and_a_changed_or_late_one_is()
    {
        var now = DateTimeOffset.UtcNow;
        var start = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, TimeSpan.Zero);
        var values = Enumerable.Range(1, 5).ToDictionary(i => start.AddMinutes(-i), i => (double?)i);
        IReadOnlyList<ResourceMinute> Read() => [.. values.Select(entry => new ResourceMinute(entry.Key, new Dictionary<string, double?> { ["ingest_today_gb"] = entry.Value }, "1.0.5.0"))];
        var keep = new MemoryResourceHistory("kept in the test");
        var recorder = new CloudRecorder([(Card(Read), keep)], new BuildInfo("1.0.5.0", "test"), TimeProvider.System, NullLogger<CloudRecorder>.Instance);

        await recorder.RecordOnceAsync("insights", CancellationToken.None);
        Assert.Equal(5, recorder.MinutesWritten);

        await recorder.RecordOnceAsync("insights", CancellationToken.None);
        Assert.Equal(5, recorder.MinutesWritten);

        // Azure filled in a minute it was late with, and a new minute arrived: both are written, nothing else.
        values[start.AddMinutes(-2)] = 2.5;
        values[start] = 6;
        await recorder.RecordOnceAsync("insights", CancellationToken.None);
        Assert.Equal(7, recorder.MinutesWritten);
        Assert.Equal(2.5, (await keep.NewestAsync(60, CancellationToken.None)).Single(m => m.At == start.AddMinutes(-2)).Values["ingest_today_gb"]);
    }

    [Fact]
    public void The_fingerprint_is_the_same_reading_whatever_order_its_values_came_in()
    {
        var at = new DateTimeOffset(2026, 11, 16, 15, 0, 0, TimeSpan.Zero);
        var one = new ResourceMinute(at, new Dictionary<string, double?> { ["a"] = 1, ["b"] = null }, "v");
        var two = new ResourceMinute(at, new Dictionary<string, double?> { ["b"] = null, ["a"] = 1 }, "v");

        Assert.Equal(CloudRecorder.Fingerprint(one), CloudRecorder.Fingerprint(two));
        Assert.NotEqual(CloudRecorder.Fingerprint(one), CloudRecorder.Fingerprint(one with { Version = "w" }));
        Assert.NotEqual(CloudRecorder.Fingerprint(one), CloudRecorder.Fingerprint(new ResourceMinute(at, new Dictionary<string, double?> { ["a"] = 1, ["b"] = 0 }, "v")));
    }
}
