-- Every campaign email, sent or (Shadow = 1) only written: enough to rebuild what went out without the
-- template (the review, section 3 check 1), the Gmail ids for the bounce and reply joins (F11), and the
-- company's keys and name as they were that day for the 30-day rule. Follow-ups are rows too (Touch 2).
CREATE TABLE [campaign].[EmailsSent] (
    [Id]              bigint         NOT NULL IDENTITY,
    [LeadId]          bigint         NOT NULL,
    [Key]             nvarchar(253)  NOT NULL,
    [ParentKey]       nvarchar(253)  NULL,
    [Company]         nvarchar(200)  NOT NULL,
    [To]              nvarchar(254)  NOT NULL,
    [TemplateId]      nvarchar(16)   NOT NULL,  -- A, B, F (follow-up)
    [TemplateVersion] nvarchar(8)    NOT NULL,  -- v1, v2
    [Subject]         nvarchar(300)  NOT NULL,
    [BodyText]        nvarchar(max)  NOT NULL,
    [BodyHtml]        nvarchar(max)  NOT NULL,
    [SentAt]          datetimeoffset NOT NULL,
    [CentralDay]      date           NOT NULL,  -- the Central day it counts against the cap
    [Touch]           tinyint        NOT NULL,  -- 1 the first email, 2 the follow-up
    [Shadow]          bit            NOT NULL,
    [RefCode]         char(6)        NULL,
    [GmailMessageId]  nvarchar(64)   NULL,
    [GmailThreadId]   nvarchar(64)   NULL,
    [RfcMessageId]    nvarchar(300)  NULL,      -- the Message-Id header a delivery failure quotes back
    [Bounced]         bit            NOT NULL,
    [BounceReason]    nvarchar(400)  NULL,
    [ReplyReceivedAt] datetimeoffset NULL,
    CONSTRAINT [PK_EmailsSent] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_EmailsSent_Leads] FOREIGN KEY ([LeadId]) REFERENCES [campaign].[Leads] ([Id]),
    CONSTRAINT [CK_EmailsSent_Touch] CHECK ([Touch] IN (1, 2))
);
GO
CREATE INDEX [IX_EmailsSent_CentralDay] ON [campaign].[EmailsSent] ([CentralDay], [Shadow]);
GO
CREATE INDEX [IX_EmailsSent_Key] ON [campaign].[EmailsSent] ([Key], [SentAt]);
GO
CREATE UNIQUE INDEX [IX_EmailsSent_Lead_Touch_Sent] ON [campaign].[EmailsSent] ([LeadId], [Touch]) WHERE [Shadow] = 0;
GO
CREATE INDEX [IX_EmailsSent_GmailThreadId] ON [campaign].[EmailsSent] ([GmailThreadId]) WHERE [GmailThreadId] IS NOT NULL;
GO
