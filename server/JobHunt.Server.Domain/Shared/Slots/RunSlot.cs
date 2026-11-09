using System.Globalization;

namespace JobHunt.Server.Domain.Shared.Slots;

/// <summary>The browser lanes, in the order the run works them.</summary>
public enum Lane
{
    /// <summary>The hot-manager feed: reads LinkedIn and the share, writes the day's queue, sends nothing.</summary>
    Feed,

    /// <summary>LinkedIn connection requests.</summary>
    Invites,

    /// <summary>LinkedIn messages to new connections.</summary>
    Messages,

    /// <summary>Job applications in Chrome.</summary>
    Apply,
}

/// <summary>
/// What a lane is called on the share, and where its run folder goes: nightly/&lt;date&gt;/0600-&lt;lane&gt;-&lt;model short&gt;,
/// the shape nightly.yml names its own folders in, with -dry on the end of a dry run. Central time decides the
/// date, because a run at six in the morning belongs to the day it is read on.
/// </summary>
public static class RunSlot
{
    /// <summary>The slot's four digits in a folder's name.</summary>
    public const string Hour = "0600";

    /// <summary>The lanes in run order.</summary>
    public static readonly IReadOnlyList<Lane> Order = [Lane.Feed, Lane.Invites, Lane.Messages, Lane.Apply];

    private static readonly TimeZoneInfo Central = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    /// <summary>The lane's word in folder names, commands and logs.</summary>
    /// <param name="lane">The lane.</param>
    public static string Word(Lane lane) => lane switch
    {
        Lane.Feed => "feed",
        Lane.Invites => "invites",
        Lane.Messages => "messages",
        _ => "apply",
    };

    /// <summary>The lane doc's file name under nightly/ on the share.</summary>
    /// <param name="lane">The lane.</param>
    public static string Doc(Lane lane) => lane switch
    {
        Lane.Feed => "FEED-LANE.md",
        Lane.Invites => "LINKEDIN-INVITES-LANE.md",
        Lane.Messages => "LINKEDIN-MESSAGES-LANE.md",
        _ => "APPLY-LANE.md",
    };

    /// <summary>
    /// True for a lane that sends on LinkedIn, the two a second driver or a stop condition closes. The feed reads the site
    /// and sends nothing, so a stop in it never closes the day; the invites lane's own gates still hold.
    /// </summary>
    /// <param name="lane">The lane.</param>
    public static bool OnLinkedIn(Lane lane) => lane is Lane.Invites or Lane.Messages;

    /// <summary>The lane a word names, or null.</summary>
    /// <param name="word">A word from the command line.</param>
    public static Lane? Parse(string word) =>
        Order.Cast<Lane?>().FirstOrDefault(lane => string.Equals(Word(lane!.Value), word, StringComparison.OrdinalIgnoreCase));

    /// <summary>A model id with the claude- prefix cut, as nightly.yml cuts it.</summary>
    /// <param name="model">The model id.</param>
    public static string ModelShort(string model) =>
        model.StartsWith("claude-", StringComparison.Ordinal) ? model["claude-".Length..] : model;

    /// <summary>The instant in Central time.</summary>
    /// <param name="utc">The instant.</param>
    public static DateTime Local(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Central).DateTime;

    /// <summary>The Central calendar day of the instant.</summary>
    /// <param name="utc">The instant.</param>
    public static DateOnly Day(DateTimeOffset utc) => DateOnly.FromDateTime(Local(utc));

    /// <summary>The day's folder, relative to the share's root.</summary>
    /// <param name="day">The Central day.</param>
    public static string DayFolder(DateOnly day) => $"nightly/{day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    /// <summary>A lane's run folder, relative to the share's root.</summary>
    /// <param name="day">The Central day.</param>
    /// <param name="lane">The lane.</param>
    /// <param name="model">The model id.</param>
    /// <param name="dry">True for a dry run.</param>
    public static string RunFolder(DateOnly day, Lane lane, string model, bool dry) =>
        $"{DayFolder(day)}/{Hour}-{Word(lane)}-{ModelShort(model)}{(dry ? "-dry" : string.Empty)}";

    /// <summary>The file that says LinkedIn is closed for the day, which every later run that day reads.</summary>
    /// <param name="day">The Central day.</param>
    public static string LinkedInClosed(DateOnly day) => $"{DayFolder(day)}/{Hour}-linkedin-closed.md";

    /// <summary>The UTC stamp a spool file's name starts with.</summary>
    /// <param name="utc">The instant.</param>
    public static string Stamp(DateTimeOffset utc) => utc.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "Z";
}
