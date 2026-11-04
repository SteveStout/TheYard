using Microsoft.Data.SqlClient;

namespace JobHunt.Infrastructure.Features.NetworkLog;

/// <summary>
/// The network log's rows in the network schema of the site's database, over Microsoft.Data.SqlClient and the
/// Function's managed identity, the connection string the campaign uses. Every statement is a constant in
/// NetworkSql. A missing table answers SQL error 208 on the first statement, which the pass reads as blocked.
/// </summary>
public sealed class SqlConversationStore(string connectionString) : IConversationStore, ICampaignOwnership
{
    /// <summary>The error number SQL Server answers for an object that does not exist.</summary>
    public const int MissingObject = 208;

    /// <inheritdoc />
    public Task<Conversation?> FindAsync(string personKey, CancellationToken ct) => OneAsync(NetworkSql.Find, ct, ("@key", personKey));

    /// <inheritdoc />
    public Task<Conversation?> FindByMatchKeyAsync(string matchKey, CancellationToken ct) => OneAsync(NetworkSql.FindByMatch, ct, ("@match", matchKey));

    /// <inheritdoc />
    public async Task<IReadOnlyList<Conversation>> OpenAsync(CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = SqlCommands.Build(connection, NetworkSql.Open);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<Conversation>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add(From(reader));
        }
        return rows;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DateTimeOffset>> StartsAsync(CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = SqlCommands.Build(connection, NetworkSql.Starts);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var starts = new List<DateTimeOffset>();
        while (await reader.ReadAsync(ct))
        {
            starts.Add(reader.GetDateTimeOffset(0));
        }
        return starts;
    }

