using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Tests;

/// <summary>
/// The funnel by channel on the Admin tab (ADR-109, the addendum on the funnel by channel): the card schema's Channels
/// view folded into weeks, newest first, every week with the six channels in one order and zeros where a channel had
/// nothing; and a refused or failed read is the card's unread, never a 500.
/// </summary>
public sealed class ChannelsTests
{
    private static readonly DateOnly Monday = new(2026, 11, 9);

    [Fact]
    public void The_weeks_come_newest_first_each_with_the_six_channels_in_order_and_zeros_where_none()
    {
        var weeks = ChannelRules.Weeks([
            new ChannelWeek("hotmgr", Monday.AddDays(-7), 40, 2, 0),
            new ChannelWeek("applications", Monday, 5, 0, 1),
            new ChannelWeek("pounce", Monday, 1, 0, 0),
            new ChannelWeek("hotmgr", Monday, 12, 1, 0),
            new ChannelWeek("chat lanes", Monday, 9, 9, 9),
        ]);

        Assert.Equal(["2026-11-09", "2026-11-02"], weeks.Select(week => week.Week));
        Assert.Equal(["pounce", "hotmgr", "email campaign", "old contacts", "applications", "recruiters"], weeks[0].Channels.Select(cell => cell.Channel));
        Assert.Equal(new ChannelCell("hotmgr", 12, 1, 0), weeks[0].Channels[1]);
        Assert.Equal(new ChannelCell("applications", 5, 0, 1), weeks[0].Channels[4]);
        Assert.Equal(new ChannelCell("recruiters", 0, 0, 0), weeks[1].Channels[5]);
        Assert.Equal(40, weeks[1].Channels[1].Touches);
        Assert.Equal(ChannelRules.MostWeeks, ChannelRules.Weeks([.. Enumerable.Range(0, 12).Select(back => new ChannelWeek("pounce", Monday.AddDays(-7 * back), 1, 0, 0))]).Count);
    }

    [Fact]
    public async Task A_read_says_what_it_does_not_count_and_a_refused_read_is_unread_never_a_failure()
    {
        var read = await new ReadChannelsHandler(new Fixed(new ChannelCountsRead(true, null, [new ChannelWeek("recruiters", Monday, 3, 2, 1)]))).HandleAsync(new ReadChannelsQuery(), CancellationToken.None);
        Assert.True(read.Available);
        Assert.Equal(ReadChannelsHandler.NotYet, read.Note);
        Assert.Equal(new ChannelCell("recruiters", 3, 2, 1), Assert.Single(read.Weeks).Channels[5]);

        var refused = await new ReadChannelsHandler(new Fixed(ChannelCountsRead.Unread("the funnel by channel's view is not published yet"))).HandleAsync(new ReadChannelsQuery(), CancellationToken.None);
        Assert.False(refused.Available);
        Assert.Equal("the funnel by channel's view is not published yet", refused.Note);
        Assert.Empty(refused.Weeks);

        var thrown = await new ReadChannelsHandler(new Throws()).HandleAsync(new ReadChannelsQuery(), CancellationToken.None);
        Assert.False(thrown.Available);
        Assert.Contains("could not be read (TimeoutException)", thrown.Note, StringComparison.Ordinal);
        Assert.Contains("[card].[Channels]", SqlChannelCounts.Sql, StringComparison.Ordinal);
    }

    private sealed class Fixed(ChannelCountsRead read) : IChannelCounts
    {
        public string Source => "a fake";

        public Task<ChannelCountsRead> ReadAsync(CancellationToken cancellation) => Task.FromResult(read);
    }

    private sealed class Throws : IChannelCounts
    {
        public string Source => "a fake";

        public Task<ChannelCountsRead> ReadAsync(CancellationToken cancellation) => throw new TimeoutException();
    }
}
