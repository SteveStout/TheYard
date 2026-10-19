-- The words: one row per distinct word of each vehicle's searchable text in Catalogue, so a typed
-- word is a seek on IX_CatalogueWords_Word instead of a leading-wildcard LIKE over every row's
-- text, which cost 4.2 to 4.7 seconds a word on the Basic tier (ADR: The store does the work,
-- the addendum of 19 October). Written beside each Catalogue row by the fill, and for the rows
-- already stored by the site that fills the catalogue, a range of places at a time.
CREATE TABLE [dbo].[CatalogueWords] (
    [Seq]   int            NOT NULL,  -- the vehicle's place, Catalogue's clustered key
    [Word]  nvarchar(64)   NOT NULL,  -- one lowercased word of Catalogue.SearchText
    CONSTRAINT [PK_CatalogueWords] PRIMARY KEY CLUSTERED ([Seq], [Word])
);
GO

-- A typed word of three characters or more is a range on this index, "hon" to every word that
-- starts with it, and hands back the places, which the clustered keys of both tables then read.
CREATE INDEX [IX_CatalogueWords_Word] ON [dbo].[CatalogueWords] ([Word], [Seq]);
GO
