namespace JobHunt.Infrastructure.Features.NetworkLog;

/// <summary>
/// Every statement SqlConversationStore runs, as a constant, so NetworkSqlTests can hold each column it names to
/// a column the SQL project declares. The alias c is network.Conversations and p is network.Passes.
/// </summary>
internal static class NetworkSql
{
    private const string Columns =
        "c.[Id], c.[PersonKey], c.[MatchKey], c.[FirstName], c.[FullName], c.[Company], c.[CompanyKey], c.[Channel], c.[Email], c.[ProfileUrl], " +
        "c.[ThreadIds], c.[FirstTwoWayAt], c.[LastInboundAt], c.[LastOutboundAt], c.[LastChannel], c.[LastSubject], c.[TopicLine], c.[TopicSource], " +
        "c.[State], c.[CloseReason], c.[DraftedAt], c.[DraftId], c.[DraftChannel], c.[FollowUpCount], c.[Source], c.[RefCode], c.[BlogSlug]";

    internal const string Find = "SELECT " + Columns + " FROM [network].[Conversations] c WHERE c.[PersonKey] = @key;";

    internal const string FindByMatch = "SELECT TOP (1) " + Columns + " FROM [network].[Conversations] c WHERE c.[MatchKey] = @match ORDER BY c.[Id];";

    internal const string Open = "SELECT " + Columns + " FROM [network].[Conversations] c WHERE c.[State] IN ('Open', 'Drafted');";

    internal const string Starts = "SELECT c.[FirstTwoWayAt] FROM [network].[Conversations] c;";

    internal const string Upsert = """
        MERGE [network].[Conversations] WITH (HOLDLOCK) AS t
        USING (SELECT @key AS [PersonKey]) AS s ON t.[PersonKey] = s.[PersonKey]
        WHEN MATCHED THEN UPDATE SET
            [MatchKey] = @match, [FirstName] = @first, [FullName] = @full, [Company] = @company, [CompanyKey] = @companyKey,
            [Email] = @email, [ProfileUrl] = @profile, [ThreadIds] = @threads, [FirstTwoWayAt] = @firstAt,
            [LastInboundAt] = @inAt, [LastOutboundAt] = @outAt, [LastChannel] = @lastChannel, [LastSubject] = @subject,
            [State] = @state, [CloseReason] = @reason, [FollowUpCount] = @followUps, [UpdatedAt] = @now
        WHEN NOT MATCHED THEN INSERT
            ([PersonKey], [MatchKey], [FirstName], [FullName], [Company], [CompanyKey], [Channel], [Email], [ProfileUrl], [ThreadIds],
             [FirstTwoWayAt], [LastInboundAt], [LastOutboundAt], [LastChannel], [LastSubject], [State], [CloseReason], [FollowUpCount],
             [Source], [CreatedAt], [UpdatedAt])
            VALUES (@key, @match, @first, @full, @company, @companyKey, @channel, @email, @profile, @threads,
             @firstAt, @inAt, @outAt, @lastChannel, @subject, @state, @reason, @followUps, @source, @now, @now)
        OUTPUT inserted.[Id];
        """;

    internal const string SetState = "UPDATE [network].[Conversations] SET [State] = @state, [CloseReason] = @reason, [UpdatedAt] = SYSDATETIMEOFFSET() WHERE [Id] = @id;";

    internal const string SetTopic = "UPDATE [network].[Conversations] SET [TopicLine] = @topic, [TopicSource] = @source, [UpdatedAt] = SYSDATETIMEOFFSET() WHERE [Id] = @id;";

    internal const string SetBlogLine = "UPDATE [network].[Conversations] SET [BlogSlug] = @slug, [RefCode] = @code, [UpdatedAt] = SYSDATETIMEOFFSET() WHERE [Id] = @id;";

    internal const string SetDraft = "UPDATE [network].[Conversations] SET [State] = 'Drafted', [DraftId] = @draft, [DraftChannel] = @channel, [DraftedAt] = @at, [UpdatedAt] = @at WHERE [Id] = @id;";

    internal const string Pass = "SELECT p.[CentralDay], p.[Mode], p.[StartedAt], p.[FinishedAt] FROM [network].[Passes] p WHERE p.[CentralDay] = @day;";

    internal const string StartPass = """
        MERGE [network].[Passes] WITH (HOLDLOCK) AS t
        USING (SELECT @day AS [CentralDay]) AS s ON t.[CentralDay] = s.[CentralDay]
        WHEN MATCHED THEN UPDATE SET [Mode] = @mode, [StartedAt] = @at, [FinishedAt] = NULL
        WHEN NOT MATCHED THEN INSERT ([CentralDay], [Mode], [StartedAt], [ThreadsRead], [Upserted], [Stopped], [Quiet], [DraftsBuilt], [StartedThisWeek], [Seconds])
            VALUES (@day, @mode, @at, 0, 0, 0, 0, 0, 0, 0);
        """;

    internal const string FinishPass = """
        UPDATE [network].[Passes] SET [FinishedAt] = @at, [ThreadsRead] = @threads, [Upserted] = @upserted, [Stopped] = @stopped,
            [Quiet] = @quiet, [DraftsBuilt] = @drafts, [StartedThisWeek] = @week, [Seconds] = @seconds, [Line] = @line
        WHERE [CentralDay] = @day;
        """;

    internal const string CampaignOpenThreads = "SELECT e.[GmailThreadId] FROM [campaign].[EmailsSent] e WHERE e.[Shadow] = 0 AND e.[ReplyReceivedAt] IS NULL AND e.[GmailThreadId] IS NOT NULL;";
}
