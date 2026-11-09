-- One row a changed field of a posting (Title, Level, CompMin, CompMax, RemoteStance), and one row a re-post
-- (Field repost_of, OldValue the closed posting's key): where a Staff role re-posted as Senior, or a band cut,
-- is caught by code (PROMPT-JOBHUNT-POSTINGS rung 1).
CREATE TABLE [jobhunt].[PostingChanges] (
    [Id]          bigint         NOT NULL IDENTITY,
    [PostingKey]  bigint         NOT NULL,
    [Field]       nvarchar(32)   NOT NULL,
    [OldValue]    nvarchar(300)  NULL,
    [NewValue]    nvarchar(300)  NULL,
    [ChangedAt]   datetimeoffset NOT NULL,
    CONSTRAINT [PK_PostingChanges] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PostingChanges_Postings] FOREIGN KEY ([PostingKey]) REFERENCES [jobhunt].[Postings] ([Id]),
    CONSTRAINT [CK_PostingChanges_Field] CHECK ([Field] IN ('Title', 'Level', 'CompMin', 'CompMax', 'RemoteStance', 'repost_of'))
);
GO
CREATE INDEX [IX_PostingChanges_PostingKey] ON [jobhunt].[PostingChanges] ([PostingKey]);
GO
