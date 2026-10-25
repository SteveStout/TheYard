using Microsoft.Data.SqlClient;
using TheYard.Application.Features.Admin.Cloud;

namespace TheYard.Infrastructure.Features.Admin.Cloud;

/// <summary>
/// The campaign card's counts from the `card` schema (ADR-118, Decision 6), over the site's own connection with the
/// catalog named by configuration and the same managed identity: no secret. The schema lives in the site's own
/// database, where the site's user is denied the `campaign` and `jobhunt` schemas, so no name, company or address can
/// reach the card. A refused or
/// absent database is a read that did not happen, with the reason as a sentence.
/// </summary>
/// <param name="connectionString">The campaign database's connection string; null where the site has no SQL server.</param>
public sealed class SqlCampaignCounts(string? connectionString) : ICampaignCounts
{
    /// <summary>The three reads, in one batch.</summary>
    public const string Sql = """
        SELECT [CentralDay], [Sent], [Shadow], [Bounced], [Replies] FROM [card].[Days] ORDER BY [CentralDay];
        SELECT [Open], [WithAddress], [WaitingOnAddress], [Blocked], [NoMx], [Unverified], [Companies], [Stopped], [Boards], [LastScanAt], [FirstWrittenAt], [FirstSentAt], [LastBounceAt] FROM [card].[Leads];
        SELECT [Template], [Weekday], [Track], [ProofKey], [Sent], [Replies] FROM [card].[Weeks];
        """;

    /// <summary>The site's own connection string with the campaign's database as its catalog; null for none.</summary>
    /// <param name="siteConnection">The site's SQL connection string.</param>
    /// <param name="database">The campaign's database.</param>
    public static string? For(string? siteConnection, string database) =>
        string.IsNullOrWhiteSpace(siteConnection) || !siteConnection.Contains("database.windows.net", StringComparison.OrdinalIgnoreCase)
            ? null
            : new SqlConnectionStringBuilder(siteConnection) { InitialCatalog = database }.ConnectionString;

    /// <inheritdoc />
    public async Task<CampaignCountsRead> ReadAsync(CancellationToken cancellation)
    {
        if (connectionString is null)
        {
            return CampaignCountsRead.Unread("this site has no connection to the campaign's database, which only Azure holds");
        }
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(cancellation);
            await using var command = new SqlCommand(Sql, connection) { CommandTimeout = 30 };
            await using var reader = await command.ExecuteReaderAsync(cancellation);
            var days = new List<CampaignDay>();
            while (await reader.ReadAsync(cancellation))
            {
                days.Add(new CampaignDay(DateOnly.FromDateTime(reader.GetDateTime(0)), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4)));
            }
            await reader.NextResultAsync(cancellation);
            CampaignLeadCounts? leads = null;
            if (await reader.ReadAsync(cancellation))
            {
                leads = new CampaignLeadCounts(
                    reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5),
                    reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8), When(reader, 9), When(reader, 10), When(reader, 11), When(reader, 12));
            }
            await reader.NextResultAsync(cancellation);
            var weeks = new List<CampaignWeekRow>();
            while (await reader.ReadAsync(cancellation))
            {
                weeks.Add(new CampaignWeekRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5)));
            }
            return new CampaignCountsRead(true, null, days, leads, weeks);
        }
        catch (SqlException ex) when (!cancellation.IsCancellationRequested)
        {
            return CampaignCountsRead.Unread($"the campaign's database answered error {ex.Number}");
        }
        catch (InvalidOperationException ex) when (!cancellation.IsCancellationRequested)
        {
            return CampaignCountsRead.Unread("the campaign's database could not be opened: " + ex.GetType().Name);
        }
    }

    private static DateTimeOffset? When(SqlDataReader reader, int column) => reader.IsDBNull(column) ? null : reader.GetDateTimeOffset(column);
}
