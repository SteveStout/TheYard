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

    /// <summary>The network log's LinkedIn half: reads every two-way thread and saves its text, sends nothing.</summary>
    Network,

    /// <summary>Job applications in Chrome.</summary>
    Apply,
}

/// <summary>
/// What a lane is called on the share, and where its run folder goes: nightly/&lt;date&gt;/&lt;slot&gt;-&lt;lane&gt;-&lt;model short&gt;,
/// the shape nightly.yml names its own folders in, with -dry on the end of a dry run. The slot is the morning's 0600,
/// or pounce-&lt;posting key&gt; for a run the pounce started for one caught role. Central time decides the date, because a
/// run at six in the morning belongs to the day it is read on.
/// </summary>
public static class RunSlot
{
    /// <summary>The morning's slot, its four digits in a folder's name.</summary>
    public const string Hour = "0600";

    /// <summary>The start of a slot that runs the program for one caught role; the posting's key follows it.</summary>
    public const string Pounce = "pounce-";

    /// <summary>The lanes in run order.</summary>
    public static readonly IReadOnlyList<Lane> Order = [Lane.Feed, Lane.Invites, Lane.Messages, Lane.Network, Lane.Apply];

    /// <summary>
    /// The lanes a run for one caught role works: the feed finds the leader and writes the role's queue, the invites lane
    /// sends it, and the apply lane applies to that one posting when nothing on its form needs Steve. A message block
    /// the feed queues waits for the morning's messages lane.
    /// </summary>
    public static readonly IReadOnlyList<Lane> RoleOrder = [Lane.Feed, Lane.Invites, Lane.Apply];

    /// <summary>Where the morning's run sits, Central: no slot run starts from its start to its end.</summary>
    public static readonly (TimeSpan From, TimeSpan To) Morning = (new(5, 30, 0), new(7, 0, 0));

    private static readonly TimeZoneInfo Central = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");

    /// <summary>The lane's word in folder names, commands and logs.</summary>
    /// <param name="lane">The lane.</param>
    public static string Word(Lane lane) => lane switch
    {
        Lane.Feed => "feed",
        Lane.Invites => "invites",
        Lane.Messages => "messages",
        Lane.Network => "network",
        _ => "apply",
    };

    /// <summary>The lane doc's file name under nightly/ on the share.</summary>
    /// <param name="lane">The lane.</param>
    public static string Doc(Lane lane) => lane switch
    {
        Lane.Feed => "FEED-LANE.md",
        Lane.Invites => "LINKEDIN-INVITES-LANE.md",
        Lane.Messages => "LINKEDIN-MESSAGES-LANE.md",
        Lane.Network => "LINKEDIN-NETWORK-LANE.md",
        _ => "APPLY-LANE.md",
    };

    /// <summary>
    /// True for a lane that sends on LinkedIn, the two a second driver or a stop condition closes. The feed and the network
    /// lane read the site and send nothing, so a stop in them never closes the day; the invites lane's own gates still hold.
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
    /// <param name="slot">The slot, the morning's by default.</param>
    public static string RunFolder(DateOnly day, Lane lane, string model, bool dry, string slot = Hour) =>
        $"{DayFolder(day)}/{slot}-{Word(lane)}-{ModelShort(model)}{(dry ? "-dry" : string.Empty)}";

    /// <summary>A slot's card, relative to the share's root.</summary>
    /// <param name="day">The Central day.</param>
    /// <param name="slot">The slot.</param>
    public static string Card(DateOnly day, string slot) => $"{DayFolder(day)}/{slot}-card.md";

    /// <summary>True for the morning's slot or pounce- and a posting key of digits, the only names a folder may carry.</summary>
    /// <param name="slot">A slot name from the command line.</param>
    public static bool IsSlot(string slot) =>
        slot == Hour || (slot.StartsWith(Pounce, StringComparison.Ordinal) && slot.Length is > 7 and <= 26 && slot[Pounce.Length..].All(char.IsAsciiDigit));

    /// <summary>
    /// True from 05:30 to 07:00 Central, when the morning's run owns the browser and no slot run starts, and from as far
    /// before 05:30 as <paramref name="lead"/> reaches: a run that could still be working at 05:30 would hold the run lock
    /// the morning needs at 06:00. A lead longer than 05:30 reaches back past midnight into the evening before.
    /// </summary>
    /// <param name="utc">The instant.</param>
    /// <param name="lead">How long the run could last at most.</param>
    public static bool Quiet(DateTimeOffset utc, TimeSpan lead = default)
    {
        TimeSpan now = Local(utc).TimeOfDay, from = Morning.From - lead;
        return from < TimeSpan.Zero ? now >= from + TimeSpan.FromDays(1) || now < Morning.To : now >= from && now < Morning.To;
    }

    /// <summary>The file that says LinkedIn is closed for the day, which every later run that day reads.</summary>
    /// <param name="day">The Central day.</param>
    public static string LinkedInClosed(DateOnly day) => $"{DayFolder(day)}/{Hour}-linkedin-closed.md";

    /// <summary>The UTC stamp a spool file's name starts with.</summary>
    /// <param name="utc">The instant.</param>
    public static string Stamp(DateTimeOffset utc) => utc.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "Z";
}
