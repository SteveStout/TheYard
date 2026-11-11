// The funnel by channel as the Admin tab shows it (ADR-109, the addendum on the funnel by channel): the card schema's
// Channels view gives one row a channel a week, and the card shows the newest weeks first, every week with the six
// channels in one fixed order, a channel with no row that week a row of zeros. Pure: the rows go in, the weeks come out.
namespace TheYard.Domain.Features.Admin;

/// <summary>One channel's week as the view counts it.</summary>
/// <param name="Channel">pounce, hotmgr, email campaign, old contacts, applications or recruiters.</param>
/// <param name="Week">The Monday the week starts on.</param>
/// <param name="Touches">Sends and applications.</param>
/// <param name="Replies">Replies from people.</param>
/// <param name="Rooms">Interviews and booked calls.</param>
public sealed record ChannelWeek(string Channel, DateOnly Week, int Touches, int Replies, int Rooms);

/// <summary>One channel's counts inside a week of the card.</summary>
/// <param name="Channel">The channel.</param>
/// <param name="Touches">Sends and applications.</param>
/// <param name="Replies">Replies from people.</param>
/// <param name="Rooms">Interviews and booked calls.</param>
public sealed record ChannelCell(string Channel, int Touches, int Replies, int Rooms);

/// <summary>One week of the card: its Monday and the six channels in order.</summary>
/// <param name="Week">The Monday, yyyy-MM-dd.</param>
/// <param name="Channels">The six channels, in the card's order.</param>
public sealed record ChannelsWeek(string Week, IReadOnlyList<ChannelCell> Channels);

/// <summary>The six channels in the card's order, and the fold of the view's rows into the card's weeks.</summary>
public static class ChannelRules
{
    /// <summary>The channels, in the order the card shows them.</summary>
    public static readonly IReadOnlyList<string> Channels = ["pounce", "hotmgr", "email campaign", "old contacts", "applications", "recruiters"];

    /// <summary>The most weeks the card shows.</summary>
    public const int MostWeeks = 8;

    /// <summary>The weeks the rows cover, newest first, at most eight, each with the six channels; a row for any other channel is left out.</summary>
    /// <param name="rows">The view's rows.</param>
    public static IReadOnlyList<ChannelsWeek> Weeks(IReadOnlyList<ChannelWeek> rows)
    {
        var known = rows.Where(row => Channels.Contains(row.Channel, StringComparer.Ordinal)).ToList();
        return [.. known.Select(row => row.Week).Distinct().OrderDescending().Take(MostWeeks).Select(week => new ChannelsWeek(
            week.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            [.. Channels.Select(channel => known.Where(row => row.Week == week && row.Channel == channel)
                .Aggregate(new ChannelCell(channel, 0, 0, 0), (cell, row) => cell with { Touches = cell.Touches + row.Touches, Replies = cell.Replies + row.Replies, Rooms = cell.Rooms + row.Rooms }))]))];
    }
}
