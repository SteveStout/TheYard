using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

// #region summary
/// <summary>
/// Appends the tracker's new rows to the Google Sheet and checks they landed, so the records run stops paying a
/// model to do it. In shadow it writes the plan and acts on nothing, and it does not move on a row the CSV and the
/// Sheet disagree about.
/// </summary>
// #endregion summary
/// <remarks>
/// The Sheet mirror, every fifteen minutes at 7, 22, 37 and 52 past (UTC clock), clear of the
/// merger's minute and the slot hour's dispatch. SHEET_MIRROR picks the mode: "shadow" plans and
/// writes the plan only, "on" appends and checks, anything else is off. It runs only while the
/// Google settings are set; without them it logs why and does nothing.
/// </remarks>
public sealed class SheetMirrorTimer(IMergeStore store, IHttpClientFactory http, TimeProvider time, ILogger<SheetMirrorTimer> log)
{
    [Function("SheetMirror")]
    public async Task RunAsync([TimerTrigger("0 7,22,37,52 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        var run = new RunLine("SheetMirror", time.GetUtcNow(), [7, 22, 37, 52]);
        try
        {
            var mode = Mode(Environment.GetEnvironmentVariable("SHEET_MIRROR"));
            if (mode == SheetMode.Off)
            {
                run.Came("unchanged", "switch_off");
                log.LogInformation("sheet mirror idle: SHEET_MIRROR is not shadow or on");
                return;
            }
            if (GoogleSheetSettings.FromEnvironment() is not { } settings)
            {
                run.Came("blocked", "google_settings");
                log.LogWarning("sheet mirror blocked: GMAIL_CLIENT_ID, GMAIL_CLIENT_SECRET, GMAIL_REFRESH_TOKEN or TRACKER_SHEET_ID is not set");
                return;
            }
            string line = await new SheetMirror(store, new GoogleSheetGrid(http, settings), time).RunAsync(mode, ct);
            RunLines.Counted(run, line, ("sheet_rows", @"sheet (\d+)"), ("master_rows", @"master (\d+)"), ("would_append", @"add (\d+)"));
            var (outcome, detail) = Outcome(line, mode);
            run.Came(outcome, detail);
            log.LogInformation("sheet mirror: {Line}", line);
        }
        catch (Exception ex)
        {
            run.Failed(ex);
            throw;
        }
        finally
        {
            RunLines.Write(log, run, time.GetUtcNow());
        }
    }

    /// <summary>
    /// What a pass came to, from its line: a read-back that disagreed is blocked on the mismatch; a plan the gate
    /// stopped at a row it may not copy is blocked on the gate, so the card shows the rows waiting behind it; otherwise
    /// shadow planned and on appended.
    /// </summary>
    public static (string Outcome, string? Detail) Outcome(string line, SheetMode mode) =>
        line.Contains("MISMATCH", StringComparison.Ordinal) ? ("blocked", "mismatch")
        : line.Contains(" STOPPED at ", StringComparison.Ordinal) ? ("blocked", "gate")
        : (mode == SheetMode.Shadow ? "planned" : "success", null);

    /// <summary>The mode a SHEET_MIRROR value names: shadow, on, or off for anything else.</summary>
    public static SheetMode Mode(string? value) =>
        string.Equals(value, "on", StringComparison.OrdinalIgnoreCase) ? SheetMode.On
        : string.Equals(value, "shadow", StringComparison.OrdinalIgnoreCase) ? SheetMode.Shadow
        : SheetMode.Off;
}
