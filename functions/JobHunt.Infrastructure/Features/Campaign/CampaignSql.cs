namespace JobHunt.Infrastructure.Features.Campaign;

/// <summary>
/// Every statement SqlCampaignStore sends (ADR-118, Decision 2), as constants with their parameters, so
/// CampaignSqlTests can hold every column they name to a column the SQL project declares.
/// </summary>
internal static class CampaignSql
{
    internal const string FindCompany = """
        SELECT c.[Id], c.[Key], c.[ParentKey], c.[Names], c.[RemoteStance], c.[LastDotNetPostingAt], c.[LastDotNetPostingTitle], c.[DoNotEmail],
               (SELECT COUNT(*) FROM [campaign].[CompanyEvidence] v WHERE v.[CompanyId] = c.[Id]) AS [Evidence]
        FROM [campaign].[Companies] c WHERE c.[Key] = @key
        """;

    internal const string DoNotEmail = """
        UPDATE [campaign].[Companies] SET [DoNotEmail] = 1, [DoNotEmailAt] = @at, [UpdatedAt] = @at WHERE [Id] = @id AND [DoNotEmail] = 0
        """;

    internal const string OpenLeads = """
        SELECT l.[Id], l.[Track], l.[Company], l.[RoleTitle], l.[WhatTheyDo], l.[FirstName], l.[ContactName], l.[ContactTitle],
               l.[ContactEmail], l.[EmailSource], l.[MxChecked], l.[Verified], l.[AppliedAt], l.[LinkedInInviteSentAt], l.[LinkedInAcceptedAt],
               l.[ProofKey], l.[FollowUpPostSlug], l.[GhostScore], l.[BandTop], l.[FirstSeen], l.[StrengthStep], l.[RefCode],
               l.[ResumeLink], l.[SiteLink], l.[GitHubLink], l.[LinkedInLink], l.[AboutLink], l.[CreatedAt],
               c.[Id] AS [CompanyId], c.[Key], c.[ParentKey], c.[Names], c.[RemoteStance], c.[LastDotNetPostingAt], c.[LastDotNetPostingTitle], c.[DoNotEmail],
               (SELECT COUNT(*) FROM [campaign].[CompanyEvidence] v WHERE v.[CompanyId] = c.[Id]) AS [Evidence]
        FROM [campaign].[Leads] l JOIN [campaign].[Companies] c ON c.[Id] = l.[CompanyId]
        WHERE l.[Blocked] IS NULL
        ORDER BY l.[CreatedAt], l.[Id]
        """;

    internal const string Block = """
        UPDATE [campaign].[Leads] SET [Blocked] = @reason, [UpdatedAt] = SYSDATETIMEOFFSET() WHERE [Id] = @id
        """;

    internal const string Unverify = """
        UPDATE [campaign].[Leads] SET [Verified] = 0, [VerifiedReason] = @reason, [UpdatedAt] = SYSDATETIMEOFFSET() WHERE [Id] = @id
        """;

    internal const string Since = """
        SELECT e.[LeadId], e.[Key], e.[ParentKey], e.[Company], e.[CentralDay], e.[Shadow] FROM [campaign].[EmailsSent] e WHERE e.[CentralDay] >= @day
        """;

    internal const string Count = """
        SELECT COUNT(*) FROM [campaign].[EmailsSent] e WHERE e.[CentralDay] = @day AND e.[Shadow] = @shadow
        """;

    internal const string Write = """
        INSERT INTO [campaign].[EmailsSent] ([LeadId], [Key], [ParentKey], [Company], [To], [TemplateId], [TemplateVersion], [Subject], [BodyText], [BodyHtml],
            [SentAt], [CentralDay], [Touch], [Shadow], [RefCode], [GmailMessageId], [GmailThreadId], [RfcMessageId], [Bounced])
        OUTPUT INSERTED.[Id]
        VALUES (@lead, @key, @parent, @company, @to, @template, @version, @subject, @text, @html,
            @sent, @day, @touch, @shadow, @ref, @message, @thread, @rfc, 0)
        """;

    internal const string Threads = """
        SELECT e.[Id], e.[LeadId], e.[Key], e.[To], e.[GmailThreadId], e.[CentralDay], e.[Bounced], e.[ReplyReceivedAt] FROM [campaign].[EmailsSent] e
        WHERE e.[CentralDay] >= @day AND e.[Shadow] = 0 AND e.[GmailThreadId] IS NOT NULL
        """;

    internal const string Bounce = """
        UPDATE [campaign].[EmailsSent] SET [Bounced] = 1, [BounceReason] = @reason WHERE [Id] = @id AND [Bounced] = 0
        """;

    internal const string Reply = """
        UPDATE [campaign].[EmailsSent] SET [ReplyReceivedAt] = @at WHERE [GmailThreadId] = @thread AND [Shadow] = 0 AND [ReplyReceivedAt] IS NULL
        """;

