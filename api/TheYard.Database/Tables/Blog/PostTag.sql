-- A post's hashtags, one row each, without the hash.
CREATE TABLE [blog].[PostTag] (
    [PostId] int          NOT NULL,
    [Tag]    nvarchar(60) NOT NULL,
    CONSTRAINT [PK_PostTag] PRIMARY KEY ([PostId], [Tag]),
    CONSTRAINT [FK_PostTag_Post] FOREIGN KEY ([PostId]) REFERENCES [blog].[Post] ([Id]) ON DELETE CASCADE
);
GO
