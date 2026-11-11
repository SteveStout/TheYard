namespace JobHunt.Infrastructure.Features.Campaign;

/// <summary>
/// The postings' statements and the watchlist's, one constant each, every column held to the SQL project by
/// PostingsSqlTests. A list of keys travels as one JSON array read by OPENJSON, so a board of two hundred unchanged
/// postings is one round trip and not two hundred.
/// </summary>
internal static class PostingsSql
{
    internal const string Ready = """
        SELECT CASE WHEN OBJECT_ID(N'[jobhunt].[Postings]') IS NOT NULL AND OBJECT_ID(N'[jobhunt].[PostingChanges]') IS NOT NULL
            AND OBJECT_ID(N'[jobhunt].[PostingDescriptions]') IS NOT NULL THEN 1 ELSE 0 END
        """;

    internal const string Count = """
        SELECT COUNT(*) FROM [jobhunt].[Postings] p
        """;

    internal const string Board = """
        SELECT p.[Id], p.[PostingId], p.[Title], p.[Level], p.[RemoteStance], p.[CompMin], p.[CompMax], p.[ContentHash], p.[ClosedAt]
        FROM [jobhunt].[Postings] p WHERE p.[Source] = @source AND p.[BoardToken] = @token AND (p.[ClosedAt] IS NULL OR p.[ClosedAt] >= @since)
        """;

    internal const string Known = """
        SELECT p.[Source], p.[BoardToken], p.[PostingId] FROM [jobhunt].[Postings] p
        """;

    internal const string Add = """
        INSERT INTO [jobhunt].[Postings] ([Source], [BoardToken], [PostingId], [CompanyKey], [Title], [Level], [RemoteStance], [Location], [CompMin], [CompMax],
            [CompCurrency], [CompInterval], [CompSource], [Url], [PostedAt], [PublishedAt], [FirstSeenAt], [LastSeenAt], [ContentHash])
        OUTPUT INSERTED.[Id]
        VALUES (@source, @token, @id, @company, @title, @level, @stance, @location, @min, @max, @currency, @interval, @compSource, @url, @posted, @published, @at, @at, @hash)
        """;

    internal const string Update = """
        UPDATE [jobhunt].[Postings] SET [Title] = @title, [Level] = @level, [RemoteStance] = @stance, [Location] = @location, [CompMin] = @min, [CompMax] = @max,
            [CompCurrency] = @currency, [CompInterval] = @interval, [CompSource] = @compSource, [Url] = @url, [PostedAt] = @posted, [ContentHash] = @hash,
            [LastSeenAt] = @at, [ClosedAt] = NULL
        WHERE [Id] = @key
        """;

    internal const string Seen = """
        UPDATE [jobhunt].[Postings] SET [LastSeenAt] = @at, [ClosedAt] = NULL WHERE [Id] IN (SELECT CAST(j.[value] AS bigint) FROM OPENJSON(@keys) j)
        """;

    internal const string Close = """
        UPDATE [jobhunt].[Postings] SET [ClosedAt] = @at WHERE [ClosedAt] IS NULL AND [Id] IN (SELECT CAST(j.[value] AS bigint) FROM OPENJSON(@keys) j)
        """;

    internal const string Change = """
        INSERT INTO [jobhunt].[PostingChanges] ([PostingKey], [Field], [OldValue], [NewValue], [ChangedAt]) VALUES (@key, @field, @old, @new, @at)
        """;

    internal const string Describe = """
        IF NOT EXISTS (SELECT 1 FROM [jobhunt].[PostingDescriptions] d WHERE d.[PostingKey] = @key AND d.[Hash] = @hash)
            INSERT INTO [jobhunt].[PostingDescriptions] ([PostingKey], [Hash], [Text], [SeenAt]) VALUES (@key, @hash, @text, @at)
        """;

    internal const string BoardCount = """
        SELECT COUNT(*) FROM [campaign].[Companies] c WHERE c.[BoardKind] IS NOT NULL AND c.[BoardToken] IS NOT NULL
        """;

    internal const string AddBoard = """
        IF EXISTS (SELECT 1 FROM [campaign].[Companies] c WHERE (c.[BoardKind] = @board AND c.[BoardToken] = @token) OR (c.[Key] = @key AND c.[BoardKind] IS NOT NULL))
            SELECT 0;
        ELSE IF EXISTS (SELECT 1 FROM [campaign].[Companies] c WHERE c.[Key] = @key)
        BEGIN
            UPDATE [campaign].[Companies] SET [BoardKind] = @board, [BoardToken] = @token, [UpdatedAt] = @at WHERE [Key] = @key;
            SELECT 1;
        END
        ELSE
        BEGIN
            INSERT INTO [campaign].[Companies] ([Key], [Names], [RemoteStance], [BoardKind], [BoardToken], [DoNotEmail], [CreatedAt], [CreatedBy], [UpdatedAt])
                VALUES (@key, @names, 'Unknown', @board, @token, 0, @at, @run, @at);
            SELECT 1;
        END
        """;
}
