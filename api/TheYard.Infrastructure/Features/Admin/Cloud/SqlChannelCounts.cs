using System.Data;
using Microsoft.Data.SqlClient;
using TheYard.Application.Features.Admin.Cloud;
using TheYard.Domain.Features.Admin;

namespace TheYard.Infrastructure.Features.Admin.Cloud;

/// <summary>
/// The funnel by channel from `card.Channels`, over the same connection the campaign and network cards read their
/// schema with: the site's own server, the catalog named by configuration, the managed identity, no secret. The view
/// carries counts only. A refused or absent database, or a view not yet published, is a read that did not happen,
/// with the reason as a sentence.
/// </summary>
/// <param name="connectionString">The database's connection string; null where the site has no SQL server.</param>
public sealed class SqlChannelCounts(string? connectionString) : IChannelCounts
{
    /// <summary>The one read.</summary>
    public const string Sql = "SELECT [Channel], [Week], [Touches], [Replies], [Rooms] FROM [card].[Channels];";

    /// <inheritdoc />
    public string Source => "the card schema's Channels view, from the tracker's rows";

    /// <inheritdoc />
    public async Task<ChannelCountsRead> ReadAsync(CancellationToken cancellation)
    {
        if (connectionString is null)
        {
            return ChannelCountsRead.Unread("this site has no connection to the job hunt's database, which only Azure holds");
        }
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellation);
            await using var command = new SqlCommand(Sql, connection) { CommandTimeout = 30 };
            await using var reader = await command.ExecuteReaderAsync(cancellation);
            var rows = new List<ChannelWeek>();
            while (await reader.ReadAsync(cancellation))
            {
                if (Row(reader) is { } row)
                {
                    rows.Add(row);
                }
            }
            return new ChannelCountsRead(true, null, rows);
        }
        catch (SqlException ex) when (!cancellation.IsCancellationRequested)
        {
            return ChannelCountsRead.Unread(ex.Number == 208 ? "the funnel by channel's view is not published yet" : $"the job hunt's database answered error {ex.Number}");
        }
        catch (InvalidOperationException ex) when (!cancellation.IsCancellationRequested)
        {
            return ChannelCountsRead.Unread("the job hunt's database could not be opened: " + ex.GetType().Name);
        }
    }

    /// <summary>
    /// One row of the view as a channel's week, or null for a row the view gave no channel: it is left out, as a channel
    /// outside the six is, so one row the view could not attribute never makes the whole read unread.
    /// </summary>
    /// <param name="record">The reader, on the row.</param>
    public static ChannelWeek? Row(IDataRecord record) =>
        record.IsDBNull(0) ? null : new(record.GetString(0), DateOnly.FromDateTime(record.GetDateTime(1)), record.GetInt32(2), record.GetInt32(3), record.GetInt32(4));
}
