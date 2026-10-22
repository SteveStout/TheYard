-- One post, as it was published on LinkedIn: its line 1 is the title, the body is the text, and the
-- LinkedIn address is the original it was first posted at. Read in date order, newest first.
CREATE TABLE [blog].[Post] (
    [Id]          int            NOT NULL IDENTITY,
    [Slug]        nvarchar(120)  NOT NULL,  -- the post's address on the site, unique
    [Title]       nvarchar(200)  NOT NULL,
    [Summary]     nvarchar(400)  NOT NULL,  -- the one or two lines a list card and the page description carry
    [Body]        nvarchar(max)  NOT NULL,
    [PublishedAt] datetimeoffset NOT NULL,  -- when it went out on LinkedIn
    [LinkedInUrl] nvarchar(400)  NOT NULL,  -- unique
    [CodeUrl]     nvarchar(400)  NULL,      -- the code or the live page the post talks about
    [IsPublished] bit            NOT NULL,
    [CreatedAt]   datetimeoffset NOT NULL,
    [UpdatedAt]   datetimeoffset NOT NULL,
    CONSTRAINT [PK_Post] PRIMARY KEY ([Id])
);
GO
CREATE UNIQUE INDEX [IX_Post_Slug] ON [blog].[Post] ([Slug]);
GO
CREATE UNIQUE INDEX [IX_Post_LinkedInUrl] ON [blog].[Post] ([LinkedInUrl]);
GO
CREATE INDEX [IX_Post_PublishedAt] ON [blog].[Post] ([PublishedAt]);
GO
