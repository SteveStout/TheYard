using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Instrumentation.Http;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace TheYard.Api.Shared.Wiring;

/// <summary>
/// Application Insights where a connection string was handed over at roll time, and
/// the reader the Admin tab queries with the container's identity (ADR-024).
/// </summary>
public static class TelemetryRegistration
{
    /// <summary>Turns telemetry on when configured and registers its reader.</summary>
    /// <param name="builder">The host being built.</param>
    /// <param name="identity">Where this process runs: its web app's name is the role the reader counts, its door the one it asks for a token.</param>
    public static void AddTheYardTelemetry(this WebApplicationBuilder builder, HostIdentity identity)
    {
        #region telemetry
        // Application Insights. The connection string is an ingestion key, so it is
        // never in the repository: the deploy reads it from Azure at roll time and
        // passes it to the container as an environment variable. Absent, as it is
        // locally and in every test, this block does nothing and the app runs without
        // telemetry, which is why no test needs a fake for it.
        string? telemetryConnection = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        bool telemetryOn = !string.IsNullOrWhiteSpace(telemetryConnection)
            && !telemetryConnection.StartsWith("__", StringComparison.Ordinal);
        if (telemetryOn)
        {
            // Two in three of the site's successful dependency calls stay out of the workspace, the one kept standing for
            // three (TelemetrySampling); added before the distro, as its documentation says, so it runs before the exporter.
            builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddProcessor(new TelemetrySampling()));
            builder.Services.AddOpenTelemetry().UseAzureMonitor(options =>
            {
                options.ConnectionString = telemetryConnection;
            });
            // A call a card's reader cancelled at its own time limit is that card's reading, not an exception row (it was
            // 237 of them in a week on the Admin tab's failures list); every other exception on a call is recorded as before.
            // Registered after the distro, so it is the last word on the option the distro sets.
            builder.Services.Configure<HttpClientTraceInstrumentationOptions>(options =>
            {
                options.RecordException = false;
                options.EnrichWithException = TelemetrySampling.OnException;
            });
            // The box's agent posts every two seconds, forty-three thousand requests a day, which would spend most of
            // the component's daily cap on itself (ADR: The box reports itself): its routes are left out of the
            // request telemetry, the distro's documented way, and every other path is kept.
            builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options => options.Filter = context => Traced(context.Request.Path, context.Request.Headers.UserAgent.ToString()));
            // The workspace's free 5 GB a month (ADR-024, the addendum on what fills it): the HttpClient's connection-pool
            // instruments, read by nothing on the site, and Entity Framework's line for every command, which the Admin
            // tab's own rings already keep, stay out of telemetry. The distro's documented way to drop an instrument.
            foreach (string instrument in DroppedInstruments)
            {
                builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddView(instrumentName: instrument, MetricStreamConfiguration.Drop));
            }
            builder.Logging.AddFilter<OpenTelemetryLoggerProvider>(CommandCategory, LogLevel.Warning);
        }
        // The component's app id is public (it is not a key) and is what the Admin
        // tab queries with the container's managed identity.
        var source = new TelemetrySource(
            builder.Configuration["Azure:AppInsightsAppId"] ?? "6ff89351-7fcc-4a41-8238-db65c5903c36",
            AzureIds.ClientId(builder.Configuration),
            identity: identity);
        builder.Services.AddSingleton(source);
        // Wired only where the connection string is: the app id has a default and
        // is therefore no evidence at all that this build can read anything. The real
        // clock is handed over here, which is the one place a clock belongs.
        builder.Services.AddSingleton(new TelemetryReader(source, enabled: telemetryOn, TimeProvider.System));
        #endregion telemetry
    }

    /// <summary>
    /// The metric instruments left out of telemetry: the HttpClient's connection pool, which sent about 34 MB a day to
    /// the workspace (its AppMetrics table) and which no card, alert or query reads. The request durations
    /// stay.
    /// </summary>
    public static readonly IReadOnlyList<string> DroppedInstruments =
    [
        "http.client.open_connections",
        "http.client.active_requests",
        "http.client.connection.duration",
        "http.client.request.time_in_queue",
    ];

    /// <summary>
    /// Entity Framework's category for the line it logs for every command: kept from telemetry below a warning, and
    /// kept whole in the Admin tab's rings and the kept log, which read the same lines through their own providers.
    /// </summary>
    public const string CommandCategory = "Microsoft.EntityFrameworkCore.Database.Command";

    /// <summary>
    /// Whether a request reaches the request telemetry: every request but the box's routes, every card's live route
    /// (polled every few seconds while a card is open), and the site's own self-reads (the keep-warm loop and the
    /// nightly self-read, which carry the self-read tag), none of which is a visitor.
    /// </summary>
    /// <param name="path">The request's path.</param>
    /// <param name="userAgent">The request's user agent, empty when it sent none.</param>
    public static bool Traced(PathString path, string? userAgent = null) =>
        !(path.HasValue && (path.Value!.StartsWith(RequestRecorder.BoxPrefix, StringComparison.Ordinal) || IsLive(path.Value)))
        && !(userAgent ?? "").Contains(Hits.SelfTag, StringComparison.OrdinalIgnoreCase);

    /// <summary>A live route: /api/admin/cloud/{key}/live or /api/admin/plan/live.</summary>
    public static bool IsLive(string path) =>
        path == "/api/admin/plan/live"
        || (path.StartsWith("/api/admin/cloud/", StringComparison.Ordinal) && path.EndsWith("/live", StringComparison.Ordinal) && path.Count(c => c == '/') == 5);
}
