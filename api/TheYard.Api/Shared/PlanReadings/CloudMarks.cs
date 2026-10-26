namespace TheYard.Api.Shared.PlanReadings;

/// <summary>
/// Which kind of mark an Activity Log entry is on a card's chart (ADR: One frame for every resource, the addendum on
/// each series' own lines): a restart or maintenance is an `event`, drawn at full height at every window because it
/// is rare and it matters; any other entry the chart shows, a configuration write above all, is a `change`, a counted
/// tick on the time axis. Each roll writes both web apps' configuration, and the Activity Log names no caller on the
/// finished entry, so a week of rolls was drawn as two hundred full-height red lines read as Azure's own doing.
/// </summary>
public static class CloudMarks
{
    /// <summary>The mark's kind for an Activity Log entry.</summary>
    /// <param name="entry">The entry.</param>
    public static string KindOf(PlanEvent entry) => entry.Kind is "restart" or "maintenance" ? "event" : "change";
}
