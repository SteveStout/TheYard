namespace TheYard.Application;

/// <summary>The four tones a row of the plan card can carry: good, information, warning and alarm.</summary>
public enum PlanTone
{
    /// <summary>Green: inside every threshold.</summary>
    Good,

    /// <summary>Black: nothing to judge yet, or a fact with no threshold.</summary>
    Info,

    /// <summary>Amber: over the first threshold for the whole sustained window.</summary>
    Warning,

    /// <summary>Red: over the second threshold for the whole sustained window, an app stopped, or a restart nobody asked for.</summary>
    Alarm,
}

// #region plan-thresholds
/// <summary>
/// When a reading of the plan turns a row amber or red (ADR: The App Service plan, read every
/// minute). A level counts only when every one of the last fifteen kept minutes is over it, so a
/// deploy's one busy minute is not an alarm and a quarter of an hour at 85 per cent is.
/// </summary>
public static partial class PlanThresholds
{
    /// <summary>How many consecutive minutes a level has to hold.</summary>
    public const int Sustained = 15;

    /// <summary>CPU, in percent: amber from 60, red from 80.</summary>
    public static readonly (double Amber, double Red) Cpu = (60, 80);

    /// <summary>Memory, in percent: amber from 70, red from 85.</summary>
    public static readonly (double Amber, double Red) Memory = (70, 85);

    /// <summary>The HTTP queue, in requests waiting: amber from 5, red from 20.</summary>
    public static readonly (double Amber, double Red) HttpQueue = (5, 20);

    /// <summary>
    /// Outbound sockets one instance allows, by the plan's size as Azure names it. Azure Monitor
    /// counts the sockets and no record the plan returns states the limit, so this is the one rule
    /// the card keeps, read off Microsoft's App Service limits page (learn.microsoft.com, "App
    /// Service limits", 20 October 2026): the Basic and Standard sizes 1 to 3 allow 1,920, 3,968
    /// and 8,064, and Premium v3's sizes 0 to 3 allow 1,920, 3,968, 8,064 and 16,128, which is
    /// 128 short of a power of two at every step. A size outside those is measured against no
    /// limit, and the row says so.
    /// </summary>
    public static int? SocketLimitFor(string? sku)
    {
        var size = SizeName().Match(sku?.Trim() ?? "");
        if (!size.Success)
        {
            return null;
        }
        bool premium = size.Groups[1].Value.Equals("P", StringComparison.OrdinalIgnoreCase);
        if (premium != size.Groups[3].Success)
        {
            return null;
        }
        int step = (size.Groups[2].Value[0] - '0') + (premium ? 1 : 0);
        return step is >= 1 and <= 4 ? (1 << (10 + step)) - 128 : null;
    }

    /// <summary>A size's name: the tier letter, the size digit, and v3 on the Premium tier.</summary>
    [System.Text.RegularExpressions.GeneratedRegex("^([BSP])(\\d)(V3)?$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex SizeName();

    /// <summary>Sockets, as a share of the limit: amber from 60 per cent; there is no red short of running out.</summary>
    public const double SocketsAmberShare = 0.60;

    /// <summary>
    /// The tone of one reading over its newest minutes, oldest first: red or amber when every one
    /// of the last <see cref="Sustained"/> minutes is at or over that level, good when they are
    /// not, information when fewer than that many minutes carry the reading.
    /// </summary>
    public static PlanTone ToneOf(IReadOnlyList<double?> minutes, (double Amber, double Red) levels)
    {
        var last = minutes.TakeLast(Sustained).ToList();
        if (last.Count < Sustained || last.Any(value => value is null))
        {
            return PlanTone.Info;
        }
        return last.All(value => value >= levels.Red) ? PlanTone.Alarm
            : last.All(value => value >= levels.Amber) ? PlanTone.Warning
            : PlanTone.Good;
    }

    /// <summary>The sockets row's tone: amber when every one of the last fifteen minutes held 60 per cent of the limit or more; informational where the size names no limit.</summary>
    public static PlanTone SocketsTone(IReadOnlyList<double?> minutes, int? limit) =>
        limit is { } sockets ? ToneOf(minutes, (sockets * SocketsAmberShare, double.PositiveInfinity)) : PlanTone.Info;

    /// <summary>The apps row's tone: red when any app is not running, good when all are, information before the first hour is read.</summary>
    public static PlanTone AppsTone(IReadOnlyList<PlanAppState> apps) =>
        apps.Count == 0 ? PlanTone.Info
        : apps.Any(app => !string.Equals(app.State, "Running", StringComparison.OrdinalIgnoreCase)) ? PlanTone.Alarm
        : PlanTone.Good;

    /// <summary>The events row's tone: red on a restart nobody asked for, information when anything else happened, good when nothing did.</summary>
    public static PlanTone EventsTone(IReadOnlyList<PlanEvent> events) =>
        events.Any(e => e.Kind == "restart" && !e.Planned) ? PlanTone.Alarm
        : events.Count > 0 ? PlanTone.Info
        : PlanTone.Good;
}
// #endregion plan-thresholds
