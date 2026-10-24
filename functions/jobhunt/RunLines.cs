using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

/// <summary>
/// How a process writes its run line (ADR-109, the addendum on the six processes): one information line a run,
/// "process:Name outcome=... took_ms=... started=..." and its counts, which the Functions host sends to
/// Application Insights as a trace in the run's own operation, beside the request row the host writes for the
/// invocation. The Admin tab's nightly card reads the two together. Counts are read out of a part's own line by
/// a pattern a count, so the line's words, which can name a sender, never reach Application Insights here.
/// </summary>
public static partial class RunLines
{
    /// <summary>The prefix every run line opens with, which the card's query finds them by.</summary>
    public const string Prefix = "process:";

    /// <summary>Writes the run's line once, at its end.</summary>
    public static void Write(ILogger log, RunLine run, DateTimeOffset ended) => Line(log, run.Text(ended));

    /// <summary>Keeps each named count a pattern finds in a part's line; a pattern that finds nothing keeps nothing.</summary>
    public static RunLine Counted(RunLine run, string text, params (string Name, string Pattern)[] counts)
    {
        foreach (var (name, pattern) in counts)
        {
            if (Regex.Match(text, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100)) is { Success: true } match
                && long.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
            {
                run.Count(name, value);
            }
        }
        return run;
    }

    /// <summary>A slot's name as one word: "4 AM reply" becomes "4_AM_reply".</summary>
    public static string SlotWord(string slot) => Spaces().Replace(slot.Trim(), "_");

    [LoggerMessage(EventId = 109, Level = LogLevel.Information, Message = "{RunLine}")]
    private static partial void Line(ILogger log, string runLine);

    [GeneratedRegex(@"[^A-Za-z0-9]+")]
    private static partial Regex Spaces();
}
