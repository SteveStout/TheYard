// What every answer this process gives is recorded and stamped with: how long it took, the
// failures in it, and the name of the web app that gave it. Its own file because recording an
// answer is a different job from deciding what a browser may keep (ResponseKeeping.cs) and from
// the order the steps go in, which is RequestPipeline.cs and is now most of what it holds.
//
// Two methods rather than one, because these steps are not next to each other and must not be:
// the timing is outside the exception handler and the record is directly inside it, and the file
// that holds the order is the one that puts them there.
using System.Diagnostics;

namespace TheYard.Api.Shared.Wiring;

/// <summary>
/// The timing of every request, the record of every failure, and the header that names the web app
/// that answered (ADR: The order of the request pipeline).
/// </summary>
public static class RequestRecording
{
    /// <summary>
    /// The outermost step: how long the caller waited and what status they actually got.
    /// </summary>
    /// <param name="app">The built host.</param>
    /// <param name="recorder">The request ring every answer is recorded in.</param>
    public static WebApplication UseTheYardRequestTiming(this WebApplication app, RequestRecorder recorder)
    {
        #region request-timing
        // Timing, and it has to be the outermost thing here.
        //
        // Outermost, above UseExceptionHandler, because unwinding runs inner to outer.
        // Placed below the handler, a request that threw would reach this finally
        // before the handler had written anything, so every failed request would be
        // recorded as a 200 with the handler's time left out. Here it records the
        // status the caller actually received and the whole time they waited.
        //
        // Above the handler it sees the status that was actually sent. Above
        // UseAuthentication too, so a request rejected with 401 is counted rather than
        // short-circuited before it ever reaches the ring.
        app.Use(async (context, next) =>
        {
            long start = Stopwatch.GetTimestamp();
            recorder.MarkOperator(context); // the operator's key leaves with the operator's cookie (ADR-116)
            try
            {
                await next();
            }
            finally
            {
                recorder.Record(context, Stopwatch.GetElapsedTime(start));
            }
        });
        #endregion request-timing

        return app;
    }

    /// <summary>
    /// The failure record and the site header, which both belong inside the exception handler: the
    /// record so it sees a failure in anything below it, the header because every answer carries it.
    /// </summary>
    /// <param name="app">The built host.</param>
    /// <param name="errorLog">The server's error ring, read by the Admin tab.</param>
    /// <param name="identity">Where this process runs: the web app's name goes on every answer.</param>
    public static WebApplication UseTheYardAnswerRecord(this WebApplication app, ErrorRingBuffer errorLog, HostIdentity identity)
    {
        #region error-log
        // What: every 500 and every exception goes into the error record the Admin tab
        // reads. How: it records and rethrows rather than handling, so the exception
        // handler above still owns the response. Why here: directly inside the
        // exception handler, so it sees a failure in anything below it, signing in and
        // renewing a session included. It sat below both until the pipeline lane, and
        // an exception thrown while reading a token never reached the record.
        app.Use(async (context, next) =>
        {
            try
            {
                await next();
                if (context.Response.StatusCode >= 500)
                {
                    errorLog.Record(context.Request.Path, context.Response.StatusCode, "server error response");
                }
            }
            catch (Exception ex)
            {
                // A caller who hung up is not a server error: the exception handler
                // answers it 499 and logs it at Information, so it is left out of
                // this record too, and the Admin tab's error count stays green.
                if (!ProblemHandler.CallerLeft(context, ex))
                {
                    // The type and the frames, never the message. This record is
                    // served at /api/errors with no sign-in, and an exception message
                    // is where a framework writes a filesystem path, a connection
                    // detail, or the value that broke a constraint; the exception
                    // handler keeps it out of a response for the same reason. The
                    // frames are source locations this repository already publishes.
                    // The message is not lost: it goes to the console and to
                    // Application Insights as a structured exception, behind a sign-in
                    // (ADR: Error handling, the addendum on frames).
                    errorLog.Record(context.Request.Path, 500, ex.GetType().Name, StackFrames.Of(ex));
                }
                throw;
            }
        });
        #endregion error-log

        #region site-header
        // Every answer names the web app that gave it, App Service's own WEBSITE_SITE_NAME, so the
        // edge card's hourly sample reads who answered a page and who answered the API instead of
        // saying so from memory. App Service sets it as an environment setting; off where none is set.
        if (identity.SiteName is { } siteName)
        {
            app.Use(async (context, next) =>
            {
                context.Response.OnStarting(() =>
                {
                    context.Response.Headers[EdgeKeeps.SiteHeader] = siteName;
                    return Task.CompletedTask;
                });
                await next();
            });
        }
        #endregion site-header

        return app;
    }
}
