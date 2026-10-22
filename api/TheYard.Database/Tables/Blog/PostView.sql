-- The read log: one row each time a post is opened. Where the reader came from is a word and a host,
-- the tagged link's code when there was one; who they were is a keyed hash that changes every day.
-- No address and no user agent is stored: the device is a class worked out and the agent dropped,
-- the country is the edge's two letters when it sends them. A bot's read is kept and flagged, and
-- every view leaves it out.
CREATE TABLE [blog].[PostView] (
    [Id]           bigint         NOT NULL IDENTITY,
    [PostId]       int            NOT NULL,
    [ViewedAt]     datetimeoffset NOT NULL,
    [Source]       varchar(16)    NOT NULL,  -- linkedin, ref, search, direct
    [RefCode]      char(6)        NULL,      -- the ?r= code from the tagged links
    [ReferrerHost] varchar(120)   NULL,
    [UtmSource]    varchar(60)    NULL,
    [UtmMedium]    varchar(60)    NULL,
    [UtmCampaign]  varchar(60)    NULL,
    [Device]       varchar(8)     NOT NULL,  -- phone, tablet, desk, bot
    [Country]      char(2)        NULL,
    [SessionHash]  binary(32)     NOT NULL,
    [IsBot]        bit            NOT NULL,
    CONSTRAINT [PK_PostView] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PostView_Post] FOREIGN KEY ([PostId]) REFERENCES [blog].[Post] ([Id])
);
GO
CREATE INDEX [IX_PostView_PostId_ViewedAt] ON [blog].[PostView] ([PostId], [ViewedAt]);
GO
