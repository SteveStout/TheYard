using System.Globalization;

namespace JobHunt.Domain.Shared.Tracker;

/// <summary>
/// Where a week's LinkedIn follow-ups wait for Steve's send: the network log writes the file on a Monday in the reply
/// queue's format 2, every item under section A, and the morning card reads the file for the week it is in beside the
/// day's reply queue. Section A is Steve's, so no lane sends from it.
/// </summary>
public static class FollowUpQueue
{
    /// <summary>The Monday of the week a Central day is in.</summary>
    /// <param name="day">The Central day.</param>
    public static DateOnly Monday(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    /// <summary>The week's queue file, from the share's root.</summary>
    /// <param name="monday">The week's Monday.</param>
    public static string Path(DateOnly monday) => string.Create(CultureInfo.InvariantCulture, $"networklog/linkedin-queue-{monday:yyyy-MM-dd}.md");

    /// <summary>The folder the week's draft texts sit in, from the share's root.</summary>
    /// <param name="monday">The week's Monday.</param>
    public static string Texts(DateOnly monday) => string.Create(CultureInfo.InvariantCulture, $"networklog/texts/{monday:yyyy-MM-dd}");
}
