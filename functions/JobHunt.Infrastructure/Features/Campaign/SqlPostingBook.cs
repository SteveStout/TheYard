using System.Globalization;
using Microsoft.Data.SqlClient;

namespace JobHunt.Infrastructure.Features.Campaign;

/// <summary>
/// The postings and the watchlist over the site's database, the connection string the campaign uses (managed
/// identity, no secret). Every statement is a constant in PostingsSql. The tables are asked for before the first
/// write, so a schema not yet published reads as not ready and the scan says so instead of failing.
/// </summary>
public sealed class SqlPostingBook(string connectionString) : IPostingBook, IWatchlist
{
    /// <summary>SQL Server's error for a row a unique constraint already holds.</summary>
    private const int DuplicateKey = 2627;

    /// <summary>SQL Server's error for a row a unique index already holds.</summary>
    private const int DuplicateIndex = 2601;

    /// <inheritdoc />
    public async Task<bool> ReadyAsync(CancellationToken ct) => Convert.ToInt32(await ScalarAsync(PostingsSql.Ready, ct), CultureInfo.InvariantCulture) == 1;

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken ct) => Convert.ToInt32(await ScalarAsync(PostingsSql.Count, ct), CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public async Task<IReadOnlyList<StoredPosting>> BoardAsync(string source, string token, DateTimeOffset closedSince, CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = SqlCommands.Build(connection, PostingsSql.Board, ("@source", source), ("@token", token), ("@since", closedSince));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<StoredPosting>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(new StoredPosting(reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5), reader.IsDBNull(6) ? null : reader.GetInt32(6), reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetDateTimeOffset(8)));
        }
        return rows;
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> KnownAsync(CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = SqlCommands.Build(connection, PostingsSql.Known);
        await using var reader = await command.ExecuteReaderAsync(ct);
        // Case-blind, as the table's unique key compares under the database's collation.
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct))
        {
            known.Add(Posting.Known(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }
        return known;
    }

    /// <inheritdoc />
    public async Task<long?> AddAsync(Posting posting, string? companyKey, DateTimeOffset seenAt, CancellationToken ct)
    {
        try
        {
            return Convert.ToInt64(await ScalarAsync(PostingsSql.Add, ct, [.. Fields(posting), ("@source", posting.Source), ("@token", Clip(posting.BoardToken, 100)), ("@id", Clip(posting.PostingId, 100)),
                ("@company", companyKey is { } k ? Clip(k, 253) : null), ("@published", posting.PublishedAt), ("@at", seenAt)]), CultureInfo.InvariantCulture);
        }
        catch (SqlException ex) when (ex.Number is DuplicateKey or DuplicateIndex)
        {
            // UX_Postings_Board already holds this board's id: the other scan wrote the row first.
            return null;
        }
    }

    /// <inheritdoc />
    public Task UpdateAsync(long key, Posting posting, DateTimeOffset at, CancellationToken ct) =>
        ExecuteAsync(PostingsSql.Update, ct, [.. Fields(posting), ("@key", key), ("@at", at)]);

    /// <inheritdoc />
    public Task SeenAsync(IReadOnlyList<long> keys, DateTimeOffset at, CancellationToken ct) =>
        ExecuteAsync(PostingsSql.Seen, ct, ("@keys", Json(keys)), ("@at", at));

    /// <inheritdoc />
    public Task CloseAsync(IReadOnlyList<long> keys, DateTimeOffset at, CancellationToken ct) =>
        ExecuteAsync(PostingsSql.Close, ct, ("@keys", Json(keys)), ("@at", at));

    /// <inheritdoc />
    public Task ChangeAsync(long key, string field, string? oldValue, string? newValue, DateTimeOffset at, CancellationToken ct) =>
        ExecuteAsync(PostingsSql.Change, ct, ("@key", key), ("@field", field), ("@old", oldValue is { } o ? Clip(o, 300) : null), ("@new", newValue is { } n ? Clip(n, 300) : null), ("@at", at));

    /// <inheritdoc />
    public Task DescribeAsync(long key, string hash, string text, DateTimeOffset at, CancellationToken ct) =>
        ExecuteAsync(PostingsSql.Describe, ct, ("@key", key), ("@hash", hash), ("@text", text), ("@at", at));

    /// <inheritdoc />
    public async Task<int> BoardCountAsync(CancellationToken ct) => Convert.ToInt32(await ScalarAsync(PostingsSql.BoardCount, ct), CultureInfo.InvariantCulture);

    /// <inheritdoc />
    public async Task<bool> AddBoardAsync(BoardRow row, string run, DateTimeOffset at, CancellationToken ct) =>
        Convert.ToInt32(await ScalarAsync(PostingsSql.AddBoard, ct, ("@key", Clip(row.CompanyKey, 253)), ("@names", Clip(row.Company, 1000)), ("@board", row.BoardKind),
            ("@token", Clip(row.BoardToken, 100)), ("@at", at), ("@run", Clip(run, 64))), CultureInfo.InvariantCulture) == 1;

    private static (string, object?)[] Fields(Posting p) =>
    [
        ("@title", Clip(p.Title, 300)), ("@level", p.Level), ("@stance", p.RemoteStance), ("@location", Clip(p.Location, 300)), ("@min", p.CompMin), ("@max", p.CompMax),
        ("@currency", p.CompCurrency is { } c ? Clip(c, 8) : null), ("@interval", p.CompInterval is { } i ? Clip(i, 32) : null), ("@compSource", p.CompSource),
        ("@url", Clip(p.Url, 500)), ("@posted", p.Posted), ("@hash", p.ContentHash),
    ];

    private static string Json(IReadOnlyList<long> keys) => "[" + string.Join(",", keys.Select(k => k.ToString(CultureInfo.InvariantCulture))) + "]";

    private static string Clip(string text, int length) => text.Length <= length ? text : text[..length];

    private async Task<object?> ScalarAsync(string sql, CancellationToken ct, params (string, object?)[] parameters)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = SqlCommands.Build(connection, sql, parameters);
        return await command.ExecuteScalarAsync(ct);
    }

    private async Task ExecuteAsync(string sql, CancellationToken ct, params (string, object?)[] parameters)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = SqlCommands.Build(connection, sql, parameters);
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task<SqlConnection> ConnectAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }
}
