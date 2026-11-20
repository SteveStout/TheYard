using System.Text.Json;
using Microsoft.Extensions.Logging;
using Xunit;

namespace JobHunt.Functions.Tests;

/// <summary>
/// What the Function sends to the workspace (ADR-109, the addendum on what the workspace pays for): the HTTP client, the
/// host's start-up and the health route's status lines only at Warning and above, every process's own run line at
/// Information, because the Admin tab's nightly card reads the run lines and nothing else of the three.
/// </summary>
public sealed class LogLevelsTests
{
    [Fact]
    public void The_http_client_says_only_warnings_and_a_process_keeps_its_information_line()
    {
        var provider = new Seen();
        using var factory = LoggerFactory.Create(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Information).AddProvider(provider);
            LogLevels.Apply(logging);
        });

        Assert.False(factory.CreateLogger("System.Net.Http.HttpClient.Default.LogicalHandler").IsEnabled(LogLevel.Information));
        Assert.False(factory.CreateLogger("System.Net.Http.HttpClient.Default.ClientHandler").IsEnabled(LogLevel.Information));
        Assert.True(factory.CreateLogger("System.Net.Http.HttpClient.Default.ClientHandler").IsEnabled(LogLevel.Warning));
        Assert.True(factory.CreateLogger("JobHunt.Functions.FreshScanTimer").IsEnabled(LogLevel.Information));
        Assert.True(factory.CreateLogger("JobHunt.Functions.Merger").IsEnabled(LogLevel.Information));
    }

    [Fact]
    public void The_host_keeps_information_by_default_and_its_start_up_and_status_lines_at_warning()
    {
        using var host = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "functions", "jobhunt", "host.json")));
        var levels = host.RootElement.GetProperty("logging").GetProperty("logLevel");

        Assert.Equal("Information", levels.GetProperty("default").GetString());
        Assert.Equal("Warning", levels.GetProperty("Host.Startup").GetString());
        Assert.Equal("Warning", levels.GetProperty("Microsoft.AspNetCore.Mvc.StatusCodeResult").GetString());
        Assert.False(levels.TryGetProperty("Function", out _));
    }

    /// <summary>
    /// The host's per-function aggregates (AppMetrics, about 8 MB a day) and its performance counters (about 27 MB a day)
    /// stay out of the workspace: the Nightly card reads the Function's Azure Monitor metrics and its run lines, and no
    /// query on the Admin tab reads either table for the Function (ADR-109, the addendum on what the workspace pays for).
    /// The run lines stay at Information.
    /// </summary>
    [Fact]
    public void The_host_sends_no_aggregates_and_no_performance_counters()
    {
        using var host = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "functions", "jobhunt", "host.json")));
        var logging = host.RootElement.GetProperty("logging");

        Assert.Equal("Warning", logging.GetProperty("logLevel").GetProperty("Host.Aggregator").GetString());
        Assert.False(logging.GetProperty("applicationInsights").GetProperty("enablePerformanceCountersCollection").GetBoolean());
        Assert.Equal("Information", logging.GetProperty("logLevel").GetProperty("default").GetString());
    }

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "functions", "jobhunt", "host.json")))
            {
                return dir.FullName;
            }
        }
        throw new InvalidOperationException("functions/jobhunt/host.json not found above " + AppContext.BaseDirectory);
    }

    /// <summary>A provider that takes every line, so the filters alone decide what is enabled.</summary>
    private sealed class Seen : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Taker();

        public void Dispose()
        {
        }
    }

    private sealed class Taker : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
        }
    }
}
