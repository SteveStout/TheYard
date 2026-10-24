-- One company the campaign knows, keyed the way the 30-day rule keys it (CompanyKey: the registrable
-- domain). Written by the lead run; DoNotEmail is set by the inbox pass when a reply asks to stop.
CREATE TABLE [campaign].[Companies] (
    [Id]                     int            NOT NULL IDENTITY,
    [Key]                    nvarchar(253)  NOT NULL,  -- the registrable domain, unique
    [ParentKey]              nvarchar(253)  NULL,      -- the parent's key when a posting or the company page names one
    [Names]                  nvarchar(1000) NOT NULL,  -- the name first, then its variants, separated by " | "
    [RemoteStance]           nvarchar(16)   NOT NULL,  -- Unknown, Remote, Hybrid, Onsite (Strength's RemoteStance)
    [LastDotNetPostingAt]    date           NULL,      -- the newest .NET posting the scan or the leads files saw
    [LastDotNetPostingTitle] nvarchar(200)  NULL,
    [BoardKind]              nvarchar(16)   NULL,      -- greenhouse, lever or ashby: the public board the daily scan reads
    [BoardToken]             nvarchar(100)  NULL,
    [ScannedAt]              datetimeoffset NULL,      -- the daily board scan's last read
    [DoNotEmail]             bit            NOT NULL,
    [DoNotEmailAt]           datetimeoffset NULL,
    [CreatedAt]              datetimeoffset NOT NULL,
    [CreatedBy]              nvarchar(64)   NOT NULL,  -- the run that wrote it
    [UpdatedAt]              datetimeoffset NOT NULL,
    CONSTRAINT [PK_Companies] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Companies_RemoteStance] CHECK ([RemoteStance] IN ('Unknown', 'Remote', 'Hybrid', 'Onsite')),
    CONSTRAINT [CK_Companies_BoardKind] CHECK ([BoardKind] IS NULL OR [BoardKind] IN ('greenhouse', 'lever', 'ashby'))
);
GO
CREATE UNIQUE INDEX [IX_Companies_Key] ON [campaign].[Companies] ([Key]);
GO
CREATE INDEX [IX_Companies_ParentKey] ON [campaign].[Companies] ([ParentKey]) WHERE [ParentKey] IS NOT NULL;
GO
