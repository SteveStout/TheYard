// What the site sends to the workspace, cut where nothing reads it (ADR-024, the addendum on the dependency rows): the
// site's own successful dependency calls, the largest single source in the workspace at about 63 MB a day, are kept
// one in three; requests, exceptions and every failed call are left as the distro's own sampler leaves them. And a
// call a card's reader cancelled at its own time limit is that card's reading, which the card already says in words,
// not an exception row on the Admin tab's failures list.
using System.Diagnostics;
using OpenTelemetry;
using Span = System.Diagnostics.Activity;

namespace TheYard.Api.Shared.Observability;

// #region telemetry-sampling
/// <summary>
/// Keeps one in three of the site's successful dependency calls, chosen by the call's own span id so the same call
/// always gets the same answer, and keeps a call its reader cancelled off the failures list. Runs before the exporter,
/// which only sends what is still recorded. The exporter this site pins writes every kept row with an item count of
/// one, so a count of the site's dependency rows is a count of the calls kept, a third of them.
/// </summary>
public sealed class TelemetrySampling : BaseProcessor<Span>
{
    /// <summary>One successful dependency kept in this many.</summary>
    public const int KeepOneIn = 3;

    /// <summary>The tag a call cancelled at its reader's time limit carries in place of an exception row.</summary>
    public const string CancelledTag = "theyard.read";

    /// <summary>
    /// Drops two in three of the successful dependency calls. A failed call out whose error is a cancellation, which is a
    /// card reader's own time limit reached, is tagged as a read that timed out; when an exception was already recorded
    /// on it, which no processor can take off a span, the call is not sent, so the failures list never carries it.
    /// </summary>
    /// <param name="data">The call, as it ends.</param>
    public override void OnEnd(Span data)
    {
        if (Cancelled(data.Kind, data.Status, data.GetTagItem("error.type") as string))
        {
            data.SetTag(CancelledTag, "timed out");
            if (data.Events.Any(e => e.Name == "exception"))
            {
                data.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
            }
            return;
        }
        if (Sampled(data.Kind, data.Status) && !Kept(data.SpanId))
        {
            data.ActivityTraceFlags &= ~ActivityTraceFlags.Recorded;
        }
    }

    /// <summary>
    /// True for a successful dependency: a call out (client or producer) or an in-process one, whose status is not an
    /// error. A request (server or consumer) and a failed call are never sampled.
    /// </summary>
    /// <param name="kind">The span's kind.</param>
    /// <param name="status">The span's status as it ended.</param>
    public static bool Sampled(ActivityKind kind, ActivityStatusCode status) =>
        kind is ActivityKind.Client or ActivityKind.Producer or ActivityKind.Internal && status != ActivityStatusCode.Error;

    /// <summary>True for a call out that failed on a cancellation, as the runtime's error.type names it: TaskCanceledException or OperationCanceledException.</summary>
    /// <param name="kind">The span's kind.</param>
    /// <param name="status">The span's status as it ended.</param>
    /// <param name="errorType">The span's error.type tag, or null.</param>
    public static bool Cancelled(ActivityKind kind, ActivityStatusCode status, string? errorType) =>
        kind is ActivityKind.Client && status == ActivityStatusCode.Error
        && errorType is not null && (errorType.EndsWith(nameof(TaskCanceledException), StringComparison.Ordinal) || errorType.EndsWith(nameof(OperationCanceledException), StringComparison.Ordinal));

    /// <summary>True for the one call in three that is kept, read from the span id's last four hex digits.</summary>
    /// <param name="span">The span's id.</param>
    public static bool Kept(ActivitySpanId span) =>
        Convert.ToInt32(span.ToHexString()[^4..], 16) % KeepOneIn == 0;

    /// <summary>
    /// What the HttpClient instrumentation does with a call that threw: a cancellation is tagged as a read that timed
    /// out and recorded as no exception; any other exception is recorded on the call as before.
    /// </summary>
    /// <param name="call">The call's span.</param>
    /// <param name="exception">What it threw.</param>
    public static void OnException(Span call, Exception exception)
    {
        if (exception is OperationCanceledException)
        {
            call.SetTag(CancelledTag, "timed out");
            return;
        }
        call.AddException(exception);
    }
}
// #endregion telemetry-sampling