    internal const string SaveCompany = """
        MERGE [campaign].[Companies] AS c
        USING (SELECT @key AS [Key]) AS s ON c.[Key] = s.[Key]
        WHEN MATCHED THEN UPDATE SET [ParentKey] = @parent, [Names] = @names, [RemoteStance] = @stance,
            [LastDotNetPostingAt] = COALESCE(@lastAt, c.[LastDotNetPostingAt]), [LastDotNetPostingTitle] = COALESCE(@lastTitle, c.[LastDotNetPostingTitle]),
            [BoardKind] = @board, [BoardToken] = @token, [UpdatedAt] = @at
        WHEN NOT MATCHED THEN INSERT ([Key], [ParentKey], [Names], [RemoteStance], [LastDotNetPostingAt], [LastDotNetPostingTitle], [BoardKind], [BoardToken], [DoNotEmail], [CreatedAt], [CreatedBy], [UpdatedAt])
            VALUES (@key, @parent, @names, @stance, @lastAt, @lastTitle, @board, @token, 0, @at, @run, @at)
        OUTPUT INSERTED.[Id];
        """;

    internal const string SaveEvidence = """
        IF NOT EXISTS (SELECT 1 FROM [campaign].[CompanyEvidence] v WHERE v.[CompanyId] = @company AND v.[Url] = @url)
            INSERT INTO [campaign].[CompanyEvidence] ([CompanyId], [Kind], [Title], [Url], [SeenAt]) VALUES (@company, @kind, @title, @url, @seen)
        """;

    internal const string Boards = """
        SELECT c.[Id], c.[BoardKind], c.[BoardToken] FROM [campaign].[Companies] c WHERE c.[BoardKind] IS NOT NULL AND c.[BoardToken] IS NOT NULL AND c.[DoNotEmail] = 0
        """;

    internal const string Scanned = """
        UPDATE [campaign].[Companies] SET [ScannedAt] = @at, [UpdatedAt] = @at,
            [LastDotNetPostingAt] = COALESCE(@lastAt, [LastDotNetPostingAt]), [LastDotNetPostingTitle] = COALESCE(@lastTitle, [LastDotNetPostingTitle])
        WHERE [Id] = @id
        """;

    internal const string SaveLead = """
        MERGE [campaign].[Leads] AS l
        USING (SELECT @company AS [CompanyId], @contact AS [ContactName]) AS s ON l.[CompanyId] = s.[CompanyId] AND l.[ContactName] = s.[ContactName]
        WHEN MATCHED THEN UPDATE SET [Track] = @track, [Company] = @name, [RoleTitle] = @role, [PostingUrl] = @posting, [WhatTheyDo] = @what,
            [FirstName] = @first, [ContactTitle] = @title, [ContactEmail] = @email, [EmailSource] = @source,
            [MxChecked] = CASE WHEN @email IS NULL THEN NULL WHEN l.[ContactEmail] = @email AND @mx IS NULL THEN l.[MxChecked] ELSE @mx END,
            [MxCheckedAt] = CASE WHEN @mx IS NULL THEN l.[MxCheckedAt] ELSE @at END,
            [Verified] = CASE WHEN l.[ContactEmail] = @email THEN l.[Verified] ELSE NULL END,
            [AppliedAt] = @applied, [AppliedCount] = @count, [LinkedInInviteSentAt] = @invited, [LinkedInAcceptedAt] = @accepted,
            [ProofKey] = @proof, [FollowUpPostSlug] = @slug, [GhostScore] = @ghost, [BandTop] = @band, [Strength] = @strength, [StrengthStep] = @step,
            [StrengthWhy] = COALESCE(@why, l.[StrengthWhy]), [RankInputsHash] = @hash, [RankedAt] = @at, [Blocked] = @blocked,
            [RefCode] = @ref, [ResumeLink] = @resume, [SiteLink] = @site, [GitHubLink] = @github, [LinkedInLink] = @linkedin, [AboutLink] = @about, [UpdatedAt] = @at
        WHEN NOT MATCHED THEN INSERT ([CompanyId], [Track], [Company], [RoleTitle], [PostingUrl], [WhatTheyDo], [FirstName], [ContactName], [ContactTitle],
            [ContactEmail], [EmailSource], [MxChecked], [MxCheckedAt], [AppliedAt], [AppliedCount], [LinkedInInviteSentAt], [LinkedInAcceptedAt], [ProofKey],
            [FollowUpPostSlug], [GhostScore], [BandTop], [FirstSeen], [Strength], [StrengthStep], [StrengthWhy], [RankInputsHash], [RankedAt], [Blocked],
            [RefCode], [ResumeLink], [SiteLink], [GitHubLink], [LinkedInLink], [AboutLink], [CreatedAt], [CreatedBy], [UpdatedAt])
            VALUES (@company, @track, @name, @role, @posting, @what, @first, @contact, @title,
            @email, @source, @mx, CASE WHEN @mx IS NULL THEN NULL ELSE @at END, @applied, @count, @invited, @accepted, @proof,
            @slug, @ghost, @band, @day, @strength, @step, @why, @hash, @at, @blocked,
            @ref, @resume, @site, @github, @linkedin, @about, @at, @run, @at);
        """;
}
