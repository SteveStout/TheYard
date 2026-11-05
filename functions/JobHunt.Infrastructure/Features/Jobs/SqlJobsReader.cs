using System.Globalization;
using Microsoft.Data.SqlClient;

namespace JobHunt.Infrastructure.Features.Jobs;

/// <summary>
/// The picker's reads over the site's database, the connection string the campaign uses (managed identity, no
/// secret): the postings the scan saw open tonight with their company's name, a description for each one asked
/// for, and the tracker's SQL copy. A band counts only when it is in US dollars a year; an hourly or other-currency
/// band reads as none, so the rule that judges it never compares unlike figures. It writes nothing.
/// </summary>
public sealed class SqlJobsReader(string connectionString) : IJobsPostings, ITrackerRows
{
    /// <inheritdoc />
    public async Task<bool> ReadyAsync(CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = SqlCommands.Build(connection, JobsSql.Ready);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 1;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(long Key, Candidate Candidate)>> OpenAsync(DateTimeOffset seenSince, CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = SqlCommands.Build(connection, JobsSql.Open, ("@since", seenSince));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<(long, Candidate)>();
        while (await reader.ReadAsync(ct))
        {
            string kind = reader.GetString(1);
            string company = reader.IsDBNull(3) ? reader.GetString(2) : reader.GetString(3).Split(" | ")[0];
            DateOnly? posted = reader.IsDBNull(6) ? DateOnly.FromDateTime(reader.GetDateTimeOffset(7).UtcDateTime) : DateOnly.FromDateTime(reader.GetDateTime(6));
            bool yearly = !reader.IsDBNull(12) && reader.GetString(12) == "USD" && !reader.IsDBNull(13) && Yearly(reader.GetString(13));
            rows.Add((reader.GetInt64(0), new Candidate("feed:" + kind, company, reader.GetString(4), reader.GetString(5), kind, posted, reader.GetString(8), reader.GetString(9),
                yearly && !reader.IsDBNull(11) ? reader.GetInt32(11) : null, "feed", "")
            {
                BandMin = yearly && !reader.IsDBNull(10) ? reader.GetInt32(10) : null,
            }));
        }
        return rows;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, string>> TextsAsync(IReadOnlyList<long> keys, CancellationToken ct)
    {
        if (keys.Count == 0)
        {
            return new Dictionary<long, string>();
        }
        await using var connection = await ConnectAsync(ct);
        await using var command = SqlCommands.Build(connection, JobsSql.Texts, ("@keys", "[" + string.Join(",", keys.Select(k => k.ToString(CultureInfo.InvariantCulture))) + "]"));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var texts = new Dictionary<long, string>();
        while (await reader.ReadAsync(ct))
        {
            texts[reader.GetInt64(0)] = reader.GetString(1);
        }
        return texts;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string[]>> RowsAsync(CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = SqlCommands.Build(connection, JobsSql.Tracker);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<string[]>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add([reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6)]);
        }
        return rows;
    }

    /// <summary>Whether a board's interval reads as a year: "year", "1 YEAR", "per-year-salary", "yearly", "annual".</summary>
    public static bool Yearly(string interval) =>
        interval.Contains("year", StringComparison.OrdinalIgnoreCase) || interval.Contains("annual", StringComparison.OrdinalIgnoreCase);

    private async Task<SqlConnection> ConnectAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
