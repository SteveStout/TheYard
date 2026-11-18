-- The words of a post, worked out on save by the Domain's search rules: each word of the title, the summary
-- and the full body's text with how many times it is there, and each pair of neighbouring words, so a typed
-- phrase such as "microsoft copilot" is one lookup. A search for "copil" is Word LIKE 'copil%', a seek on
-- the key; the counts come back per post and per field, and the Domain weighs them. Derived, never typed:
-- a post's rows are replaced whole on every save, and again when the rules change.
CREATE TABLE [blog].[PostWord] (
    [Word]     nvarchar(80) NOT NULL,  -- lower case, a word or two words with one space
    [PostId]   int          NOT NULL,
    [Field]    tinyint      NOT NULL,  -- 1 title, 2 summary, 3 body
    [Mentions] int          NOT NULL,
    CONSTRAINT [PK_PostWord] PRIMARY KEY ([Word], [PostId], [Field]),
    CONSTRAINT [FK_PostWord_Post] FOREIGN KEY ([PostId]) REFERENCES [blog].[Post] ([Id]) ON DELETE CASCADE
);
GO
-- A save replaces one post's words, found by the post.
CREATE INDEX [IX_PostWord_PostId] ON [blog].[PostWord] ([PostId]);
GO
