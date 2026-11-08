using Azure.Monitor.OpenTelemetry.AspNetCore;
using OpenTelemetry.Instrumentation.AspNetCore;

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
            builder.Services.AddOpenTelemetry().UseAzureMonitor(options =>
            {
                options.ConnectionString = telemetryConnection;
            });
            // The box's agent posts every two seconds, forty-three thousand requests a day, which would spend most of
            // the component's daily cap on itself (ADR: The box reports itself): its routes are left out of the
            // request telemetry, the distro's documented way, and every other path is kept.
            builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options => options.Filter = context => Traced(context.Request.Path, context.Request.Headers.UserAgent.ToString()));
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
