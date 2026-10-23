// The words and tones every card under Cloud infrastructure shares, so seven cards say "fine" and
// draw green for the same reasons (ADR: The App Service plan, read every minute).
using TheYard.Application;

namespace TheYard.Api.Shared.Grades;

/// <summary>Tones as the wire writes them, and the small rules every card's dial and rows share.</summary>
public static class CloudTones
{
    /// <summary>A tone as the wire writes it.</summary>
    public static string Of(PlanTone tone) => tone switch
    {
        PlanTone.Good => "good",
        PlanTone.Warning => "warning",
        PlanTone.Alarm => "alarm",
        _ => "info",
    };

    /// <summary>
    /// A dial's arc: the sustained tone when it is a warning or worse; otherwise green under the
    /// amber mark less five, and black in the last five points before it, a reading worth watching
    /// but not yet a warning.
    /// </summary>
    public static string Dial(double? now, (double Amber, double Red) levels, PlanTone sustained)
    {
        if (sustained is PlanTone.Warning or PlanTone.Alarm)
        {
            return Of(sustained);
        }
        return now is not { } value ? "info"
            : value >= levels.Amber - 5 ? "info"
            : "good";
    }

    /// <summary>The window in words, for the chart's title.</summary>
    public static string WindowWords(string window) => window switch
    {
        "7d" => "7 days",
        "30d" => "30 days",
        _ => "24 hours",
    };

    /// <summary>The worse of two tones.</summary>
    public static PlanTone Worse(PlanTone a, PlanTone b) => (PlanTone)Math.Max((int)a, (int)b);
}
