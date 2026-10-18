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
public static class PlanThresholds
{
    /// <summary>How many consecutive minutes a level has to hold.</summary>
    public const int Sustained = 15;

    /// <summary>CPU, in percent: amber from 60, red from 80.</summary>
    public static readonly (double Amber, double Red) Cpu = (60, 80);

    /// <summary>Memory, in percent: amber from 70, red from 85.</summary>
    public static readonly (double Amber, double Red) Memory = (70, 85);

    /// <summary>The HTTP queue, in requests waiting: amber from 5, red from 20.</summary>
    public static readonly (double Amber, double Red) HttpQueue = (5, 20);

    /// <summary>Outbound sockets one B1 instance allows, which the sockets row is measured against.</summary>
    public const int SocketLimit = 1_920;

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

    /// <summary>The sockets row's tone: amber when every one of the last fifteen minutes held 60 per cent of the limit or more.</summary>
    public static PlanTone SocketsTone(IReadOnlyList<double?> minutes) =>
        ToneOf(minutes, (SocketLimit * SocketsAmberShare, double.PositiveInfinity));

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
