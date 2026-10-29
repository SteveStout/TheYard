using System.Data;
using Microsoft.Data.SqlClient;

namespace JobHunt.Infrastructure.Features.Merge;

// #region sql-tracker-table
/// <summary>
/// The SQL copy of the tracker in the jobhunt schema (PROMPT-JOBHUNT-TRACKER-SQL, rungs 1 and 2), over
/// Microsoft.Data.SqlClient with the Function's managed identity, the same connection the campaign uses. The first
/// import writes every row in one bulk copy; after it, a pass writes only the rows whose hash changed. Every write is
/// one transaction, so the copy is never half a file.
/// </summary>
/// <param name="connectionString">The database's connection string: managed identity, no secret.</param>
public sealed class SqlTrackerTable(string connectionString, TimeProvider clock) : ITrackerTable
{
    private const string Frame = "SELECT [Record], [Bom], [EndsWithBreak], [Rows], [FileHash], [ParityAt] FROM [jobhunt].[TrackerHeader] WHERE [Id] = 1;";
    private const string Hashes = "SELECT [Position], [RecordHash] FROM [jobhunt].[Tracker];";
    private const string Records = "SELECT [Record] FROM [jobhunt].[Tracker] ORDER BY [Position];";
    private const string Trim = "DELETE FROM [jobhunt].[Tracker] WHERE [Position] > @rows;";
    private const string Upsert = """
        MERGE [jobhunt].[Tracker] AS t
        USING (SELECT @position AS [Position]) AS s ON t.[Position] = s.[Position]
        WHEN MATCHED THEN UPDATE SET [Count] = @count, [Person] = @person, [Company] = @company, [Communication] = @communication, [Date] = @date,
            [PhoneEmail] = @phone, [Notes] = @notes, [Record] = @record, [RecordHash] = @hash, [WrittenAt] = @at
        WHEN NOT MATCHED THEN INSERT ([Position], [Count], [Person], [Company], [Communication], [Date], [PhoneEmail], [Notes], [Record], [RecordHash], [WrittenAt])
            VALUES (@position, @count, @person, @company, @communication, @date, @phone, @notes, @record, @hash, @at);
        """;
    private const string KeepFrame = """
        MERGE [jobhunt].[TrackerHeader] AS t
        USING (SELECT 1 AS [Id]) AS s ON t.[Id] = s.[Id]
        WHEN MATCHED THEN UPDATE SET [Record] = @record, [Bom] = @bom, [EndsWithBreak] = @ends, [Rows] = @rows, [FileHash] = @hash, [WrittenAt] = @at
        WHEN NOT MATCHED THEN INSERT ([Id], [Record], [Bom], [EndsWithBreak], [Rows], [FileHash], [WrittenAt]) VALUES (1, @record, @bom, @ends, @rows, @hash, @at);
        """;
    private const string Parity = "UPDATE [jobhunt].[TrackerHeader] SET [ParityAt] = @at, [ParitySame] = @same WHERE [Id] = 1;";

