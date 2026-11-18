-- What a typed word is widened to before it is matched: copilot to copilot, microsoft copilot and github
-- copilot; ms to microsoft only. Azure SQL Database cannot load a full-text thesaurus file, and the search
-- reads blog.PostWord rather than a full-text index, so the widening is a table the query expands through.
-- Its rows are KeywordRules' list, written by the site when the rules' version changes; never typed.
CREATE TABLE [blog].[SearchSynonym] (
    [Word] nvarchar(40) NOT NULL,  -- what the reader typed, lower case
    [Term] nvarchar(80) NOT NULL,  -- one thing it is matched as
    CONSTRAINT [PK_SearchSynonym] PRIMARY KEY ([Word], [Term])
);
GO
