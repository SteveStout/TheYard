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
    [BodyHtml]       nvarchar(max) NULL,  -- the body drawn to HTML once, on save, so the page needs no Markdown reader
    [ReadingMinutes] int           NULL,  -- the body's words over 230, rounded up, for the cards
    [DerivedWith]    int           NULL,  -- the version of the rules BodyHtml, the minutes, the words and the keywords were worked out with; null for never
    CONSTRAINT [PK_Post] PRIMARY KEY ([Id])
);
GO
CREATE UNIQUE INDEX [IX_Post_Slug] ON [blog].[Post] ([Slug]);
GO
CREATE UNIQUE INDEX [IX_Post_LinkedInUrl] ON [blog].[Post] ([LinkedInUrl]);
GO
-- The list: published posts newest first, a page at a time by keyset, with what a card shows, read from
-- this index alone. Filtered to the published rows, so a draft costs the list nothing.
CREATE INDEX [IX_Post_Published] ON [blog].[Post] ([PublishedAt] DESC, [Id] DESC)
    INCLUDE ([Slug], [Title], [Summary], [ReadingMinutes])
    WHERE [IsPublished] = 1;
GO
