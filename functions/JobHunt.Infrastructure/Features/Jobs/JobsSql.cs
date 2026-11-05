namespace JobHunt.Infrastructure.Features.Jobs;

/// <summary>The picker's statements, reads only, every column held to the SQL project by JobsTests.</summary>
internal static class JobsSql
{
    internal const string Ready = """
        SELECT CASE WHEN OBJECT_ID(N'[jobhunt].[Postings]') IS NOT NULL AND OBJECT_ID(N'[jobhunt].[PostingDescriptions]') IS NOT NULL THEN 1 ELSE 0 END
        """;

    internal const string Open = """
        SELECT p.[Id], p.[Source], p.[BoardToken], c.[Names], p.[Title], p.[Url], p.[PostedAt], p.[FirstSeenAt], p.[Location], p.[RemoteStance],
            p.[CompMin], p.[CompMax], p.[CompCurrency], p.[CompInterval]
        FROM [jobhunt].[Postings] p LEFT JOIN [campaign].[Companies] c ON c.[Key] = p.[CompanyKey]
        WHERE p.[ClosedAt] IS NULL AND p.[LastSeenAt] >= @since
        """;

    internal const string Texts = """
        SELECT d.[PostingKey], d.[Text] FROM [jobhunt].[PostingDescriptions] d
        WHERE d.[PostingKey] IN (SELECT CAST(j.[value] AS bigint) FROM OPENJSON(@keys) j)
            AND d.[Id] = (SELECT MAX(n.[Id]) FROM [jobhunt].[PostingDescriptions] n WHERE n.[PostingKey] = d.[PostingKey])
        """;

    internal const string Tracker = """
        SELECT t.[Count], t.[Person], t.[Company], t.[Communication], t.[Date], t.[PhoneEmail], t.[Notes] FROM [jobhunt].[Tracker] t ORDER BY t.[Position]
        """;
}
