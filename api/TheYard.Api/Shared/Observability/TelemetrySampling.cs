// What the site sends to the workspace, cut where nothing reads it (ADR-024, the addendum on what fills it): the
// site's own successful dependency calls, the largest single source in the workspace at about 63 MB a day, are kept
// one in three, each kept row standing for three; requests, exceptions and every failed call stay whole. And a call
// a card's reader cancelled at its own time limit is that card's reading, which the card already says in words, not
// an exception: it stays a failed dependency with its duration, and no exception row is written for it.
using System.Diagnostics;
using OpenTelemetry;
using Span = System.Diagnostics.Activity;

namespace TheYard.Api.Shared.Observability;

// #region telemetry-sampling
/// <summary>
/// Keeps one in three of the site's successful dependency calls, chosen by the call's own span id so a retry of the
/// question gives the same answer, and marks each kept one as standing for three, so a count by item count stays
/// whole. Runs before the exporter, which only sends what is still recorded.
/// </summary>
public sealed class TelemetrySampling : BaseProcessor<Span>
{
    /// <summary>One successful dependency kept in this many.</summary>
    public const int KeepOneIn = 3;

    /// <summary>The tag the Azure Monitor exporter reads a row's sample rate from, in per cent; a kept row's item count is 100 over it.</summary>
    public const string SampleRateTag = "microsoft.sample_rate";

    /// <summary>The tag a call cancelled at its reader's time limit carries in place of an exception row.</summary>
    public const string CancelledTag = "theyard.read";

    /// <summary>Drops two in three of the successful dependency calls and marks the one kept.</summary>
    /// <param name="data">The call, as it ends.</param>
    public override void OnEnd(Span data)
    {
        if (!Sampled(data.Kind, data.Status))
        {
            return;
        }
        if (Kept(data.SpanId))
        {
            data.SetTag(SampleRateTag, 100.0 / KeepOneIn);
        }
        else
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

    /// <summary>True for the one call in three that is kept, read from the span id's last four hex digits.</summary>
    /// <param name="span">The span's id.</param>
    public static bool Kept(ActivitySpanId span) =>
        Convert.ToInt32(span.ToHexString()[^4..], 16) % KeepOneIn == 0;

    /// <summary>
    /// What the HttpClient instrumentation does with a call that threw: a cancellation, which is a reader's own time limit
    /// reached, is tagged as a read that timed out; any other exception is recorded on the call as before.
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
