-- What is learned about a posting later (a hiring manager, an application, an interview, an outcome), appended and
-- never rewritten; nothing in the scan writes it (PROMPT-JOBHUNT-POSTINGS rung 1).
CREATE TABLE [jobhunt].[PostingNotes] (
    [Id]          bigint         NOT NULL IDENTITY,
    [PostingKey]  bigint         NOT NULL,
    [Kind]        nvarchar(16)   NOT NULL,
    [Value]       nvarchar(1000) NOT NULL,
    [Source]      nvarchar(300)  NOT NULL,
    [WrittenAt]   datetimeoffset NOT NULL,
    CONSTRAINT [PK_PostingNotes] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PostingNotes_Postings] FOREIGN KEY ([PostingKey]) REFERENCES [jobhunt].[Postings] ([Id]),
    CONSTRAINT [CK_PostingNotes_Kind] CHECK ([Kind] IN ('hiring_manager', 'applied', 'interview', 'outcome', 'note'))
);
GO
