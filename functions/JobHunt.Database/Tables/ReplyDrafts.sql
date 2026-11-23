-- One row a message the reply pass settled (datamove1123 rung A): skipped (not a job thread), queued (its marker and
-- dispatcher item are written), placed (its draft is in the Gmail thread) or stale (the thread changed before the draft
-- was placed). No message text and no draft text: the marker and the draft file on the share hold those. Written by
-- the Function only, beside the files it already writes, which still decide (ADR: The tracker merger runs in Azure).
CREATE TABLE [jobhunt].[ReplyDrafts] (
    [MessageId] varchar(32)    NOT NULL,  -- Gmail's message id
    [ThreadId]  varchar(32)    NULL,
    [Status]    varchar(12)    NOT NULL,  -- skipped, queued, placed, stale
    [Reason]    nvarchar(400)  NULL,      -- why skipped or stale
    [DraftId]   varchar(64)    NULL,      -- Gmail's draft id once placed
    [CreatedAt] datetimeoffset NOT NULL,
    [UpdatedAt] datetimeoffset NOT NULL,
    CONSTRAINT [PK_ReplyDrafts] PRIMARY KEY ([MessageId]),
    CONSTRAINT [CK_ReplyDrafts_Status] CHECK ([Status] IN ('skipped', 'queued', 'placed', 'stale'))
);
GO
CREATE INDEX [IX_ReplyDrafts_Queued] ON [jobhunt].[ReplyDrafts] ([Status]) WHERE [Status] = 'queued';
GO
