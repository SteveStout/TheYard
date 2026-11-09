using Microsoft.Data.SqlClient;

namespace JobHunt.Infrastructure.Features.Campaign;

/// <summary>
/// The Track A feed's reads and writes over the campaign schema, the connection string the campaign uses: the
/// companies with a Track A lead in the window, a company found or written by its key the way the lead import writes
/// one, and a lead written by the lead import's own statement, so a second pass meets the same row.
/// </summary>
public sealed class SqlTrackABook(string connectionString) : ITrackABook
{
    /// <summary>The statement for the window's Track A companies, held to the SQL project by the Track A tests.</summary>
    internal const string TrackAKeys = """
        SELECT DISTINCT c.[Key] FROM [campaign].[Leads] l JOIN [campaign].[Companies] c ON c.[Id] = l.[CompanyId]
        WHERE l.[Track] = 'A' AND l.[AppliedAt] >= @since
        """;

    private readonly SqlCampaignStore campaign = new(connectionString);

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> TrackAKeysAsync(DateTimeOffset since, CancellationToken ct)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using var command = SqlCommands.Build(connection, TrackAKeys, ("@since", since));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var keys = new List<string>();
        while (await reader.ReadAsync(ct))
        {
            keys.Add(reader.GetString(0));
        }
        return keys;
    }

    /// <inheritdoc />
    public async Task<int> CompanyIdAsync(SpoolCompany company, string run, DateTimeOffset at, CancellationToken ct) =>
        await campaign.FindAsync(company.Key, ct) is { } known ? known.Id : await campaign.SaveAsync(company, run, at, ct);

    /// <inheritdoc />
    public Task SaveLeadAsync(int companyId, SpoolLead lead, bool? mx, int strength, string inputsHash, string run, DateTimeOffset at, CancellationToken ct) =>
        campaign.SaveAsync(companyId, lead, mx, strength, inputsHash, null, run, at, ct);
}
