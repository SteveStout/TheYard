-- The named products and ideas a post really talks about, worked out on save by the Domain's keyword rules
-- (the list, the aliases and the minimum mentions are KeywordRules'): a keyword row is kept when the title
-- names it or the text names it at least its minimum. The keyword chips filter on it and count it.
CREATE TABLE [blog].[PostKeyword] (
    [PostId]   int         NOT NULL,
    [Keyword]  varchar(40) NOT NULL,  -- the keyword's key, as the address carries it: copilot, sql, onion
    [Mentions] int         NOT NULL,
    CONSTRAINT [PK_PostKeyword] PRIMARY KEY ([PostId], [Keyword]),
    CONSTRAINT [FK_PostKeyword_Post] FOREIGN KEY ([PostId]) REFERENCES [blog].[Post] ([Id]) ON DELETE CASCADE
);
GO
-- A keyword chip: the posts with that keyword, and its count among the posts the other filters leave.
CREATE INDEX [IX_PostKeyword_Keyword] ON [blog].[PostKeyword] ([Keyword], [PostId]);
GO
