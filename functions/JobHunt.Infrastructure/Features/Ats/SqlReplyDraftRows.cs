using System.Globalization;
using Microsoft.Data.SqlClient;

namespace JobHunt.Infrastructure.Features.Ats;

/// <summary>
/// The reply pass's rows in jobhunt.ReplyDrafts over the connection string the campaign uses (managed identity, no
/// secret). One statement reads the rows of a window's ids, one writes a row or moves it; nothing here deletes.
/// </summary>
public sealed class SqlReplyDraftRows(string connectionString) : IReplyDraftRows
{
    /// <inheritdoc />
    public async Task<bool> ReadyAsync(CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = SqlCommands.Build(connection, ReplyDraftSql.Ready);
        return Convert.ToInt32(await command.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 1;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ReplyDraftRow>> FindAsync(IReadOnlyCollection<string> ids, CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }
        await using var connection = await ConnectAsync(ct);
        await using var command = SqlCommands.Build(connection, ReplyDraftSql.Find, ("@ids", string.Join(',', ids)));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<ReplyDraftRow>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new ReplyDraftRow(reader.GetString(0), Text(reader, 1), reader.GetString(2), Text(reader, 3), Text(reader, 4)));
        }
        return rows;
    }

    /// <inheritdoc />
    public async Task UpsertAsync(ReplyDraftRow row, DateTimeOffset at, CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = SqlCommands.Build(connection, ReplyDraftSql.Upsert,
            ("@id", row.MessageId), ("@thread", row.ThreadId), ("@status", row.Status), ("@reason", Cut(row.Reason, 400)), ("@draft", row.DraftId), ("@at", at));
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string? Text(SqlDataReader reader, int column) => reader.IsDBNull(column) ? null : reader.GetString(column);

    private static string? Cut(string? text, int most) => text is { Length: > 0 } && text.Length > most ? text[..most] : text;

    private async Task<SqlConnection> ConnectAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
