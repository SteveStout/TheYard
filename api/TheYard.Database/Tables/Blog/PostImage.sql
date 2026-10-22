-- A post's images in order: the carousel's slides and any picture in the body. The file itself lives in
-- the site's blob container; this row keeps where, how big, and what it says to a screen reader.
CREATE TABLE [blog].[PostImage] (
    [PostId]   int           NOT NULL,
    [Position] int           NOT NULL,
    [Url]      nvarchar(400) NOT NULL,
    [AltText]  nvarchar(300) NOT NULL,
    [Width]    int           NOT NULL,
    [Height]   int           NOT NULL,
    CONSTRAINT [PK_PostImage] PRIMARY KEY ([PostId], [Position]),
    CONSTRAINT [FK_PostImage_Post] FOREIGN KEY ([PostId]) REFERENCES [blog].[Post] ([Id]) ON DELETE CASCADE
);
GO
