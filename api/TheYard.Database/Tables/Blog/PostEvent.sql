-- What a reader did on a post: how far they scrolled (25, 50, 75, 100), how long the page was open in
-- seconds, see more opened, each carousel slide seen, each click out (LinkedIn, the code, a link in the
-- body), the link copied. One row each, tied to the read by the same day's keyed hash.
CREATE TABLE [blog].[PostEvent] (
    [Id]          bigint         NOT NULL IDENTITY,
    [PostId]      int            NOT NULL,
    [SessionHash] binary(32)     NOT NULL,
    [At]          datetimeoffset NOT NULL,
    [Type]        varchar(16)    NOT NULL,  -- depth, time, more, slide, click, copy
    [Value]       nvarchar(200)  NOT NULL,  -- 25/50/75/100, seconds, the slide's number, where the click went
    [IsBot]       bit            NOT NULL,
    CONSTRAINT [PK_PostEvent] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_PostEvent_Post] FOREIGN KEY ([PostId]) REFERENCES [blog].[Post] ([Id])
);
GO
CREATE INDEX [IX_PostEvent_PostId_Type] ON [blog].[PostEvent] ([PostId], [Type]);
GO
