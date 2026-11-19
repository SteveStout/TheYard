using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

/// <summary>
/// What the worker sends to Application Insights (ADR-109, the addendum on what the workspace pays for). The HTTP client's
/// own Information lines, "Start processing" and "Sending HTTP request" for every board FreshScan reads, were about 54,000
/// rows and 46 MB a day of the workspace's free 5 GB a month; the HTTP client says only warnings and errors now. Every
/// run line ("process:..."), which the Admin tab's nightly card reads, is the process's own and keeps Information.
/// </summary>
public static class LogLevels
{
    /// <summary>The HTTP client's categories, every named client and handler under it.</summary>
    public const string HttpClient = "System.Net.Http.HttpClient";

    /// <summary>The worker's filters.</summary>
    /// <param name="logging">The worker's logging.</param>
    public static ILoggingBuilder Apply(ILoggingBuilder logging) => logging.AddFilter(HttpClient, LogLevel.Warning);
}