    /// <inheritdoc />
    public async Task<PassRow?> PassAsync(DateOnly centralDay, CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = SqlCommands.Build(connection, NetworkSql.Pass, ("@day", centralDay));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct)
            ? new PassRow(DateOnly.FromDateTime(reader.GetDateTime(0)), reader.GetString(1), reader.GetDateTimeOffset(2), reader.IsDBNull(3) ? null : reader.GetDateTimeOffset(3))
            : null;
    }

    /// <inheritdoc />
    public async Task<long> UpsertAsync(Conversation row, DateTimeOffset at, CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = SqlCommands.Build(connection, NetworkSql.Upsert,
            ("@key", row.PersonKey), ("@match", row.MatchKey), ("@first", Clip(row.FirstName, 100)), ("@full", Clip(row.FullName, 200)),
            ("@company", row.Company is { } company ? Clip(company, 200) : null), ("@companyKey", row.CompanyKey), ("@channel", row.Channel.ToString()),
            ("@email", row.Email), ("@profile", row.ProfileUrl), ("@threads", Clip(string.Join(' ', row.ThreadIds), 1000)), ("@firstAt", row.FirstTwoWayAt),
            ("@inAt", row.LastInboundAt), ("@outAt", row.LastOutboundAt), ("@lastChannel", row.LastChannel.ToString()),
            ("@subject", row.LastSubject is { } subject ? Clip(subject, 300) : null), ("@state", row.State.ToString()), ("@reason", row.CloseReason),
            ("@followUps", row.FollowUpCount), ("@source", Clip(row.Source, 200)), ("@now", at));
        return Convert.ToInt64(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <inheritdoc />
    public Task SetStateAsync(long id, ConversationState state, string? reason, CancellationToken ct) =>
        ExecuteAsync(NetworkSql.SetState, ct, ("@id", id), ("@state", state.ToString()), ("@reason", reason));

    /// <inheritdoc />
    public Task SetTopicAsync(long id, string topicLine, string source, CancellationToken ct) =>
        ExecuteAsync(NetworkSql.SetTopic, ct, ("@id", id), ("@topic", Clip(topicLine, 200)), ("@source", Clip(source, 300)));

    /// <inheritdoc />
    public Task SetDraftAsync(long id, string draftId, Channel channel, DateTimeOffset at, CancellationToken ct) =>
        ExecuteAsync(NetworkSql.SetDraft, ct, ("@id", id), ("@draft", Clip(draftId, 128)), ("@channel", channel.ToString()), ("@at", at));

    /// <inheritdoc />
    public Task StartPassAsync(DateOnly centralDay, string mode, DateTimeOffset at, CancellationToken ct) =>
        ExecuteAsync(NetworkSql.StartPass, ct, ("@day", centralDay), ("@mode", mode), ("@at", at));

    /// <inheritdoc />
    public Task FinishPassAsync(DateOnly centralDay, DateTimeOffset at, PassCounts counts, CancellationToken ct) =>
        ExecuteAsync(NetworkSql.FinishPass, ct, ("@day", centralDay), ("@at", at), ("@threads", counts.ThreadsRead), ("@upserted", counts.Upserted),
            ("@stopped", counts.Stopped), ("@quiet", counts.Quiet), ("@drafts", counts.DraftsBuilt), ("@week", counts.StartedThisWeek),
            ("@seconds", counts.Seconds), ("@line", Clip(counts.Line, 1000)));

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> OpenThreadIdsAsync(CancellationToken ct)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = SqlCommands.Build(connection, NetworkSql.CampaignOpenThreads);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct))
        {
            ids.Add(reader.GetString(0));
        }
        return ids;
    }

    /// <summary>True when the exception is SQL Server saying a table or view does not exist: the schema is not published.</summary>
    public static bool IsMissingTable(Exception ex) => ex is SqlException sql && sql.Errors.Cast<SqlError>().Any(e => e.Number == MissingObject);

    private async Task<Conversation?> OneAsync(string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = SqlCommands.Build(connection, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? From(reader) : null;
    }

    private async Task<SqlConnection> ConnectAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private async Task ExecuteAsync(string sql, CancellationToken ct, params (string Name, object? Value)[] parameters)
    {
        await using var connection = await ConnectAsync(ct);
        await using var command = SqlCommands.Build(connection, sql, parameters);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static string Clip(string text, int length) => text.Length <= length ? text : text[..length];

    private static Conversation From(SqlDataReader r) => new()
    {
        Id = r.GetInt64(r.GetOrdinal("Id")),
        PersonKey = r.GetString(r.GetOrdinal("PersonKey")),
        MatchKey = Text(r, "MatchKey"),
        FirstName = r.GetString(r.GetOrdinal("FirstName")),
        FullName = r.GetString(r.GetOrdinal("FullName")),
        Company = Text(r, "Company"),
        CompanyKey = Text(r, "CompanyKey"),
        Channel = Enum.Parse<Channel>(r.GetString(r.GetOrdinal("Channel"))),
        Email = Text(r, "Email"),
        ProfileUrl = Text(r, "ProfileUrl"),
        ThreadIds = r.GetString(r.GetOrdinal("ThreadIds")).Split(' ', StringSplitOptions.RemoveEmptyEntries),
        FirstTwoWayAt = r.GetDateTimeOffset(r.GetOrdinal("FirstTwoWayAt")),
        LastInboundAt = When(r, "LastInboundAt"),
        LastOutboundAt = When(r, "LastOutboundAt"),
        LastChannel = Enum.Parse<Channel>(r.GetString(r.GetOrdinal("LastChannel"))),
        LastSubject = Text(r, "LastSubject"),
        TopicLine = Text(r, "TopicLine"),
        TopicSource = Text(r, "TopicSource"),
        State = Enum.Parse<ConversationState>(r.GetString(r.GetOrdinal("State"))),
        CloseReason = Text(r, "CloseReason"),
        DraftedAt = When(r, "DraftedAt"),
        DraftId = Text(r, "DraftId"),
        DraftChannel = Text(r, "DraftChannel") is { } channel ? Enum.Parse<Channel>(channel) : null,
        FollowUpCount = r.GetInt32(r.GetOrdinal("FollowUpCount")),
        Source = r.GetString(r.GetOrdinal("Source")),
        RefCode = Text(r, "RefCode"),
        BlogSlug = Text(r, "BlogSlug"),
    };

    private static string? Text(SqlDataReader r, string column) => r.IsDBNull(r.GetOrdinal(column)) ? null : r.GetString(r.GetOrdinal(column));

    private static DateTimeOffset? When(SqlDataReader r, string column) => r.IsDBNull(r.GetOrdinal(column)) ? null : r.GetDateTimeOffset(r.GetOrdinal(column));
}
