using Microsoft.Extensions.Logging.Abstractions;
using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// A live read that times out is the recorder's gap, never the card route's 500 (ADR: One frame for every resource,
/// the addendum on a read that times out). An HttpClient that gives up throws TaskCanceledException, which is an
/// OperationCanceledException though nobody cancelled the request, and the route let every one of those through, so a
/// timeout reached the Admin tab as a 500. On 25 October at 11:47 CDT Azure Monitor timed out 58 times and the route
/// answered one 500 in the same minute. Only the request's own cancellation passes now.
/// </summary>
public sealed class CloudTimeoutTests
{
    private static TaskCanceledException Timeout() =>
        new("The request was canceled due to the configured HttpClient.Timeout of 30 seconds elapsing.", new TimeoutException("The operation was canceled."));

    private static ResourceCard Card(bool hourly, Func<int> minutesAsked) => new(
        "insights", "insights", "appi-test", ["ingest_today_gb"],
        (_, _, _, _) => { minutesAsked(); throw Timeout(); },
        (_, _, _) => throw Timeout(),
        inputs => InsightsCard.View(inputs),
        Hourly: hourly);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_card_whose_live_read_times_out_answers_from_its_keep(bool hourly)
    {
        int asked = 0;
        var card = Card(hourly, () => ++asked);
        var keep = new MemoryResourceHistory("kept in the test");
        var recorder = new CloudRecorder([(card, keep)], new BuildInfo("1.0.4.87", "test"), TimeProvider.System, NullLogger<CloudRecorder>.Instance);
        var reader = new CloudReader([(card, keep)], recorder, (_, _) => Task.FromResult<double?>(null), TimeProvider.System);

        var report = await reader.ReadAsync("insights", "24h", CancellationToken.None);

        Assert.NotNull(report);
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task A_read_the_request_itself_cancelled_still_stops()
    {
        var card = Card(false, () => 0);
        var keep = new MemoryResourceHistory("kept in the test");
        var recorder = new CloudRecorder([(card, keep)], new BuildInfo("1.0.4.87", "test"), TimeProvider.System, NullLogger<CloudRecorder>.Instance);
        var reader = new CloudReader([(card, keep)], recorder, (_, _) => Task.FromResult<double?>(null), TimeProvider.System);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync("insights", "24h", cancelled.Token));
    }

    [Fact]
    public async Task An_hourly_card_whose_hour_times_out_still_keeps_its_minutes_and_says_which_part_failed()
    {
        int asked = 0;
        var card = new ResourceCard("insights", "insights", "appi-test", ["ingest_today_gb"],
            (_, _, _, _) => { asked++; return Task.FromResult<IReadOnlyList<ResourceMinute>>([]); },
            (_, _, _) => throw Timeout(),
            inputs => InsightsCard.View(inputs),
            Hourly: true);
        var keep = new MemoryResourceHistory("kept in the test");
        var recorder = new CloudRecorder([(card, keep)], new BuildInfo("1.0.4.87", "test"), TimeProvider.System, NullLogger<CloudRecorder>.Instance);

        var failed = await Assert.ThrowsAsync<TaskCanceledException>(() => recorder.RecordOnceAsync("insights", CancellationToken.None));

        Assert.Equal(1, asked);
        Assert.Equal("hour", CloudRecorder.PartOf(failed));
    }
}
