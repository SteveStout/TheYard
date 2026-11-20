using System.Diagnostics;
using System.Text.RegularExpressions;
using TheYard.Api.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The site's successful dependency calls kept one in three (ADR: Telemetry that outlives the container, the addendum on
/// the dependency rows), requests and failed calls kept whole, a reader's cancelled call not written as an exception, and
/// every query the Admin tab runs against the workspace still seeing what it needs.
/// </summary>
public sealed partial class TelemetrySamplingTests
{
    private static List<Activity> Ended(ActivityKind kind, int count, ActivityStatusCode status = ActivityStatusCode.Unset)
    {
        string name = "TheYard.Tests.TelemetrySampling." + Guid.NewGuid().ToString("N");
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == name,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        };
        ActivitySource.AddActivityListener(listener);
        using var source = new ActivitySource(name);
        var processor = new TelemetrySampling();
        var ended = new List<Activity>();
        for (int i = 0; i < count; i++)
        {
            var span = source.StartActivity("call", kind, parentContext: default)!;
            span.SetStatus(status);
            span.Stop();
            processor.OnEnd(span);
            ended.Add(span);
        }
        return ended;
    }

    [Fact]
    public void About_one_in_three_successful_calls_is_kept_and_each_kept_one_stands_for_three()
    {
        var spans = Ended(ActivityKind.Client, 3_000);
        var kept = spans.Where(span => span.Recorded).ToList();
        Assert.InRange(kept.Count, 850, 1_150);
        Assert.All(kept, span => Assert.Equal(100.0 / 3, (double)span.GetTagItem(TelemetrySampling.SampleRateTag)!, 6));
        Assert.All(spans.Where(span => !span.Recorded), span => Assert.Null(span.GetTagItem(TelemetrySampling.SampleRateTag)));
    }

    [Fact]
    public void Requests_and_failed_calls_are_kept_whole()
    {
        Assert.All(Ended(ActivityKind.Server, 300), span => Assert.True(span.Recorded));
        Assert.All(Ended(ActivityKind.Consumer, 30), span => Assert.True(span.Recorded));
        Assert.All(Ended(ActivityKind.Client, 300, ActivityStatusCode.Error), span => Assert.True(span.Recorded));
        Assert.All(Ended(ActivityKind.Client, 300, ActivityStatusCode.Error), span => Assert.Null(span.GetTagItem(TelemetrySampling.SampleRateTag)));
    }

    [Theory]
    [InlineData(ActivityKind.Client, ActivityStatusCode.Ok, true)]
    [InlineData(ActivityKind.Producer, ActivityStatusCode.Unset, true)]
    [InlineData(ActivityKind.Internal, ActivityStatusCode.Unset, true)]
    [InlineData(ActivityKind.Client, ActivityStatusCode.Error, false)]
    [InlineData(ActivityKind.Server, ActivityStatusCode.Ok, false)]
    [InlineData(ActivityKind.Consumer, ActivityStatusCode.Ok, false)]
    public void Only_a_successful_dependency_is_sampled(ActivityKind kind, ActivityStatusCode status, bool sampled) =>
        Assert.Equal(sampled, TelemetrySampling.Sampled(kind, status));

    [Fact]
    public void The_choice_is_the_span_s_own_so_the_same_call_always_gets_the_same_answer()
    {
        var id = ActivitySpanId.CreateFromString("00000000000000c3".AsSpan());
        Assert.True(TelemetrySampling.Kept(id));
        Assert.False(TelemetrySampling.Kept(ActivitySpanId.CreateFromString("00000000000000c4".AsSpan())));
        Assert.Equal(TelemetrySampling.Kept(id), TelemetrySampling.Kept(id));
    }

    [Fact]
    public void A_cancelled_call_is_tagged_as_a_read_that_timed_out_and_any_other_exception_is_recorded()
    {
        using var cancelled = new Activity("call").Start();
        TelemetrySampling.OnException(cancelled, new TaskCanceledException("The operation was canceled."));
        Assert.Equal("timed out", cancelled.GetTagItem(TelemetrySampling.CancelledTag));
        Assert.Empty(cancelled.Events);

        using var refused = new Activity("call").Start();
        TelemetrySampling.OnException(refused, new HttpRequestException("Connection refused"));
        Assert.Null(refused.GetTagItem(TelemetrySampling.CancelledTag));
        Assert.Contains(refused.Events, e => e.Name == "exception");
    }

    [Fact]
    public void The_registration_adds_the_processor_before_the_distro_and_takes_the_exception_option_after_it()
    {
        string registration = File.ReadAllText(Path.Combine(Repo.Root(), "api", "TheYard.Api", "Shared", "Wiring", "TelemetryRegistration.cs"));
        int processor = registration.IndexOf("tracing.AddProcessor(new TelemetrySampling())", StringComparison.Ordinal);
        int distro = registration.IndexOf("UseAzureMonitor(", StringComparison.Ordinal);
        int exceptions = registration.IndexOf("options.EnrichWithException = TelemetrySampling.OnException", StringComparison.Ordinal);
        Assert.True(processor > 0 && processor < distro && distro < exceptions, "the processor runs before the exporter and the exception option is set after the distro's");
        Assert.Contains("options.RecordException = false;", registration, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every query the Admin tab runs against the workspace that reads the dependency table, named here with what it needs
    /// from it. A count reads item counts, so a kept row counts for the three it stands for; the component's size reads
    /// billed bytes, which sampling lowers because it is what is sent; a run's trace is the Function's, which is not
    /// sampled. A new query that reads the table fails here until it is named.
    /// </summary>
    [Fact]
    public void Every_admin_query_on_the_dependency_table_still_sees_what_it_needs()
    {
        string api = Path.Combine(Repo.Root(), "api", "TheYard.Api");
        var readers = Directory.EnumerateFiles(api, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => Dependencies().IsMatch(File.ReadAllText(path)))
            .Select(path => Path.GetRelativePath(api, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.Equal(["Features/Admin/Cloud/InsightsProcessRuns.cs", "Features/Admin/Cloud/InsightsSource.cs"], readers);

        Assert.Contains("summarize items = sum(ItemCount) by AppRoleName", InsightsSource.Reporters, StringComparison.Ordinal);
        Assert.DoesNotContain("count()", InsightsSource.Reporters, StringComparison.Ordinal);
        Assert.Contains("sum(_BilledSize)", InsightsSource.ComponentToday, StringComparison.Ordinal);
        Assert.Contains("where OperationId ==", File.ReadAllText(Path.Combine(api, "Features", "Admin", "Cloud", "InsightsProcessRuns.cs")), StringComparison.Ordinal);
        // The failures list and the telemetry card read requests and exceptions, both kept whole, by item count.
        Assert.DoesNotContain("AppDependencies", InsightsIncidents.Failures, StringComparison.Ordinal);
        Assert.Contains("sum(ItemCount)", InsightsIncidents.Failures, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"\bAppDependencies\b|\bdependencies\s*\|")]
    private static partial Regex Dependencies();
}
