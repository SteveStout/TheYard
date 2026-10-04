using Azure.Monitor.OpenTelemetry.AspNetCore;

namespace TheYard.Api;

/// <summary>
/// Application Insights where a connection string was handed over at roll time, and
/// the reader the Admin tab queries with the container's identity (ADR-024).
/// </summary>
public static class TelemetryRegistration
{
    /// <summary>Turns telemetry on when configured and registers its reader.</summary>
    public static void AddTheYardTelemetry(this WebApplicationBuilder builder)
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
        }
        // The component's app id is public (it is not a key) and is what the Admin
        // tab queries with the container's managed identity.
        var telemetry = new TelemetryReader(
            builder.Configuration["Azure:AppInsightsAppId"] ?? "6ff89351-7fcc-4a41-8238-db65c5903c36",
            YardComposition.AzureClientId(builder.Configuration),
            // Wired only where the connection string is: the app id has a default and
            // is therefore no evidence at all that this build can read anything.
            enabled: telemetryOn);
        builder.Services.AddSingleton(telemetry);
        #endregion telemetry
    }
}