    /// <inheritdoc />
    public async Task<TrackerFrame?> FrameAsync(CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = new SqlCommand(Frame, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new TrackerFrame(reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetInt32(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetDateTimeOffset(5))
            : null;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, string>> HashesAsync(CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = new SqlCommand(Hashes, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var hashes = new Dictionary<int, string>();
        while (await reader.ReadAsync(ct))
        {
            hashes[reader.GetInt32(0)] = reader.GetString(1);
        }
        return hashes;
    }

    /// <inheritdoc />
    public async Task KeepAsync(TrackerFile file, IReadOnlyList<int> positions, string fileHash, CancellationToken ct)
    {
        var at = clock.GetUtcNow();
        int rows = file.Records.Count - 1;
        await using var connection = await ConnectAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        if (positions.Count > 200)
        {
            // The first import, or a file rewritten wholesale: one bulk copy, after the rows it replaces are gone.
            await using (var clear = new SqlCommand("DELETE FROM [jobhunt].[Tracker] WHERE [Position] IN (SELECT [value] FROM OPENJSON(@positions));", connection, transaction))
            {
                clear.Parameters.AddWithValue("@positions", System.Text.Json.JsonSerializer.Serialize(positions));
                await clear.ExecuteNonQueryAsync(ct);
            }
            using var table = Table(file, positions, at);
            using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, transaction) { DestinationTableName = "[jobhunt].[Tracker]", BulkCopyTimeout = 120 };
            foreach (DataColumn column in table.Columns)
            {
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            }
            await bulk.WriteToServerAsync(table, ct);
        }
        else
        {
            foreach (int position in positions)
            {
                await using var command = new SqlCommand(Upsert, connection, transaction);
                Fill(command.Parameters, position, file.Records[position], at);
                await command.ExecuteNonQueryAsync(ct);
            }
        }
        await using (var trim = new SqlCommand(Trim, connection, transaction))
        {
            trim.Parameters.AddWithValue("@rows", rows);
            await trim.ExecuteNonQueryAsync(ct);
        }
        await using (var frame = new SqlCommand(KeepFrame, connection, transaction))
        {
            frame.Parameters.AddWithValue("@record", file.Records[0]);
            frame.Parameters.AddWithValue("@bom", file.Bom);
            frame.Parameters.AddWithValue("@ends", file.EndsWithBreak);
            frame.Parameters.AddWithValue("@rows", rows);
            frame.Parameters.AddWithValue("@hash", fileHash);
            frame.Parameters.AddWithValue("@at", at);
            await frame.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }

    /// <inheritdoc />
    public async Task<TrackerFile?> RebuildAsync(CancellationToken ct)
    {
        if (await FrameAsync(ct) is not { } frame)
        {
            return null;
        }
        await using var connection = await ConnectAsync(ct);
        await using var command = new SqlCommand(Records, connection) { CommandTimeout = 120 };
        await using var reader = await command.ExecuteReaderAsync(ct);
        var records = new List<string> { frame.Record };
        while (await reader.ReadAsync(ct))
        {
            records.Add(reader.GetString(0));
        }
        return new TrackerFile(frame.Bom, records, frame.EndsWithBreak);
    }

    /// <inheritdoc />
    public async Task MarkParityAsync(bool same, DateTimeOffset at, CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = new SqlCommand(Parity, connection);
        command.Parameters.AddWithValue("@same", same);
        command.Parameters.AddWithValue("@at", at);
        await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>The seven cells of a record as the CSV holds them, padded with empty cells when a record has fewer.</summary>
    /// <param name="record">The record.</param>
    public static string[] Cells(string record)
    {
        string[] cells = TrackerCsvText.Parse(record) is [var only, ..] ? only : [];
        return [.. Enumerable.Range(0, 7).Select(i => i < cells.Length ? cells[i] : string.Empty)];
    }

    private static void Fill(SqlParameterCollection p, int position, string record, DateTimeOffset at)
    {
        string[] c = Cells(record);
        p.AddWithValue("@position", position);
        p.AddWithValue("@count", c[0]);
        p.AddWithValue("@person", c[1]);
        p.AddWithValue("@company", c[2]);
        p.AddWithValue("@communication", c[3]);
        p.AddWithValue("@date", c[4]);
        p.AddWithValue("@phone", c[5]);
        p.AddWithValue("@notes", c[6]);
        p.AddWithValue("@record", record);
        p.AddWithValue("@hash", TrackerFile.Hash(record));
        p.AddWithValue("@at", at);
    }

    private static DataTable Table(TrackerFile file, IReadOnlyList<int> positions, DateTimeOffset at)
    {
        var table = new DataTable();
        foreach (string name in new[] { "Position", "Count", "Person", "Company", "Communication", "Date", "PhoneEmail", "Notes", "Record", "RecordHash", "WrittenAt" })
        {
            table.Columns.Add(name, name switch { "Position" => typeof(int), "WrittenAt" => typeof(DateTimeOffset), _ => typeof(string) });
        }
        foreach (int position in positions)
        {
            string record = file.Records[position];
            string[] c = Cells(record);
            table.Rows.Add(position, c[0], c[1], c[2], c[3], c[4], c[5], c[6], record, TrackerFile.Hash(record), at);
        }
        return table;
    }

    private async Task<SqlConnection> ConnectAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
// #endregion sql-tracker-table
