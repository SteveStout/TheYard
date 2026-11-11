using System.Globalization;
using Microsoft.Data.SqlClient;

namespace JobHunt.Infrastructure.Features.Jobs;

/// <summary>
/// The pounce's rows in jobhunt.PounceRoles and its read of the campaign's sends, over the connection string the
/// campaign uses (managed identity, no secret). A posting already judged keeps its row, and a first touch once set is
/// never moved: both are held in the statements, so two ticks at once cannot write either twice.
/// </summary>
public sealed class SqlPounceBook(string connectionString) : IPounceBook
{
    /// <inheritdoc />
    public async Task<bool> ReadyAsync(CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = SqlCommands.Build(connection, PounceSql.Ready);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 1;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OpenRole>> OpenAsync(DateTimeOffset liveSince, CancellationToken ct) =>
        await ReadAsync(PounceSql.Open, liveSince, r => new OpenRole(r.GetInt64(0), r.GetString(1), r.GetDateTimeOffset(2)), ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<(string Company, string Title)>> PassedSinceAsync(DateTimeOffset judgedSince, CancellationToken ct) =>
        await ReadAsync(PounceSql.PassedSince, judgedSince, r => (r.GetString(0), r.GetString(1)), ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<(DateTimeOffset Live, DateTimeOffset? Touch)>> CohortAsync(DateTimeOffset liveSince, CancellationToken ct) =>
        await ReadAsync(PounceSql.Cohort, liveSince, r => (r.GetDateTimeOffset(0), r.IsDBNull(1) ? (DateTimeOffset?)null : r.GetDateTimeOffset(1)), ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<EmailTouch>> EmailsSinceAsync(DateTimeOffset since, CancellationToken ct) =>
        await ReadAsync(PounceSql.Emails, since, r => new EmailTouch(r.GetString(0), r.GetString(1), r.GetString(2), r.GetDateTimeOffset(3)), ct);

    /// <inheritdoc />
    public async Task AddAsync(IReadOnlyList<PounceRole> roles, CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        foreach (var role in roles)
        {
            await using var command = SqlCommands.Build(connection, PounceSql.Add,
                ("@key", role.Key), ("@company", role.Company.Length > 200 ? role.Company[..200] : role.Company), ("@live", role.LiveAt), ("@known", role.LiveKnown),
                ("@seen", role.FirstSeenAt), ("@judged", role.JudgedAt), ("@passed", role.Passed), ("@failed", role.Failed), ("@band", role.BandFlag), ("@budget", role.BudgetFlag));
            await command.ExecuteNonQueryAsync(ct);
        }
    }

    /// <inheritdoc />
    public async Task MarkTouchedAsync(long key, DateTimeOffset at, string channel, CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = SqlCommands.Build(connection, PounceSql.Touch, ("@key", key), ("@at", at), ("@channel", channel));
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<IReadOnlyList<T>> ReadAsync<T>(string sql, DateTimeOffset since, Func<SqlDataReader, T> row, CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = SqlCommands.Build(connection, sql, ("@since", since));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<T>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(row(reader));
        }
        return rows;
    }

    private async Task<SqlConnection> ConnectAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
