using System.Text.Json;

namespace JobHunt.Server.Tests;

/// <summary>
/// A 6 AM lane's usage post carries the lane's exit code (ADR-112): the Claude card reads a run that failed from it, and
/// a post without one is information, never a failure.
/// </summary>
public sealed class UsageExitTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 12, 12, 5, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, "0")]
    [InlineData(1, "1")]
    [InlineData(-1, "-1")]
    public async Task The_usage_post_carries_the_lane_s_exit_code(int code, string exit)
    {
        var usage = new KeptUsage();
        var report = new UsageReport(new MemoryShare(), usage, new ServerConfig(), new FixedTime(At));

        await report.WriteAsync(new ClaudeRun(code, false, Fixture.Read("result-line.json")), new LaneStart(Lane.Apply, new DateOnly(2026, 11, 12), false, null, []), "nightly/2026-11-12/0600-apply-opus-5-5", At, [], CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(Assert.Single(usage.Documents));
        Assert.Equal(exit, document.RootElement.GetProperty("exit").GetString());
    }
}
