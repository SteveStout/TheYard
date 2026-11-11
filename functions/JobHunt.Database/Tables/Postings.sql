-- Every posting the daily board scan reads, one row a posting for good (PROMPT-JOBHUNT-POSTINGS rung 1): keyed by its
-- board and the board's own id, so a re-scan never makes a second row; LastSeenAt moves on every sighting and ClosedAt
-- is set when a scan no longer sees it. The band is the board's own pay field or null, never read out of the text.
-- Written by the Function only (ADR: The campaign sends, the model writes rows).
CREATE TABLE [jobhunt].[Postings] (
    [Id]            bigint         NOT NULL IDENTITY,  -- the PostingKey the change, description and note rows carry
    [Source]        nvarchar(16)   NOT NULL,  -- greenhouse, lever or ashby
    [BoardToken]    nvarchar(100)  NOT NULL,
    [PostingId]     nvarchar(100)  NOT NULL,  -- the board's own id
    [CompanyKey]    nvarchar(253)  NULL,      -- the campaign's key for the board's company, when the scan knows it
    [Title]         nvarchar(300)  NOT NULL,
    [Level]         nvarchar(16)   NOT NULL,  -- PostingLevel: Principal, Staff, Director, Architect, Manager, Lead, Senior, Mid, Unknown
    [RemoteStance]  nvarchar(16)   NOT NULL,  -- Remote, Hybrid, Onsite, Unknown
    [Location]      nvarchar(300)  NOT NULL,
    [CompMin]       int            NULL,
    [CompMax]       int            NULL,
    [CompCurrency]  nvarchar(8)    NULL,
    [CompInterval]  nvarchar(32)   NULL,
    [CompSource]    nvarchar(8)    NULL,      -- ats when the board published the band, null when it published none
    [Url]           nvarchar(500)  NOT NULL,
    [PostedAt]      date           NULL,      -- the day the board says it went up
    [FirstSeenAt]   datetimeoffset NOT NULL,
    [LastSeenAt]    datetimeoffset NOT NULL,
    [ClosedAt]      datetimeoffset NULL,
    [ContentHash]   char(64)       NOT NULL,  -- lower-case hex SHA-256 of the description's text
    [PublishedAt]   datetimeoffset NULL,      -- the instant the board says it went up, when the board gives one
    CONSTRAINT [PK_Postings] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Postings_Source] CHECK ([Source] IN ('greenhouse', 'lever', 'ashby')),
    CONSTRAINT [CK_Postings_RemoteStance] CHECK ([RemoteStance] IN ('Unknown', 'Remote', 'Hybrid', 'Onsite'))
);
GO
CREATE UNIQUE INDEX [UX_Postings_Board] ON [jobhunt].[Postings] ([Source], [BoardToken], [PostingId]);
GO
CREATE INDEX [IX_Postings_LastSeenAt] ON [jobhunt].[Postings] ([LastSeenAt]) WHERE [ClosedAt] IS NULL;
GO
