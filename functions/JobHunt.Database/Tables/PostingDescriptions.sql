-- A posting's description, written on first sight and when its hash changes only, so a day of scans adds rows for
-- what moved and nothing else (PROMPT-JOBHUNT-POSTINGS rung 1).
CREATE TABLE [jobhunt].[PostingDescriptions] (
    [Id]          bigint         NOT NULL IDENTITY,
    [PostingKey]  bigint         NOT NULL,
    [Hash]        char(64)       NOT NULL,
    [Text]        nvarchar(max)  NOT NULL,
    [SeenAt]      datetimeoffset NOT NULL,
    CONSTRAINT [PK_PostingDescriptions] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PostingDescriptions_Postings] FOREIGN KEY ([PostingKey]) REFERENCES [jobhunt].[Postings] ([Id])
);
GO
CREATE UNIQUE INDEX [UX_PostingDescriptions_Hash] ON [jobhunt].[PostingDescriptions] ([PostingKey], [Hash]);
GO
