// The funnel by channel on the Admin tab, as a query (ADR-109, the addendum on the funnel by channel; ADR-115): the
// card schema's Channels view read through the IChannelCounts port, which only Infrastructure opens, and folded into
// the card's weeks by ChannelRules. The handler is tested with a fake port and no database.
using TheYard.Application.Shared;
using TheYard.Domain.Features.Admin;

namespace TheYard.Application.Features.Admin.Cloud;

/// <summary>One read of the Channels view, or why there is none.</summary>
/// <param name="Read">True when the database answered.</param>
/// <param name="Note">Why it was not read; null when it was.</param>
/// <param name="Rows">One row a channel a week.</param>
public sealed record ChannelCountsRead(bool Read, string? Note, IReadOnlyList<ChannelWeek> Rows)
{
    /// <summary>A read that did not happen, with why.</summary>
    /// <param name="note">The sentence the card prints.</param>
    public static ChannelCountsRead Unread(string note) => new(false, note, []);
}

/// <summary>Port: the Channels view in the card schema, read through the site's identity, which may read that schema and nothing else.</summary>
public interface IChannelCounts
{
    /// <summary>Where the rows are read, in words, for the card's source line.</summary>
    string Source { get; }

    /// <summary>The rows, or why they could not be read.</summary>
    /// <param name="cancellation">Stops the read.</param>
    Task<ChannelCountsRead> ReadAsync(CancellationToken cancellation);
}

/// <summary>The card's funnel by channel; it takes nothing.</summary>
public sealed record ReadChannelsQuery;

/// <summary>The funnel by channel card's wire.</summary>
/// <param name="Available">False when nothing was read; the note says why.</param>
/// <param name="Note">Why nothing was read, or what the card does not count yet.</param>
/// <param name="Channels">The six channels, in the card's order.</param>
/// <param name="Weeks">The newest weeks first, each with the six channels.</param>
/// <param name="Source">Where the rows are read.</param>
public sealed record ChannelsReport(bool Available, string? Note, IReadOnlyList<string> Channels, IReadOnlyList<ChannelsWeek> Weeks, string Source);

/// <summary>Reads the view and folds it into the card's weeks; a refused or failed read is the card's unread, never the route's 500.</summary>
/// <param name="counts">The view, reading side.</param>
public sealed class ReadChannelsHandler(IChannelCounts counts) : IQueryHandler<ReadChannelsQuery, ChannelsReport>
{
    /// <summary>What the card does not count yet, said on every read.</summary>
    public const string NotYet = "opens and the highest rung are not counted here yet: the site keeps opens by code, and only the share knows which channel a code went out on";

    /// <summary>The weeks, or an empty report that says why.</summary>
    /// <param name="query">Nothing.</param>
    /// <param name="cancellation">The request's cancellation.</param>
    public async Task<ChannelsReport> HandleAsync(ReadChannelsQuery query, CancellationToken cancellation)
    {
        ChannelCountsRead read;
        try
        {
            read = await counts.ReadAsync(cancellation);
        }
        catch (Exception ex) when (!cancellation.IsCancellationRequested)
        {
            read = ChannelCountsRead.Unread($"the funnel by channel could not be read ({ex.GetType().Name})");
        }
        return read.Read
            ? new ChannelsReport(true, NotYet, ChannelRules.Channels, ChannelRules.Weeks(read.Rows), counts.Source)
            : new ChannelsReport(false, read.Note, ChannelRules.Channels, [], counts.Source);
    }
}
