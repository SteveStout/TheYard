using Microsoft.Data.SqlClient;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Infrastructure.Features.Admin.Cloud;

/// <summary>
/// The network card's counts from `card.Network`, over the same connection the campaign card reads its schema with:
/// the site's own server, the catalog named by configuration, the managed identity, no secret. The view carries
/// counts only. A refused or absent database, or a view not yet published, is a read that did not happen, with the
/// reason as a sentence.
/// </summary>
/// <param name="connectionString">The database's connection string; null where the site has no SQL server.</param>
public sealed class SqlNetworkCounts(string? connectionString) : INetworkCounts
{
    /// <summary>The one read.</summary>
    public const string Sql = "SELECT [Kind], [Week], [Rows] FROM [card].[Network];";

    /// <inheritdoc />
    public async Task<NetworkCountsRead> ReadAsync(CancellationToken cancellation)
    {
        if (connectionString is null)
        {
            return NetworkCountsRead.Unread("this site has no connection to the network log's database, which only Azure holds");
        }
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellation);
            await using var command = new SqlCommand(Sql, connection) { CommandTimeout = 30 };
            await using var reader = await command.ExecuteReaderAsync(cancellation);
            var weeks = new List<(DateOnly, int)>();
            var states = new Dictionary<string, int>(StringComparer.Ordinal);
            while (await reader.ReadAsync(cancellation))
            {
                string kind = reader.GetString(0);
                if (kind == "started" && !reader.IsDBNull(1))
                {
                    weeks.Add((DateOnly.FromDateTime(reader.GetDateTime(1)), reader.GetInt32(2)));
                }
                else
                {
                    states[kind] = reader.GetInt32(2);
                }
            }
            return new NetworkCountsRead(true, null, [.. weeks.OrderBy(w => w.Item1)], states);
        }
        catch (SqlException ex) when (!cancellation.IsCancellationRequested)
        {
            return NetworkCountsRead.Unread(ex.Number == 208 ? "the network schema is not published yet" : $"the network log's database answered error {ex.Number}");
        }
        catch (InvalidOperationException ex) when (!cancellation.IsCancellationRequested)
        {
            return NetworkCountsRead.Unread("the network log's database could not be opened: " + ex.GetType().Name);
        }
    }
}
