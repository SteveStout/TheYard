namespace JobHunt.Infrastructure.Features.Jobs;

/// <summary>The pounce's statements, every column held to the SQL project by PounceTests; the last two are its only writes.</summary>
internal static class PounceSql
{
    internal const string Ready = """
        SELECT CASE WHEN OBJECT_ID(N'[jobhunt].[PounceRoles]') IS NOT NULL THEN 1 ELSE 0 END
        """;

    internal const string Fresh = """
        SELECT p.[Id], p.[Source], p.[BoardToken], c.[Names], p.[Title], p.[Url], p.[PostedAt], p.[FirstSeenAt], p.[Location], p.[RemoteStance],
            p.[CompMin], p.[CompMax], p.[CompCurrency], p.[CompInterval], p.[PublishedAt], p.[CompanyKey]
        FROM [jobhunt].[Postings] p LEFT JOIN [campaign].[Companies] c ON c.[Key] = p.[CompanyKey]
        WHERE p.[FirstSeenAt] >= @since AND NOT EXISTS (SELECT 1 FROM [jobhunt].[PounceRoles] r WHERE r.[PostingKey] = p.[Id])
        """;

    internal const string TrackerSince = """
        SELECT t.[Count], t.[Person], t.[Company], t.[Communication], t.[Date], t.[PhoneEmail], t.[Notes], t.[WrittenAt]
        FROM [jobhunt].[Tracker] t WHERE t.[WrittenAt] >= @since
        """;

    internal const string Open = """
        SELECT r.[PostingKey], r.[Company], r.[LiveAt] FROM [jobhunt].[PounceRoles] r
        WHERE r.[Passed] = 1 AND r.[FirstTouchAt] IS NULL AND r.[LiveAt] >= @since
        """;

    internal const string PassedSince = """
        SELECT r.[Company], p.[Title] FROM [jobhunt].[PounceRoles] r JOIN [jobhunt].[Postings] p ON p.[Id] = r.[PostingKey]
        WHERE r.[Passed] = 1 AND r.[JudgedAt] >= @since
        """;

    internal const string Cohort = """
        SELECT r.[LiveAt], r.[FirstTouchAt] FROM [jobhunt].[PounceRoles] r WHERE r.[Passed] = 1 AND r.[LiveAt] >= @since
        """;

    internal const string Emails = """
        SELECT e.[Company], e.[To], l.[ContactName], e.[SentAt] FROM [campaign].[EmailsSent] e JOIN [campaign].[Leads] l ON l.[Id] = e.[LeadId]
        WHERE e.[Shadow] = 0 AND e.[SentAt] >= @since
        """;

    internal const string Add = """
        INSERT INTO [jobhunt].[PounceRoles] ([PostingKey], [Company], [LiveAt], [LiveKnown], [FirstSeenAt], [JudgedAt], [Passed], [Failed], [BandFlag], [BudgetFlag])
        SELECT @key, @company, @live, @known, @seen, @judged, @passed, @failed, @band, @budget
        WHERE NOT EXISTS (SELECT 1 FROM [jobhunt].[PounceRoles] x WHERE x.[PostingKey] = @key)
        """;

    internal const string Touch = """
        UPDATE [jobhunt].[PounceRoles] SET [FirstTouchAt] = @at, [TouchChannel] = @channel WHERE [PostingKey] = @key AND [FirstTouchAt] IS NULL
        """;
}
