using JobHunt.Functions.Merge;
using JobHunt.Functions.Sheet;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace JobHunt.Functions;

/// <summary>
/// The Sheet mirror, every fifteen minutes at 7, 22, 37 and 52 past (UTC clock), clear of the
/// merger's minute and the slot hour's dispatch. SHEET_MIRROR picks the mode: "shadow" plans and
/// writes the plan only, "on" appends and checks, anything else is off. It runs only while the
/// Google settings are set; without them it logs why and does nothing.
/// </summary>
public sealed class SheetMirrorTimer(IMergeStore store, IHttpClientFactory http, TimeProvider time, ILogger<SheetMirrorTimer> log)
{
    [Function("SheetMirror")]
    public async Task RunAsync([TimerTrigger("0 7,22,37,52 * * * *")] TimerInfo timer, CancellationToken ct)
    {
        var mode = Mode(Environment.GetEnvironmentVariable("SHEET_MIRROR"));
        if (mode == SheetMode.Off)
        {
            log.LogInformation("sheet mirror idle: SHEET_MIRROR is not shadow or on");
            return;
        }
        if (GoogleSheetSettings.FromEnvironment() is not { } settings)
        {
            log.LogWarning("sheet mirror blocked: GMAIL_CLIENT_ID, GMAIL_CLIENT_SECRET, GMAIL_REFRESH_TOKEN or TRACKER_SHEET_ID is not set");
            return;
        }
        string line = await new SheetMirror(store, new GoogleSheetGrid(http, settings), time).RunAsync(mode, ct);
        log.LogInformation("sheet mirror: {Line}", line);
    }

    /// <summary>The mode a SHEET_MIRROR value names: shadow, on, or off for anything else.</summary>
    public static SheetMode Mode(string? value) =>
        string.Equals(value, "on", StringComparison.OrdinalIgnoreCase) ? SheetMode.On
        : string.Equals(value, "shadow", StringComparison.OrdinalIgnoreCase) ? SheetMode.Shadow
        : SheetMode.Off;
}
