namespace TheYard.Tests;

/// <summary>
/// What the site leaves out of telemetry so the workspace stays inside its free 5 GB a month (ADR: Telemetry that outlives the container, the
/// addendum on what fills it): the HttpClient's connection-pool instruments and Entity Framework's line for every
/// command. Read on the workspace's Usage and AppMetrics tables before the change, these were the site's largest
/// sources after the request telemetry it keeps; no card, alert or query reads either.
/// </summary>
public sealed class TelemetryTrimTests
{
    private static string Registration() =>
        File.ReadAllText(Path.Combine(Repo.Root(), "api", "TheYard.Api", "Shared", "Wiring", "TelemetryRegistration.cs"));

    [Fact]
    public void The_connection_pool_instruments_are_dropped_and_the_request_durations_kept()
    {
        Assert.Equal(["http.client.open_connections", "http.client.active_requests", "http.client.connection.duration", "http.client.request.time_in_queue"], TelemetryRegistration.DroppedInstruments);
        Assert.DoesNotContain("http.server.request.duration", TelemetryRegistration.DroppedInstruments);
        Assert.DoesNotContain("http.client.request.duration", TelemetryRegistration.DroppedInstruments);
        Assert.Contains("metrics.AddView(instrumentName: instrument, MetricStreamConfiguration.Drop)", Registration(), StringComparison.Ordinal);
    }

    [Fact]
    public void Entity_frameworks_command_lines_stay_out_of_telemetry_alone_and_in_the_admin_rings()
    {
        Assert.Equal("Microsoft.EntityFrameworkCore.Database.Command", TelemetryRegistration.CommandCategory);
        // The filter names the telemetry provider, so the rings' and the kept log's providers still read every line.
        Assert.Contains("builder.Logging.AddFilter<OpenTelemetryLoggerProvider>(CommandCategory, LogLevel.Warning)", Registration(), StringComparison.Ordinal);
        Assert.DoesNotContain(TelemetryRegistration.CommandCategory, File.ReadAllText(Path.Combine(Repo.Root(), "api", "TheYard.Api", "appsettings.json")), StringComparison.Ordinal);
    }
}
