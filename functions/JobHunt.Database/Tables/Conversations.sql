-- One person Steve has actually talked to, across both channels: the key a channel reads them under, the key that
-- joins the channels, the two last exchanges, the one clause the 4 AM lane wrote of what was discussed, and where
-- the follow-up stands. The Function is the one writer.
CREATE TABLE [network].[Conversations] (
    [Id]             bigint         NOT NULL IDENTITY,
    [PersonKey]      nvarchar(254)  NOT NULL,  -- the lower-case address, or linkedin:<slug>
    [MatchKey]       nvarchar(300)  NULL,      -- first last|company, the cross-channel join
    [FirstName]      nvarchar(100)  NOT NULL,
    [FullName]       nvarchar(200)  NOT NULL,
    [Company]        nvarchar(200)  NULL,
    [CompanyKey]     nvarchar(253)  NULL,
    [Channel]        nvarchar(8)    NOT NULL,
    [Email]          nvarchar(254)  NULL,      -- the address off a message the person sent, the only To a draft gets
    [ProfileUrl]     nvarchar(300)  NULL,
    [ThreadIds]      nvarchar(1000) NOT NULL,  -- space separated, newest exchange first
    [FirstTwoWayAt]  datetimeoffset NOT NULL,
    [LastInboundAt]  datetimeoffset NULL,
    [LastOutboundAt] datetimeoffset NULL,
    [LastChannel]    nvarchar(8)    NOT NULL,
    [LastSubject]    nvarchar(300)  NULL,
    [TopicLine]      nvarchar(200)  NULL,
    [TopicSource]    nvarchar(300)  NULL,
    [State]          nvarchar(8)    NOT NULL,
    [CloseReason]    nvarchar(64)   NULL,
    [DraftedAt]      datetimeoffset NULL,
    [DraftId]        nvarchar(128)  NULL,
    [DraftChannel]   nvarchar(8)    NULL,
    [FollowUpCount]  int            NOT NULL,
    [Source]         nvarchar(200)  NOT NULL,
    [RefCode]        char(6)        NULL,
    [BlogSlug]       nvarchar(200)  NULL,
    [CreatedAt]      datetimeoffset NOT NULL,
    [UpdatedAt]      datetimeoffset NOT NULL,
    CONSTRAINT [PK_Conversations] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Conversations_Channel] CHECK ([Channel] IN ('Email', 'LinkedIn') AND [LastChannel] IN ('Email', 'LinkedIn')),
    CONSTRAINT [CK_Conversations_State] CHECK ([State] IN ('Open', 'Drafted', 'Quiet', 'Stopped', 'Closed'))
);
GO
CREATE UNIQUE INDEX [UX_Conversations_PersonKey] ON [network].[Conversations] ([PersonKey]);
GO
CREATE INDEX [IX_Conversations_State] ON [network].[Conversations] ([State]);
GO
CREATE INDEX [IX_Conversations_FirstTwoWayAt] ON [network].[Conversations] ([FirstTwoWayAt]);
