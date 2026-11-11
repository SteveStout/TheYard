using System.Text.Json;

namespace JobHunt.Server.Application.Features.Run;

/// <summary>
/// Cuts a lane run's usage into its folder and reports it to the site. It runs after the records are written and on a
/// token nothing cancels, so a report that hangs never costs a row; a failure is a line in the DONE note, never a stop.
/// </summary>
/// <param name="share">The share.</param>
/// <param name="usage">The usage report.</param>
/// <param name="config">The settings.</param>
/// <param name="time">The clock.</param>
public sealed class UsageReport(IShare share, IUsageSink usage, ServerConfig config, TimeProvider time)
{
    /// <summary>Writes usage.json and reports it, adding the outcome to the note's lines.</summary>
    /// <param name="run">The CLI's run.</param>
    /// <param name="start">The lane.</param>
    /// <param name="folder">The run folder.</param>
    /// <param name="began">When the lane began.</param>
    /// <param name="lines">The DONE note's lines.</param>
    /// <param name="ct">Stops the report.</param>
    public async Task WriteAsync(ClaudeRun run, LaneStart start, string folder, DateTimeOffset began, List<string> lines, CancellationToken ct)
    {
        if (run.ResultLine is not { } line)
        {
            return;
        }
        try
        {
            string cut = UsageCut.Json(line);
            await share.WriteAsync($"{folder}/usage.json", cut, ct);
            var interim = new LaneReport(start.Lane, config.For(start.Lane).Model, start.Dry, began, time.GetUtcNow(), start.Before, [], [], null, [], [], null);
            lines.Add(await usage.ReportAsync(UsageCut.Document(cut, interim, RegistryRows.Lane + began.ToUnixTimeSeconds(), start.Slot), ct));
        }
        catch (Exception failure) when (failure is IOException or JsonException)
        {
            lines.Add($"usage not written: {failure.Message}");
        }
    }
}
