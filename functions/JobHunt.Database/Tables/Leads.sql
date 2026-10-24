-- One hiring manager at one company, written by the lead run (the model writes rows; code sends). Every
-- variable a template names is a column, so the renderer fills from the row and adds nothing. A row that
-- breaks a rule keeps its reason in Blocked and never sends.
CREATE TABLE [campaign].[Leads] (
    [Id]                     bigint         NOT NULL IDENTITY,
    [CompanyId]              int            NOT NULL,
    [Track]                  char(1)        NOT NULL,  -- A: a posted role he applied to; B: a cold note to a .NET shop
    [Company]                nvarchar(200)  NOT NULL,  -- {Company}, the name as the email says it
    [RoleTitle]              nvarchar(200)  NULL,      -- {RoleTitle}, Track A only
    [PostingUrl]             nvarchar(500)  NULL,
    [WhatTheyDo]             nvarchar(110)  NOT NULL,  -- {WhatTheyDo}, held by WhatTheyDoRule
    [FirstName]              nvarchar(60)   NOT NULL,  -- {FirstName}
    [ContactName]            nvarchar(120)  NOT NULL,
    [ContactTitle]           nvarchar(120)  NOT NULL,
    [ContactEmail]           nvarchar(254)  NULL,      -- blank until a public source publishes one
    [EmailSource]            nvarchar(500)  NULL,      -- the URL the address was read from; no source, no send
    [MxChecked]              bit            NULL,      -- null not read, 0 no MX, 1 MX found
    [MxCheckedAt]            datetimeoffset NULL,
    [Verified]               bit            NULL,      -- 0 after a bounce
    [VerifiedReason]         nvarchar(200)  NULL,
    [AppliedAt]              datetimeoffset NULL,      -- Track A: the tracker row's time, rendered as {AppliedWhen}
    [AppliedCount]           int            NULL,      -- the tracker's Count for that application
    [LinkedInInviteSentAt]   datetimeoffset NULL,
    [LinkedInAcceptedAt]     datetimeoffset NULL,
    [ProofKey]               nvarchar(16)   NULL,      -- reliability, platform, leadership, ai
    [FollowUpPostSlug]       nvarchar(120)  NULL,      -- the blog post the second touch carries; none, no follow-up
    [GhostScore]             tinyint        NULL,      -- 0 to 5 (CONVENTIONS, 10/24)
    [BandTop]                int            NULL,      -- the posted band's top, dollars a year
    [FirstSeen]              date           NOT NULL,
    [Strength]               int            NULL,      -- Strength.Score on RankedAt's day
    [StrengthStep]           smallint       NOT NULL,  -- the model's one step, -1 to 1
    [StrengthWhy]            nvarchar(400)  NULL,
    [RankInputsHash]         char(16)       NULL,
    [RankedAt]               datetimeoffset NULL,
    [Blocked]                nvarchar(200)  NULL,      -- the rule that holds it, named
    [RefCode]                char(6)        NULL,      -- make_ref.py's code; the five links below carry it
    [ResumeLink]             nvarchar(200)  NULL,      -- {ResumeLink}
    [SiteLink]               nvarchar(200)  NULL,      -- {SiteLink}
    [GitHubLink]             nvarchar(200)  NULL,      -- {GitHubLink}
    [LinkedInLink]           nvarchar(200)  NULL,      -- {LinkedInLink}
    [AboutLink]              nvarchar(200)  NULL,      -- {AboutLink}
    [CreatedAt]              datetimeoffset NOT NULL,
    [CreatedBy]              nvarchar(64)   NOT NULL,  -- the lead run that wrote it
    [UpdatedAt]              datetimeoffset NOT NULL,
    CONSTRAINT [PK_Leads] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Leads_Companies] FOREIGN KEY ([CompanyId]) REFERENCES [campaign].[Companies] ([Id]),
    CONSTRAINT [CK_Leads_Track] CHECK ([Track] IN ('A', 'B')),
    CONSTRAINT [CK_Leads_ProofKey] CHECK ([ProofKey] IS NULL OR [ProofKey] IN ('reliability', 'platform', 'leadership', 'ai')),
    CONSTRAINT [CK_Leads_GhostScore] CHECK ([GhostScore] IS NULL OR [GhostScore] BETWEEN 0 AND 5),
    CONSTRAINT [CK_Leads_StrengthStep] CHECK ([StrengthStep] BETWEEN -1 AND 1)
);
GO
CREATE INDEX [IX_Leads_CompanyId] ON [campaign].[Leads] ([CompanyId]);
GO
CREATE INDEX [IX_Leads_Open] ON [campaign].[Leads] ([Strength] DESC, [CreatedAt]) WHERE [Blocked] IS NULL;
GO
